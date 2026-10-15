import { Injectable } from '@nestjs/common';
import { QuestStatus } from '@prisma/client';
import {
  bangkokDay,
  CHECKIN_TRACK,
  checkinReward,
  dailyMissionsFor,
  ErrorCode,
  levelForXp,
  MissionDef,
  MissionObjective,
  missionPeriodEndsAt,
  missionPeriodKey,
  missionPeriodOfKey,
  missionsForPeriod,
  nextBangkokMidnight,
  nextCheckinStep,
  previousMissionPeriodKey,
  RewardItem,
  WEEKLY_MISSIONS,
} from '@hwo/shared-contracts';
import { AppException } from '../common/app-exception';
import { AuditService } from '../audit/audit.service';
import { AppConfig } from '../config/app-config.service';
import { InventoryService } from '../inventory/inventory.service';
import { PrismaService, Tx } from '../prisma/prisma.service';
import { WalletService } from '../wallet/wallet.service';

interface MissionRow {
  periodKey: string;
  missionId: string;
  progress: number;
  status: QuestStatus;
  claimedAt: Date | null;
}

/**
 * Phase 11 — daily check-in (7-day track) and daily/weekly missions.
 *
 * - The day is always the server's Bangkok day; the client never says what day it is.
 * - A check-in is one conditional upsert on `player_checkins` (`last_day < today`): of two parallel
 *   check-ins only one gets a row back. `daily_checkins` (PK player+day) and the ledger idempotency keys
 *   are second and third guards.
 * - Mission progress is counted by QuestsService.recordEvent, inside the transaction of the action that
 *   earned it (selling, exploring, crafting ...), so a mission can't count something that rolled back.
 * - Gold goes through WalletService, items through InventoryService: the same ledgers as everything else.
 */
@Injectable()
export class DailyRewardsService {
  /** Shifts the daily clock (dev/QA via DAILY_CLOCK_OFFSET_HOURS; e2e tests set it directly). */
  clockOffsetMs: number;

  constructor(
    private readonly prisma: PrismaService,
    private readonly wallet: WalletService,
    private readonly inventory: InventoryService,
    private readonly audit: AuditService,
    config: AppConfig,
  ) {
    this.clockOffsetMs = Math.round(config.get('DAILY_CLOCK_OFFSET_HOURS') * 3600_000);
  }

  nowMs(): number {
    return Date.now() + this.clockOffsetMs;
  }

  // ---------- reads ----------

  async state(userId: string) {
    const now = this.nowMs();
    const today = bangkokDay(now);
    const dailyKey = missionPeriodKey('DAILY', now);
    const weeklyKey = missionPeriodKey('WEEKLY', now);
    const prevDailyKey = previousMissionPeriodKey('DAILY', now);
    const prevWeeklyKey = previousMissionPeriodKey('WEEKLY', now);

    const [checkin, rows] = await Promise.all([
      this.prisma.playerCheckin.findUnique({ where: { userId } }),
      this.prisma.missionProgress.findMany({
        where: { userId, periodKey: { in: [dailyKey, weeklyKey, prevDailyKey, prevWeeklyKey] } },
        select: { periodKey: true, missionId: true, progress: true, status: true, claimedAt: true },
      }),
    ]);
    const byKey = new Map(rows.map((r) => [`${r.periodKey}|${r.missionId}`, r]));

    const claimedToday = checkin?.lastDay === today;
    // the step that belongs to today: already claimed, or the one a check-in now would land on
    const todayStep = claimedToday ? checkin!.step : nextCheckinStep(checkin?.step ?? null);
    const checkinView = {
      today,
      claimedToday,
      todayStep,
      totalDays: checkin?.totalDays ?? 0,
      nextResetAt: new Date(nextBangkokMidnight(now)).toISOString(),
      track: CHECKIN_TRACK.map((r) => ({
        step: r.step,
        gold: r.gold,
        item: r.item,
        state: r.step < todayStep || (r.step === todayStep && claimedToday) ? 'CLAIMED' : r.step === todayStep ? 'TODAY' : 'UPCOMING',
      })),
    };

    const view = (m: MissionDef, periodKey: string) => {
      const r = byKey.get(`${periodKey}|${m.id}`);
      return {
        id: m.id,
        periodKey,
        period: m.period,
        objective: m.objective,
        target: m.target,
        progress: r?.progress ?? 0,
        status: r?.status ?? QuestStatus.IN_PROGRESS,
        rewards: { gold: m.rewardGold, xp: m.rewardXp, item: m.rewardItem },
        titleTh: m.titleTh,
        titleEn: m.titleEn,
      };
    };
    // finished but unclaimed missions of the previous day/week stay claimable until the current period ends
    const leftovers = (prevKey: string) =>
      rows
        .filter((r) => r.periodKey === prevKey && r.status === QuestStatus.COMPLETED)
        .map((r) => missionsForPeriod(userId, prevKey).find((m) => m.id === r.missionId))
        .filter((m): m is MissionDef => !!m)
        .map((m) => ({ ...view(m, prevKey), fromPreviousPeriod: true }));

    const daily = [...leftovers(prevDailyKey), ...dailyMissionsFor(userId, today).map((m) => ({ ...view(m, dailyKey), fromPreviousPeriod: false }))];
    const weekly = [...leftovers(prevWeeklyKey), ...WEEKLY_MISSIONS.map((m) => ({ ...view(m, weeklyKey), fromPreviousPeriod: false }))];
    const claimable = daily.concat(weekly).filter((m) => m.status === QuestStatus.COMPLETED).length + (claimedToday ? 0 : 1);

    return {
      now: new Date(now).toISOString(),
      checkin: checkinView,
      daily: { periodKey: dailyKey, endsAt: new Date(missionPeriodEndsAt('DAILY', now)).toISOString(), missions: daily },
      weekly: { periodKey: weeklyKey, endsAt: new Date(missionPeriodEndsAt('WEEKLY', now)).toISOString(), missions: weekly },
      /** things waiting to be collected (check-in + finished missions): drives the HUD badge */
      claimable,
    };
  }

