import { Injectable } from '@nestjs/common';
import {
  answerLength,
  bangkokDay,
  checkTask,
  ErrorCode,
  JOB_CODES,
  JOB_DAILY_PAID_SHIFTS,
  JOB_SHIFT_TTL_SECONDS,
  JobCode,
  jobMinSeconds,
  JOBS,
  jobTasks,
  MATERIAL_CODES,
  MaterialBag,
  materialItemId,
  nextBangkokMidnight,
  ShiftPay,
  shiftPay,
  shiftProduce,
  shiftWorkerXp,
  TaskCheck,
} from '@hwo/shared-contracts';
import { JobShift } from '@prisma/client';
import { randomInt } from 'node:crypto';
import { SkillsService } from '../adventure/skills.service';
import { AppException } from '../common/app-exception';
import { InventoryService } from '../inventory/inventory.service';
import { PrismaService, Tx } from '../prisma/prisma.service';
import { DailyRewardsService } from '../quests/daily-rewards.service';
import { QuestsService } from '../quests/quests.service';
import { WalletService } from '../wallet/wallet.service';

export interface StoredShiftResult {
  checks: (TaskCheck & { answer: number[] })[];
  /** the payslip as earned; `wageGold` is what was really paid (0 on a practice shift) */
  pay: ShiftPay;
  wageGold: number;
  produce: MaterialBag;
  workerXp: number;
  playerXp: number;
  paid: boolean;
  playerLevel: number | null;
  workerLevelAfter: number;
  workerLeveledUp: boolean;
}

/**
 * Part-time jobs (see shared-contracts/jobs.ts). One open shift per player; the tasks are rebuilt from the
 * shift's seed, every answer is checked here and the pay goes through the wallet and item ledgers with keys
 * `job:<shiftId>:<GOLD|material>`, so handing a shift in twice pays once and returns the same payslip.
 */
