import { Injectable } from '@nestjs/common';
import {
  BUSINESS_PRODUCTS,
  BusinessProductCode,
  bangkokDay,
  businessLevelConfig,
  ErrorCode,
  levelForXp,
  MINIGAME_CODES,
  MINIGAME_DAILY_REWARDED_PLAYS,
  MINIGAME_EARLY_TOLERANCE_SECONDS,
  MINIGAME_SESSION_TTL_SECONDS,
  MINIGAME_UNITS_BY_STARS,
  MINIGAME_XP_BY_STARS,
  MINIGAMES,
  MinigameCode,
  minigameForProduct,
  minigameStars,
  nextBangkokMidnight,
} from '@hwo/shared-contracts';
import { MinigameSession } from '@prisma/client';
import { randomInt } from 'node:crypto';
import { AppException } from '../common/app-exception';
import { PrismaService, Tx } from '../prisma/prisma.service';
import { QuestsService } from '../quests/quests.service';
import { weightedAvgCost } from './business-sim';
import { BusinessService } from './business.service';
import { LivingShopService, SettleResult } from './living-shop.service';

/**
 * Mini-games: make goods for your own shop by hand (see shared-contracts/minigame.ts for the rules).
 * The server hands out each round's seed, refuses rounds that ended too early or long ago, caps the score
 * and decides stars and goods. Finishing is idempotent: the round id is the key, a retry gets the same result.
 */