  // ---------- check-in ----------

  async checkin(userId: string) {
    const now = this.nowMs();
    const today = bangkokDay(now);
    return this.prisma.tx(async (tx) => {
      const rows = await tx.$queryRaw<{ step: number; total_days: number }[]>`
        INSERT INTO "player_checkins" ("user_id", "step", "last_day", "total_days", "updated_at")
        VALUES (${userId}::uuid, 1, ${today}, 1, now())
        ON CONFLICT ("user_id") DO UPDATE
           SET "step" = ("player_checkins"."step" % 7) + 1,
               "last_day" = EXCLUDED."last_day",
               "total_days" = "player_checkins"."total_days" + 1,
               "updated_at" = now()
         WHERE "player_checkins"."last_day" < EXCLUDED."last_day"
        RETURNING "step", "total_days"`;
      const row = rows[0];
      if (!row) throw AppException.conflict(ErrorCode.CHECKIN_ALREADY_CLAIMED, 'Already checked in today', { nextResetAt: new Date(nextBangkokMidnight(now)).toISOString() });

      const reward = checkinReward(row.step);
      await tx.dailyCheckin.create({
        data: { userId, bkkDay: today, step: row.step, rewardGold: reward.gold, rewardItemId: reward.item?.itemId ?? null, rewardQty: reward.item?.quantity ?? 0 },
      });
      const ref = { referenceType: 'checkin', referenceId: today };
      if (reward.gold > 0) {
        await this.wallet.applyChange(tx, { userId, currency: 'GOLD', amount: reward.gold, type: 'DAILY_CHECKIN', idempotencyKey: `checkin:${today}:GOLD`, ...ref });
      }
      if (reward.item) {
        await this.inventory.applyChange(tx, {
          userId,
          itemId: reward.item.itemId,
          delta: reward.item.quantity,
          type: 'DAILY_CHECKIN',
          idempotencyKey: `checkin:${today}:${reward.item.itemId}`,
          ...ref,
        });
      }
      await this.record(tx, userId, 'CHECK_IN', 1, now);
      await this.audit.log({ actorUserId: userId, action: 'daily.checkin', targetType: 'checkin', targetId: today, metadata: { step: row.step } }, tx);
      return {
        day: today,
        step: row.step,
        totalDays: row.total_days,
        rewards: { gold: reward.gold, item: reward.item },
        nextResetAt: new Date(nextBangkokMidnight(now)).toISOString(),
        wallet: await this.balances(tx, userId),
      };
    });
  }

  // ---------- missions ----------

  /**
   * Counts `amount` toward every current daily/weekly mission of this objective. Runs inside the caller's
   * transaction. Rows are created on first progress; a COMPLETED/CLAIMED row is never touched again.
   */
  async record(tx: Tx, userId: string, objective: MissionObjective, amount: number, atMs = this.nowMs()): Promise<void> {
    if (!Number.isSafeInteger(amount) || amount <= 0) return;
    const day = bangkokDay(atMs);
    const matches: [MissionDef, string][] = [
      ...dailyMissionsFor(userId, day).map((m): [MissionDef, string] => [m, missionPeriodKey('DAILY', atMs)]),
      ...WEEKLY_MISSIONS.map((m): [MissionDef, string] => [m, missionPeriodKey('WEEKLY', atMs)]),
    ].filter(([m]) => m.objective === objective);
    for (const [m, periodKey] of matches) {
      await tx.$executeRaw`
        INSERT INTO "mission_progress" ("user_id", "period_key", "mission_id", "objective", "target", "progress", "status", "completed_at", "updated_at")
        VALUES (${userId}::uuid, ${periodKey}, ${m.id}, ${m.objective}, ${m.target}::int, LEAST(${m.target}::int, ${amount}::int),
                CASE WHEN ${amount}::int >= ${m.target}::int THEN 'COMPLETED'::"QuestStatus" ELSE 'IN_PROGRESS'::"QuestStatus" END,
                CASE WHEN ${amount}::int >= ${m.target}::int THEN now() END, now())
        ON CONFLICT ("user_id", "period_key", "mission_id") DO UPDATE
           SET "progress" = LEAST("mission_progress"."target", "mission_progress"."progress" + ${amount}::int),
               "status" = CASE WHEN "mission_progress"."progress" + ${amount}::int >= "mission_progress"."target"
                               THEN 'COMPLETED'::"QuestStatus" ELSE 'IN_PROGRESS'::"QuestStatus" END,
               "completed_at" = CASE WHEN "mission_progress"."progress" + ${amount}::int >= "mission_progress"."target" THEN now() END,
               "updated_at" = now()
         WHERE "mission_progress"."status" = 'IN_PROGRESS'::"QuestStatus"`;
    }
  }

