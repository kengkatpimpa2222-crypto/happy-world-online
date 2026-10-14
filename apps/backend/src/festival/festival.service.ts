import { HttpStatus, Injectable } from '@nestjs/common';
import { AgeBand } from '@prisma/client';
import {
  activeFestival,
  ErrorCode,
  FESTIVAL_MAIN_DEMAND,
  FESTIVAL_MIN_CONTRIBUTION,
  FESTIVAL_SIDE_DEMAND,
  FESTIVAL_TIERS,
  festivalByKey,
  festivalTiersReached,
  festivalWeek,
  festivalWeekIndex,
  FestivalWeek,
  levelForXp,
  SHOP_TICK_SECONDS,
  WsEvent,
} from '@hwo/shared-contracts';
import { AppException } from '../common/app-exception';
import { AuthUser } from '../common/decorators';
import { AuditService } from '../audit/audit.service';
import { AppConfig } from '../config/app-config.service';
import { PrismaService, Tx } from '../prisma/prisma.service';
import { QuestsService } from '../quests/quests.service';
import { RealtimeService } from '../realtime/realtime.service';
import { SocialGraphService } from '../social/social-graph.service';
import { WalletService } from '../wallet/wallet.service';

/** A festival tier the city just reached; published after the transaction commits. */
export interface FestivalMilestone {
  festivalKey: string;
  code: string;
  ageBand: AgeBand;
  tier: number;
  points: number;
}

export interface FestivalContributionResult {
  /** points this settle added, per festival */
  added: Record<string, number>;
  milestones: FestivalMilestone[];
}

const TOP_HELPERS = 5;

/**
 * Phase 6 — City festivals. The calendar lives in shared-contracts (pure clock arithmetic);
 * this service keeps the city's progress, each player's share, and pays the tier rewards through
 * the same wallet ledger as everything else.
 */
@Injectable()
export class FestivalService {
  /** Shifts the festival calendar (dev/QA via FESTIVAL_CLOCK_OFFSET_HOURS; tests set it directly). */
  clockOffsetMs: number;

  constructor(
    private readonly prisma: PrismaService,
    private readonly wallet: WalletService,
    private readonly quests: QuestsService,
    private readonly realtime: RealtimeService,
    private readonly graph: SocialGraphService,
    private readonly audit: AuditService,
    config: AppConfig,
  ) {
    this.clockOffsetMs = Math.round(config.get('FESTIVAL_CLOCK_OFFSET_HOURS') * 3600_000);
  }

  nowMs(): number {
    return Date.now() + this.clockOffsetMs;
  }

  /** The festival running during a shop tick (used by the NPC customer simulation). */
  readonly festivalAtTick = (tick: number): FestivalWeek | null => activeFestival(tick * SHOP_TICK_SECONDS * 1000 + this.clockOffsetMs);

  // ---------- contributions (called inside the shop settlement transaction) ----------

  /**
   * Adds featured units a player's stall sold to their share and to their city's progress.
   * Runs inside the caller's transaction, so a settlement and its festival points commit together.
   */
  async recordSales(tx: Tx, userId: string, festivalPoints: Record<string, number>): Promise<FestivalContributionResult> {
    const result: FestivalContributionResult = { added: {}, milestones: [] };
    for (const [key, points] of Object.entries(festivalPoints)) {
      if (points <= 0) continue;
      const w = festivalByKey(key);
      if (!w) continue;
      const mine = await tx.$queryRaw<{ age_band: AgeBand; inserted: boolean }[]>`
        INSERT INTO "festival_contributions" ("festival_key", "user_id", "age_band", "points", "updated_at")
        SELECT ${key}, u."id", u."age_band", ${points}, now() FROM "users" u WHERE u."id" = ${userId}::uuid
        ON CONFLICT ("festival_key", "user_id")
        DO UPDATE SET "points" = "festival_contributions"."points" + EXCLUDED."points", "updated_at" = now()
        RETURNING "age_band", (xmax = 0) AS "inserted"`;
      const row = mine[0];
      if (!row) continue;
      const city = await tx.$queryRaw<{ points: number }[]>`
        INSERT INTO "festival_city_progress" ("festival_key", "age_band", "points", "contributors", "updated_at")
        VALUES (${key}, ${row.age_band}::"AgeBand", ${points}, ${row.inserted ? 1 : 0}, now())
        ON CONFLICT ("festival_key", "age_band")
        DO UPDATE SET "points" = "festival_city_progress"."points" + EXCLUDED."points",
                      "contributors" = "festival_city_progress"."contributors" + EXCLUDED."contributors",
                      "updated_at" = now()
        RETURNING "points"`;
      const after = city[0]!.points;
      const before = after - points;
      for (const t of FESTIVAL_TIERS) {
        if (before < t.target && after >= t.target) {
          result.milestones.push({ festivalKey: key, code: w.festival.code, ageBand: row.age_band, tier: t.tier, points: after });
        }
      }
      result.added[key] = points;
      await this.quests.recordEvent(tx, userId, 'FESTIVAL_CONTRIBUTE', points);
    }
    return result;
  }