@Injectable()
export class JobsService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly wallet: WalletService,
    private readonly inventory: InventoryService,
    private readonly skills: SkillsService,
    private readonly quests: QuestsService,
    private readonly daily: DailyRewardsService,
  ) {}

  /** The job boards, today's paid shifts left, my Worker skill, an open shift to resume and my last payslips. */
  async overview(userId: string) {
    const now = this.daily.nowMs();
    const [skills, active, recent, totals, paidToday] = await Promise.all([
      this.skills.all(this.prisma, userId),
      this.prisma.jobShift.findFirst({ where: { userId, status: 'ACTIVE' } }),
      this.prisma.jobShift.findMany({ where: { userId, status: 'DONE' }, orderBy: { finishedAt: 'desc' }, take: 5 }),
      this.prisma.jobShift.aggregate({ where: { userId, status: 'DONE', paid: true }, _count: true, _sum: { wageGold: true } }),
      this.paidOn(this.prisma, userId, bangkokDay(now)),
    ]);
    const worker = skills.WORKER;
    return {
      jobs: JOB_CODES.map((job) => {
        const d = JOBS[job];
        return {
          job,
          zone: d.zone,
          x: d.x,
          y: d.y,
          nameTh: d.nameTh,
          placeTh: d.placeTh,
          skillTh: d.skillTh,
          workerLevel: d.workerLevel,
          unlocked: worker.level >= d.workerLevel,
          tasksPerShift: d.tasksPerShift,
          wagePerTask: d.wagePerTask,
          perfectBonus: d.perfectBonus,
          shiftBonus: d.shiftBonus,
          produce: d.produce,
        };
      }),
      paidShiftsPerDay: JOB_DAILY_PAID_SHIFTS,
      paidShiftsLeft: Math.max(0, JOB_DAILY_PAID_SHIFTS - paidToday),
      resetsAt: new Date(nextBangkokMidnight(now)).toISOString(),
      worker,
      active: active && !this.expired(active) ? this.present(active) : null,
      recent: recent.map((s) => this.present(s)),
      totals: { paidShifts: totals._count, goldEarned: totals._sum.wageGold ?? 0 },
    };
  }

  /** Starts a shift, or hands back the open one of the same job (resume after closing the app). */
  async start(userId: string, job: JobCode) {
    const day = bangkokDay(this.daily.nowMs());
    const out = await this.prisma.tx(async (tx) => {
      // one player's shifts start one at a time (the partial unique index is the second guard)
      await tx.$queryRaw`SELECT 1 FROM "player_profiles" WHERE "user_id" = ${userId}::uuid FOR UPDATE`;
      const active = await tx.jobShift.findFirst({ where: { userId, status: 'ACTIVE' } });
      if (active && this.expired(active)) {
        await tx.jobShift.update({ where: { id: active.id }, data: { status: 'EXPIRED', finishedAt: new Date() } });
      } else if (active) {
        if (active.job === job) return { shift: active, resumed: true };
        throw AppException.conflict(ErrorCode.JOB_SHIFT_IN_PROGRESS, 'Finish your other shift first', { shiftId: active.id, job: active.job });
      }
      const workerLevel = await this.skills.level(tx, userId, 'WORKER');
      const need = JOBS[job].workerLevel;
      if (workerLevel < need) throw AppException.conflict(ErrorCode.JOB_LOCKED, `Needs Worker level ${need}`, { workerLevel: need });
      const shift = await tx.jobShift.create({ data: { userId, job, seed: randomInt(1, 2_147_483_647), bkkDay: day, workerLevel } });
      return { shift, resumed: false };
    });
    const paidToday = await this.paidOn(this.prisma, userId, out.shift.bkkDay);
    return { ...this.present(out.shift), resumed: out.resumed, paidShiftsLeft: Math.max(0, JOB_DAILY_PAID_SHIFTS - paidToday) };
  }

  /** Hands a shift in: checks every answer, pays (within the daily limit) and explains each task. */
  async finish(userId: string, id: string, answers: number[][]) {
    const out = await this.prisma.tx(async (tx) => {
      // the daily paid-shift count must not be overrun by two shifts handed in at once
      await tx.$queryRaw`SELECT 1 FROM "player_profiles" WHERE "user_id" = ${userId}::uuid FOR UPDATE`;
      const rows = await tx.$queryRaw<{ id: string }[]>`
        SELECT "id" FROM "job_shifts" WHERE "id" = ${id}::uuid AND "user_id" = ${userId}::uuid FOR UPDATE`;
      if (!rows[0]) throw AppException.notFound('Shift not found');
      const shift = await tx.jobShift.findUniqueOrThrow({ where: { id } });
      if (shift.status === 'DONE') return { shift, replayed: true };
      if (shift.status === 'EXPIRED' || this.expired(shift)) throw AppException.conflict(ErrorCode.JOB_SHIFT_EXPIRED, 'This shift ended too long ago');

      const job = shift.job as JobCode;
      const elapsed = (Date.now() - shift.startedAt.getTime()) / 1000;
      if (elapsed < jobMinSeconds(job)) {
        throw AppException.conflict(ErrorCode.JOB_SHIFT_TOO_EARLY, 'Take a little more time on the tasks', { secondsLeft: Math.ceil(jobMinSeconds(job) - elapsed) });
      }
      const tasks = jobTasks(job, shift.seed);
      if (answers.length !== tasks.length || tasks.some((t, i) => answers[i]!.length !== answerLength(t))) {
        throw AppException.badRequest(ErrorCode.JOB_ANSWER_INVALID, 'One answer per task, in the shape of the task', {
          lengths: tasks.map(answerLength),
        });
      }

      const checks = tasks.map((t, i) => ({ ...checkTask(t, answers[i]!), answer: answers[i]! }));
      const correct = checks.filter((c) => c.correct).length;
      const pay = shiftPay(job, checks, shift.workerLevel);
      const paid = (await this.paidOn(tx, userId, shift.bkkDay)) < JOB_DAILY_PAID_SHIFTS;
      const wageGold = paid ? pay.total : 0;
      const produce = paid ? shiftProduce(job, shift.seed, correct) : {};
      const workerXp = paid ? shiftWorkerXp(checks) : 0;
      const playerXp = paid ? correct : 0;
      const ref = { referenceType: 'job_shift', referenceId: id };

      if (wageGold > 0) {
        await this.wallet.applyChange(tx, { userId, currency: 'GOLD', amount: wageGold, type: 'JOB_WAGE', idempotencyKey: `job:${id}:GOLD`, ...ref });
      }
      for (const code of MATERIAL_CODES) {
        const n = produce[code] ?? 0;
        if (n > 0) await this.inventory.applyChange(tx, { userId, itemId: materialItemId(code), delta: n, type: 'JOB_WAGE', idempotencyKey: `job:${id}:${code}`, ...ref });
      }
      const worker = workerXp > 0 ? await this.skills.addXp(tx, userId, 'WORKER', workerXp) : null;
      const playerLevel = await this.skills.addPlayerXp(tx, userId, playerXp);
      if (paid) await this.quests.recordEvent(tx, userId, 'JOB_SHIFT_COMPLETE', 1);

      const result: StoredShiftResult = {
        checks,
        pay,
        wageGold,
        produce,
        workerXp,
        playerXp,
        paid,
        playerLevel,
        workerLevelAfter: worker?.level ?? (await this.skills.level(tx, userId, 'WORKER')),
        workerLeveledUp: worker?.leveledUp ?? false,
      };
      const done = await tx.jobShift.update({
        where: { id },
        data: { status: 'DONE', finishedAt: new Date(), paid, correct, wageGold, workerXp, result: result as object },
      });
      return { shift: done, replayed: false };
    });
    const [paidToday, skills, gold] = await Promise.all([
      this.paidOn(this.prisma, userId, bangkokDay(this.daily.nowMs())),
      this.skills.all(this.prisma, userId),
      this.prisma.playerWallet.findUnique({ where: { userId_currency: { userId, currency: 'GOLD' } } }),
    ]);
    return {
      ...this.present(out.shift),
      replayed: out.replayed,
      paidShiftsLeft: Math.max(0, JOB_DAILY_PAID_SHIFTS - paidToday),
      worker: skills.WORKER,
      gold: gold?.balance ?? 0,
    };
  }

  private expired(s: JobShift): boolean {
    return Date.now() - s.startedAt.getTime() > JOB_SHIFT_TTL_SECONDS * 1000;
  }

  private paidOn(db: Tx | PrismaService, userId: string, day: string): Promise<number> {
    return db.jobShift.count({ where: { userId, bkkDay: day, status: 'DONE', paid: true } });
  }

  private present(s: JobShift) {
    const job = s.job as JobCode;
    return {
      id: s.id,
      job,
      status: s.status,
      workerLevel: s.workerLevel,
      startedAt: s.startedAt.toISOString(),
      /** hand in no earlier than this... */
      minSeconds: jobMinSeconds(job),
      /** ...and before this, or the shift is void */
      expiresAt: new Date(s.startedAt.getTime() + JOB_SHIFT_TTL_SECONDS * 1000).toISOString(),
      tasks: jobTasks(job, s.seed),
      result: (s.result as StoredShiftResult | null) ?? null,
    };
  }
}
