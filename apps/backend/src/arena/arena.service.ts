import { Injectable } from '@nestjs/common';
import {
  ARENA_DAILY_REWARDED_MATCHES,
  ARENA_REWARD_GOLD,
  ARENA_START_RATING,
  ARENA_TIER_NAMES_TH,
  arenaCarryOver,
  arenaNextTierAt,
  arenaRatingDelta,
  arenaSeason,
  arenaSeasonEndsAt,
  arenaTier,
  ArenaOutcome,
  ArenaTeam,
  bangkokDay,
  nextBangkokMidnight,
} from '@hwo/shared-contracts';
import { AgeBand, ArenaRating, Prisma } from '@prisma/client';
import { AuthUser } from '../common/decorators';
import { PrismaService, Tx } from '../prisma/prisma.service';
import { WalletService } from '../wallet/wallet.service';

export interface MatchSheetPlayer {
  userId: string;
  team: ArenaTeam;
  /** rating when the match was made (team averages use it) */
  rating: number;
  leftEarly: boolean;
  soaks: number;
  soakedTimes: number;
}

export interface MatchSheet {
  id: string;
  ageBand: AgeBand;
  withBots: boolean;
  startedAt: Date;
  endedAt: Date;
  score: Record<ArenaTeam, number>;
  outcome: Record<ArenaTeam, ArenaOutcome>;
  /** average rating per team including bots (a bot counts as the average of the real players) */
  teamAvg: Record<ArenaTeam, number>;
  players: MatchSheetPlayer[];
}

export interface PlayerMatchResult {
  userId: string;
  team: ArenaTeam;
  outcome: ArenaOutcome;
  leftEarly: boolean;
  ratingBefore: number;
  ratingAfter: number;
  delta: number;
  tier: string;
  tierBefore: string;
  rewardGold: number;
}

/**
 * Splash Arena ranks (shared-contracts/arena.ts): one rating row per player and season, results and Gold
 * written in one transaction per match. Rewards use the wallet ledger key `arena:<matchId>`, so a result can
 * never pay twice.
 */