  /** Tells the whole city (same age band only) that a tier was reached: clients decorate the city. */
  publishMilestones(milestones: FestivalMilestone[]): void {
    for (const m of milestones) {
      this.realtime.toCity(m.ageBand, WsEvent.FESTIVAL_MILESTONE, {
        festivalKey: m.festivalKey,
        code: m.code,
        tier: m.tier,
        points: m.points,
      });
    }
  }

  // ---------- reads ----------

  /** This week's festival (upcoming or running) and, if the player still has rewards there, last week's. */
  async state(user: AuthUser) {
    const now = this.nowMs();
    const week = festivalWeekIndex(now);
    const current = await this.view(user, festivalWeek(week), now, true);
    const prev = await this.view(user, festivalWeek(week - 1), now, false);
    return {
      now: new Date(now).toISOString(),
      current,
      previous: prev.tiers.some((t) => t.claimable) ? prev : null,
    };
  }

  private async view(user: AuthUser, w: FestivalWeek, now: number, withTop: boolean) {
    const [city, mine, claims] = await Promise.all([
      this.prisma.festivalCityProgress.findUnique({ where: { festivalKey_ageBand: { festivalKey: w.key, ageBand: user.ageBand } } }),
      this.prisma.festivalContribution.findUnique({ where: { festivalKey_userId: { festivalKey: w.key, userId: user.userId } } }),
      this.prisma.festivalRewardClaim.findMany({ where: { festivalKey: w.key, userId: user.userId }, select: { tier: true } }),
    ]);
    const points = city?.points ?? 0;
    const myPoints = mine?.points ?? 0;
    const claimed = new Set(claims.map((c) => c.tier));
    const eligible = myPoints >= FESTIVAL_MIN_CONTRIBUTION;
    const status = now < w.startsAt ? 'UPCOMING' : now < w.endsAt ? 'ACTIVE' : 'ENDED';
    return {
      key: w.key,
      code: w.festival.code,
      nameKey: w.festival.nameKey,
      nameTh: w.festival.nameTh,
      color: w.festival.color,
      status,
      startsAt: new Date(w.startsAt).toISOString(),
      endsAt: new Date(w.endsAt).toISOString(),
      featured: [
        { productCode: w.festival.mainProduct, demand: FESTIVAL_MAIN_DEMAND },
        { productCode: w.festival.sideProduct, demand: FESTIVAL_SIDE_DEMAND },
      ],
      rewardItemId: w.festival.rewardItemId,
      city: { points, contributors: city?.contributors ?? 0, tiersReached: festivalTiersReached(points) },
      tiers: FESTIVAL_TIERS.map((t) => ({
        tier: t.tier,
        target: t.target,
        reached: points >= t.target,
        rewardGold: t.rewardGold,
        rewardXp: t.rewardXp,
        rewardItemId: t.rewardItem ? w.festival.rewardItemId : null,
        claimed: claimed.has(t.tier),
        claimable: points >= t.target && eligible && !claimed.has(t.tier),
      })),
      me: { points: myPoints, minToClaim: FESTIVAL_MIN_CONTRIBUTION },
      top: withTop ? await this.topHelpers(user, w.key) : [],
    };
  }