  /**
   * Pays a finished mission exactly once. COMPLETED -> CLAIMED is a conditional update (row lock), so
   * parallel claims can't both pass; ledger idempotency keys are the second guard.
   */
  async claimMission(userId: string, periodKey: string, missionId: string) {
    const now = this.nowMs();
    const period = missionPeriodOfKey(periodKey);
    const current = missionPeriodKey(period, now);
    const previous = previousMissionPeriodKey(period, now);
    if (periodKey !== current && periodKey !== previous) {
      if (periodKey > current) throw AppException.notFound('Mission not found');
      throw AppException.conflict(ErrorCode.MISSION_EXPIRED, 'This mission can no longer be claimed');
    }
    const m = missionsForPeriod(userId, periodKey).find((d) => d.id === missionId);
    if (!m) throw new AppException(ErrorCode.MISSION_NOT_FOUND, 'Mission not found', 404);

    return this.prisma.tx(async (tx) => {
      const flipped = await tx.missionProgress.updateMany({
        where: { userId, periodKey, missionId, status: QuestStatus.COMPLETED },
        data: { status: QuestStatus.CLAIMED, claimedAt: new Date() },
      });
      if (flipped.count !== 1) {
        const row: MissionRow | null = await tx.missionProgress.findUnique({ where: { userId_periodKey_missionId: { userId, periodKey, missionId } } });
        if (row?.status === QuestStatus.CLAIMED) throw AppException.conflict(ErrorCode.MISSION_ALREADY_CLAIMED, 'Reward already claimed');
        if (periodKey === previous) throw AppException.conflict(ErrorCode.MISSION_EXPIRED, 'This mission ended before it was finished');
        throw AppException.conflict(ErrorCode.MISSION_NOT_COMPLETED, 'Mission is not finished yet', { progress: row?.progress ?? 0, target: m.target });
      }

      const ref = { referenceType: 'mission', referenceId: `${periodKey}:${missionId}` };
      if (m.rewardGold > 0) {
        await this.wallet.applyChange(tx, { userId, currency: 'GOLD', amount: m.rewardGold, type: 'MISSION_REWARD', idempotencyKey: `mission:${periodKey}:${missionId}:GOLD`, ...ref });
      }
      if (m.rewardItem) await this.giveItem(tx, userId, m.rewardItem, `mission:${periodKey}:${missionId}`, ref);
      let level: number | undefined;
      if (m.rewardXp > 0) {
        const profile = await tx.playerProfile.update({ where: { userId }, data: { xp: { increment: m.rewardXp } } });
        level = levelForXp(profile.xp);
        if (level !== profile.level) await tx.playerProfile.update({ where: { userId }, data: { level } });
      }
      if (m.period === 'DAILY') await this.record(tx, userId, 'DAILY_MISSION_CLAIM', 1, now);
      await this.audit.log({ actorUserId: userId, action: 'mission.claimed', targetType: 'mission', targetId: `${periodKey}:${missionId}` }, tx);
      return {
        periodKey,
        missionId,
        rewards: { gold: m.rewardGold, xp: m.rewardXp, item: m.rewardItem },
        level,
        wallet: await this.balances(tx, userId),
      };
    });
  }

  private async giveItem(tx: Tx, userId: string, item: RewardItem, keyPrefix: string, ref: { referenceType: string; referenceId: string }) {
    await this.inventory.applyChange(tx, { userId, itemId: item.itemId, delta: item.quantity, type: 'MISSION_REWARD', idempotencyKey: `${keyPrefix}:${item.itemId}`, ...ref });
  }

  private async balances(tx: Tx, userId: string) {
    const rows = await tx.playerWallet.findMany({ where: { userId } });
    return Object.fromEntries(rows.map((w) => [w.currency, w.balance]));
  }
}