@Injectable()
export class ArenaService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly wallet: WalletService,
  ) {}

  /** This season's rating row, created on first use from last season's rating (soft reset). */
  async standing(db: Tx | PrismaService, userId: string, ageBand: AgeBand, now = Date.now()): Promise<ArenaRating> {
    const season = arenaSeason(now);
    const found = await db.arenaRating.findUnique({ where: { userId_season: { userId, season } } });
    if (found) return found;
    const last = await db.arenaRating.findFirst({ where: { userId, season: { lt: season } }, orderBy: { season: 'desc' } });
    const rating = last ? arenaCarryOver(last.rating) : ARENA_START_RATING;
    try {
      return await db.arenaRating.create({ data: { userId, season, ageBand, rating, peak: rating } });
    } catch (e) {
      // created by a parallel request
      if (e instanceof Prisma.PrismaClientKnownRequestError && e.code === 'P2002') {
        return db.arenaRating.findUniqueOrThrow({ where: { userId_season: { userId, season } } });
      }
      throw e;
    }
  }

  current(user: AuthUser): Promise<ArenaRating> {
    return this.standing(this.prisma, user.userId, user.ageBand);
  }

  async me(user: AuthUser) {
    const now = Date.now();
    const r = await this.standing(this.prisma, user.userId, user.ageBand, now);
    const day = bangkokDay(now);
    const [ahead, recent, rewardedToday] = await Promise.all([
      this.prisma.arenaRating.count({ where: { season: r.season, ageBand: r.ageBand, rating: { gt: r.rating } } }),
      this.prisma.arenaMatchPlayer.findMany({
        where: { userId: user.userId },
        orderBy: { match: { endedAt: 'desc' } },
        take: 5,
        include: { match: { select: { endedAt: true, scoreRed: true, scoreBlue: true, withBots: true } } },
      }),
      this.rewardedOn(this.prisma, user.userId, day),
    ]);
    return {
      season: r.season,
      seasonEndsAt: new Date(arenaSeasonEndsAt(now)).toISOString(),
      ...this.presentRating(r),
      position: ahead + 1,
      rewardedMatchesLeft: Math.max(0, ARENA_DAILY_REWARDED_MATCHES - rewardedToday),
      rewardsResetAt: new Date(nextBangkokMidnight(now)).toISOString(),
      rewardGold: ARENA_REWARD_GOLD,
      recent: recent.map((p) => ({
        matchId: p.matchId,
        endedAt: p.match.endedAt.toISOString(),
        team: p.team,
        outcome: p.outcome,
        leftEarly: p.leftEarly,
        score: { RED: p.match.scoreRed, BLUE: p.match.scoreBlue },
        withBots: p.match.withBots,
        soaks: p.soaks,
        delta: p.ratingAfter - p.ratingBefore,
        rewardGold: p.rewardGold,
      })),
    };
  }

  /** Top players of my age band this season (display names only, like the rest of the social world). */
  async leaderboard(user: AuthUser, limit: number) {
    const season = arenaSeason(Date.now());
    const rows = await this.prisma.arenaRating.findMany({
      where: { season, ageBand: user.ageBand, user: { deletedAt: null } },
      orderBy: [{ rating: 'desc' }, { updatedAt: 'asc' }],
      take: limit,
      include: { user: { select: { profile: { select: { displayName: true, level: true } } } } },
    });
    return {
      season,
      entries: rows.map((r, i) => ({
        position: i + 1,
        userId: r.userId,
        displayName: r.user.profile?.displayName ?? '',
        level: r.user.profile?.level ?? 1,
        isMe: r.userId === user.userId,
        ...this.presentRating(r),
      })),
    };
  }

  /** Writes a finished match: ratings, result sheet and participation Gold, all or nothing. */
  async record(sheet: MatchSheet): Promise<PlayerMatchResult[]> {
    const season = arenaSeason(sheet.startedAt.getTime());
    const day = bangkokDay(sheet.endedAt.getTime());
    return this.prisma.tx(async (tx) => {
      const winner = sheet.outcome.RED === 'DRAW' ? 'DRAW' : sheet.outcome.RED === 'WIN' ? 'RED' : 'BLUE';
      await tx.arenaMatch.create({
        data: {
          id: sheet.id,
          season,
          ageBand: sheet.ageBand,
          withBots: sheet.withBots,
          scoreRed: sheet.score.RED,
          scoreBlue: sheet.score.BLUE,
          winner,
          startedAt: sheet.startedAt,
          endedAt: sheet.endedAt,
        },
      });
      const results: PlayerMatchResult[] = [];
      // same order every time, so two matches finishing together can't deadlock on rating rows
      for (const p of [...sheet.players].sort((a, b) => a.userId.localeCompare(b.userId))) {
        const other: ArenaTeam = p.team === 'RED' ? 'BLUE' : 'RED';
        const outcome: ArenaOutcome = p.leftEarly ? 'LOSS' : sheet.outcome[p.team];
        const delta = arenaRatingDelta(sheet.teamAvg[p.team], sheet.teamAvg[other], outcome, sheet.withBots);

        const row = await this.standing(tx, p.userId, sheet.ageBand, sheet.startedAt.getTime());
        await tx.$queryRaw`SELECT 1 FROM "arena_ratings" WHERE "user_id" = ${p.userId}::uuid AND "season" = ${row.season} FOR UPDATE`;
        const fresh = await tx.arenaRating.findUniqueOrThrow({ where: { userId_season: { userId: p.userId, season: row.season } } });
        const after = Math.max(0, fresh.rating + delta);
        await tx.arenaRating.update({
          where: { userId_season: { userId: p.userId, season: row.season } },
          data: {
            rating: after,
            peak: Math.max(fresh.peak, after),
            wins: { increment: outcome === 'WIN' ? 1 : 0 },
            losses: { increment: outcome === 'LOSS' ? 1 : 0 },
            draws: { increment: outcome === 'DRAW' ? 1 : 0 },
          },
        });

        let rewardGold = 0;
        if (!p.leftEarly && (await this.rewardedOn(tx, p.userId, day)) < ARENA_DAILY_REWARDED_MATCHES) {
          rewardGold = ARENA_REWARD_GOLD[outcome];
          await this.wallet.applyChange(tx, {
            userId: p.userId,
            currency: 'GOLD',
            amount: rewardGold,
            type: 'ARENA_REWARD',
            idempotencyKey: `arena:${sheet.id}`,
            referenceType: 'arena_match',
            referenceId: sheet.id,
          });
        }
        await tx.arenaMatchPlayer.create({
          data: {
            matchId: sheet.id,
            userId: p.userId,
            team: p.team,
            outcome,
            leftEarly: p.leftEarly,
            soaks: p.soaks,
            soakedTimes: p.soakedTimes,
            ratingBefore: fresh.rating,
            ratingAfter: after,
            rewardGold,
            bkkDay: day,
          },
        });
        results.push({
          userId: p.userId,
          team: p.team,
          outcome,
          leftEarly: p.leftEarly,
          ratingBefore: fresh.rating,
          ratingAfter: after,
          delta: after - fresh.rating,
          tierBefore: arenaTier(fresh.rating),
          tier: arenaTier(after),
          rewardGold,
        });
      }
      return results;
    });
  }

  private rewardedOn(db: Tx | PrismaService, userId: string, day: string): Promise<number> {
    return db.arenaMatchPlayer.count({ where: { userId, bkkDay: day, rewardGold: { gt: 0 } } });
  }

  private presentRating(r: ArenaRating) {
    const tier = arenaTier(r.rating);
    return {
      rating: r.rating,
      peak: r.peak,
      tier,
      tierNameTh: ARENA_TIER_NAMES_TH[tier],
      nextTierAt: arenaNextTierAt(r.rating),
      wins: r.wins,
      losses: r.losses,
      draws: r.draws,
    };
  }
}