@Injectable()
export class MinigameService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly shop: LivingShopService,
    private readonly business: BusinessService,
    private readonly quests: QuestsService,
  ) {}

  /** Today's limit, best scores and the rules of each game. */
  async overview(userId: string) {
    const now = Date.now();
    const used = await this.rewardedToday(this.prisma, userId, bangkokDay(now));
    const best = await this.prisma.minigameSession.groupBy({ by: ['game'], where: { userId, score: { not: null } }, _max: { score: true } });
    return {
      dailyRewardedPlays: MINIGAME_DAILY_REWARDED_PLAYS,
      rewardedPlaysLeft: Math.max(0, MINIGAME_DAILY_REWARDED_PLAYS - used),
      resetsAt: new Date(nextBangkokMidnight(now)).toISOString(),
      unitsByStars: MINIGAME_UNITS_BY_STARS,
      games: MINIGAME_CODES.map((g) => ({
        game: g,
        durationSeconds: MINIGAMES[g].durationSeconds,
        starScores: MINIGAMES[g].starScores,
        products: MINIGAMES[g].products,
        best: best.find((b) => b.game === g)?._max.score ?? 0,
      })),
    };
  }

  async start(userId: string, businessId: string, productCode: BusinessProductCode) {
    if (BUSINESS_PRODUCTS[productCode].craftOnly) {
      throw AppException.badRequest(ErrorCode.NO_MINIGAME_FOR_PRODUCT, 'This product is made by crafting, not in a mini-game', { productCode });
    }
    const game = minigameForProduct(productCode);
    const now = Date.now();
    const session = await this.prisma.tx(async (tx) => {
      const shop = await this.shop.lock(tx, userId, businessId); // 404 for someone else's shop
      const unlockLevel = BUSINESS_PRODUCTS[productCode].unlockLevel;
      if (unlockLevel > shop.level) {
        throw AppException.conflict(ErrorCode.BUSINESS_PRODUCT_LOCKED, `Unlocks at shop level ${unlockLevel}`, { unlockLevel });
      }
      return tx.minigameSession.create({
        data: { userId, businessId, game, productCode, seed: randomInt(1, 2_147_483_647), bkkDay: bangkokDay(now), startedAt: new Date(now) },
      });
    });
    const used = await this.rewardedToday(this.prisma, userId, session.bkkDay);
    return {
      sessionId: session.id,
      game,
      productCode,
      seed: session.seed,
      durationSeconds: MINIGAMES[game].durationSeconds,
      starScores: MINIGAMES[game].starScores,
      unitsByStars: MINIGAME_UNITS_BY_STARS,
      rewardedPlaysLeft: Math.max(0, MINIGAME_DAILY_REWARDED_PLAYS - used),
    };
  }

  async finish(userId: string, sessionId: string, score: number) {
    const settled: SettleResult[] = [];
    const result = await this.prisma.tx(async (tx) => {
      settled.length = 0;
      // one player's rounds finish one at a time, so the daily limit can't be overrun by parallel requests
      await tx.$queryRaw`SELECT 1 FROM "player_profiles" WHERE "user_id" = ${userId}::uuid FOR UPDATE`;
      const rows = await tx.$queryRaw<{ id: string }[]>`
        SELECT "id" FROM "minigame_sessions" WHERE "id" = ${sessionId}::uuid AND "user_id" = ${userId}::uuid FOR UPDATE`;
      if (!rows[0]) throw AppException.notFound('Mini-game round not found');
      const s = await tx.minigameSession.findUniqueOrThrow({ where: { id: sessionId } });
      if (s.finishedAt) return { s, replayed: true, stockFull: false, level: null as number | null };

      const game = s.game as MinigameCode;
      const def = MINIGAMES[game];
      const elapsed = (Date.now() - s.startedAt.getTime()) / 1000;
      if (elapsed < def.durationSeconds - MINIGAME_EARLY_TOLERANCE_SECONDS) {
        throw AppException.conflict(ErrorCode.MINIGAME_TOO_EARLY, 'The round is not over yet', {
          secondsLeft: Math.ceil(def.durationSeconds - elapsed),
        });
      }
      if (elapsed > def.durationSeconds + MINIGAME_SESSION_TTL_SECONDS) {
        throw AppException.conflict(ErrorCode.MINIGAME_EXPIRED, 'This round ended too long ago');
      }
      if (score > def.maxScore) throw AppException.badRequest(ErrorCode.MINIGAME_SCORE_INVALID, 'Score is not possible', { maxScore: def.maxScore });

      const stars = minigameStars(game, score);
      const rewarded = (await this.rewardedToday(tx, userId, s.bkkDay)) < MINIGAME_DAILY_REWARDED_PLAYS;
      let units = rewarded ? MINIGAME_UNITS_BY_STARS[stars] : 0;
      const xp = rewarded ? MINIGAME_XP_BY_STARS[stars] : 0;
      let stockFull = false;
      let level: number | null = null;

      if (units > 0) {
        const shop = await this.shop.lock(tx, userId, s.businessId);
        const res = await this.shop.settleLocked(tx, shop);
        if (res) settled.push(res);
        const code = s.productCode as BusinessProductCode;
        const line = await tx.businessInventory.findUnique({ where: { businessId_productCode: { businessId: s.businessId, productCode: code } } });
        const current = line?.quantity ?? 0;
        const room = Math.max(0, businessLevelConfig(shop.level).stockCapacity - current);
        stockFull = units > room;
        units = Math.min(units, room);
        if (units > 0) {
          await tx.businessInventory.upsert({
            where: { businessId_productCode: { businessId: s.businessId, productCode: code } },
            create: { businessId: s.businessId, productCode: code, quantity: units, avgUnitCost: 0, sellPrice: BUSINESS_PRODUCTS[code].basePrice },
            // homemade goods cost no Gold: the shop's average cost per unit goes down
            update: {
              quantity: { increment: units },
              avgUnitCost: weightedAvgCost(current + (line?.shelfQuantity ?? 0), line?.avgUnitCost ?? 0, units, 0),
            },
          });
        }
      }
      if (xp > 0) {
        const profile = await tx.playerProfile.update({ where: { userId }, data: { xp: { increment: xp } } });
        level = levelForXp(profile.xp);
        if (level !== profile.level) await tx.playerProfile.update({ where: { userId }, data: { level } });
      }
      await this.quests.recordEvent(tx, userId, 'PLAY_MINIGAME', 1);
      const done = await tx.minigameSession.update({
        where: { id: sessionId },
        data: { finishedAt: new Date(), score, stars, rewarded, unitsAwarded: units, xpAwarded: xp },
      });
      return { s: done, replayed: false, stockFull, level };
    });
    settled.forEach((r) => this.shop.publish(r));

    const s = result.s;
    const used = await this.rewardedToday(this.prisma, userId, bangkokDay(Date.now()));
    const best = await this.prisma.minigameSession.aggregate({ where: { userId, game: s.game, score: { not: null } }, _max: { score: true } });
    return {
      ...this.present(s),
      replayed: result.replayed,
      stockFull: result.stockFull,
      level: result.level,
      best: best._max.score ?? 0,
      rewardedPlaysLeft: Math.max(0, MINIGAME_DAILY_REWARDED_PLAYS - used),
      business: await this.business.get(userId, s.businessId),
    };
  }

  private present(s: MinigameSession) {
    return {
      sessionId: s.id,
      game: s.game,
      productCode: s.productCode,
      score: s.score ?? 0,
      stars: s.stars ?? 0,
      rewarded: s.rewarded,
      unitsAwarded: s.unitsAwarded,
      xpAwarded: s.xpAwarded,
    };
  }

  private rewardedToday(db: Tx | PrismaService, userId: string, day: string): Promise<number> {
    return db.minigameSession.count({ where: { userId, bkkDay: day, rewarded: true } });
  }
}