  /**
   * The city's top helpers, same age band only and never a blocked player. Under-13 players see
   * ranks and points but no names (child safety), except their own entry.
   */
  private async topHelpers(user: AuthUser, key: string) {
    const blocked = await this.graph.blockedEitherWay(user.userId);
    const rows = await this.prisma.festivalContribution.findMany({
      where: { festivalKey: key, ageBand: user.ageBand, points: { gt: 0 } },
      orderBy: [{ points: 'desc' }, { createdAt: 'asc' }],
      take: TOP_HELPERS + blocked.size,
      select: { userId: true, points: true, user: { select: { profile: { select: { displayName: true } } } } },
    });
    const hideNames = user.ageBand === 'UNDER_13';
    return rows
      .filter((r) => r.userId === user.userId || !blocked.has(r.userId))
      .slice(0, TOP_HELPERS)
      .map((r, i) => ({
        rank: i + 1,
        displayName: r.userId === user.userId || !hideNames ? (r.user.profile?.displayName ?? '') : null,
        points: r.points,
        isMe: r.userId === user.userId,
      }));
  }

  // ---------- rewards ----------

  async claim(user: AuthUser, festivalKey: string, tier: number) {
    const w = festivalByKey(festivalKey);
    const tierDef = FESTIVAL_TIERS.find((t) => t.tier === tier);
    const week = festivalWeekIndex(this.nowMs());
    if (!w || !tierDef || w.weekIndex > week) throw new AppException(ErrorCode.FESTIVAL_NOT_FOUND, 'Festival not found', HttpStatus.NOT_FOUND);
    if (w.weekIndex < week - 1) throw AppException.conflict(ErrorCode.FESTIVAL_CLAIM_EXPIRED, 'Rewards of this festival can no longer be claimed');

    return this.prisma.tx(async (tx) => {
      const mine = await tx.festivalContribution.findUnique({ where: { festivalKey_userId: { festivalKey, userId: user.userId } } });
      if (!mine || mine.points < FESTIVAL_MIN_CONTRIBUTION) {
        throw AppException.conflict(ErrorCode.FESTIVAL_NOT_ENOUGH_CONTRIBUTION, `Sell at least ${FESTIVAL_MIN_CONTRIBUTION} festival goods to share the city's rewards`, {
          points: mine?.points ?? 0,
          required: FESTIVAL_MIN_CONTRIBUTION,
        });
      }
      const city = await tx.festivalCityProgress.findUnique({ where: { festivalKey_ageBand: { festivalKey, ageBand: mine.ageBand } } });
      if ((city?.points ?? 0) < tierDef.target) throw AppException.conflict(ErrorCode.FESTIVAL_TIER_NOT_REACHED, 'The city has not reached this tier yet');

      const created = await tx.festivalRewardClaim.createMany({ data: [{ festivalKey, userId: user.userId, tier }], skipDuplicates: true });
      if (created.count !== 1) throw AppException.conflict(ErrorCode.FESTIVAL_ALREADY_CLAIMED, 'Reward already claimed');

      const ref = { referenceType: 'festival', referenceId: `${festivalKey}:${tier}` };
      await this.wallet.applyChange(tx, {
        userId: user.userId,
        currency: 'GOLD',
        amount: tierDef.rewardGold,
        type: 'FESTIVAL_REWARD',
        idempotencyKey: `festival:${festivalKey}:${tier}`,
        ...ref,
      });
      const profile = await tx.playerProfile.update({ where: { userId: user.userId }, data: { xp: { increment: tierDef.rewardXp } } });
      const level = levelForXp(profile.xp);
      if (level !== profile.level) await tx.playerProfile.update({ where: { userId: user.userId }, data: { level } });
      const itemId = tierDef.rewardItem ? w.festival.rewardItemId : null;
      if (itemId) {
        await tx.playerInventory.upsert({
          where: { userId_itemId: { userId: user.userId, itemId } },
          create: { userId: user.userId, itemId, quantity: 1 },
          update: { quantity: { increment: 1 } },
        });
      }
      await this.audit.log({ actorUserId: user.userId, action: 'festival.claimed', targetType: 'festival', targetId: `${festivalKey}:${tier}` }, tx);
      const balances = await tx.playerWallet.findMany({ where: { userId: user.userId } });
      return {
        festivalKey,
        tier,
        rewards: { gold: tierDef.rewardGold, xp: tierDef.rewardXp, itemId },
        level,
        wallet: Object.fromEntries(balances.map((b) => [b.currency, b.balance])),
      };
    });
  }
}
