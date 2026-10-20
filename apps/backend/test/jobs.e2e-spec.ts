import { INestApplication } from '@nestjs/common';
import { answerLength, checkTask, DAILY_MISSION_POOL, dailyMissionsFor, JOB_DAILY_PAID_SHIFTS, JobTask } from '@hwo/shared-contracts';
import { PrismaService } from '../src/prisma/prisma.service';
import { createTestApp, newGuest, setDailyClock, TestPlayer } from './helpers';

describe('Part-time jobs (Phase 12)', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];

  beforeAll(async () => ({ app, prisma, http } = await createTestApp()));
  afterAll(async () => app.close());

  const start = (p: TestPlayer, job: string) => http().post('/api/v1/jobs/start').set(p.auth).send({ job });
  const finish = (p: TestPlayer, id: string, answers: number[][]) => http().post(`/api/v1/jobs/shifts/${id}/finish`).set(p.auth).send({ answers });
  /** The shift "took" this long: move its start back instead of waiting. */
  const timeTravel = (id: string, seconds: number) =>
    prisma.$executeRaw`UPDATE "job_shifts" SET "started_at" = "started_at" - make_interval(secs => ${seconds}) WHERE "id" = ${id}::uuid`;
  const best = (tasks: JobTask[]) => tasks.map((t) => checkTask(t, []).expected);
  const wrongAll = (tasks: JobTask[]) => tasks.map((t) => Array.from({ length: answerLength(t) }, () => 0));
  const gold = async (p: TestPlayer) => (await http().get('/api/v1/player/wallet').set(p.auth).expect(200)).body.balances.GOLD as number;
  /** A whole shift done perfectly, as fast as allowed. */
  const work = async (p: TestPlayer, job: string, answer = best) => {
    const s = await start(p, job).expect(200);
    await timeTravel(s.body.id, 30);
    return finish(p, s.body.id, answer(s.body.tasks)).expect(200);
  };

  it('lists the three workplaces, the daily limit and the Worker skill', async () => {
    const p = await newGuest(app);
    const res = await http().get('/api/v1/jobs').set(p.auth).expect(200);
    expect(res.body.jobs.map((j: { job: string; zone: string; unlocked: boolean }) => [j.job, j.zone, j.unlocked])).toEqual([
      ['CASHIER', 'PLAZA', true],
      ['FARMHAND', 'COUNTRYSIDE', true],
      ['PACKER', 'INDUSTRIAL', false],
    ]);
    expect(res.body).toMatchObject({ paidShiftsPerDay: JOB_DAILY_PAID_SHIFTS, paidShiftsLeft: JOB_DAILY_PAID_SHIFTS, active: null, recent: [], totals: { paidShifts: 0, goldEarned: 0 } });
    expect(res.body.worker).toMatchObject({ skill: 'WORKER', level: 1, xp: 0 });
  });

  it('the city has the Countryside and Industrial zones', async () => {
    const p = await newGuest(app);
    const res = await http().get('/api/v1/city').set(p.auth).expect(200);
    const ids = res.body.zones.map((z: { id: string }) => z.id);
    expect(ids).toEqual(expect.arrayContaining(['COUNTRYSIDE', 'INDUSTRIAL']));
  });

  it('a perfect cashier shift pays wage + tips + full-shift bonus through the ledger, with a lesson per task', async () => {
    const p = await newGuest(app);
    const s = await start(p, 'CASHIER').expect(200);
    expect(s.body).toMatchObject({ job: 'CASHIER', status: 'ACTIVE', minSeconds: 10, resumed: false, result: null });
    expect(s.body.tasks).toHaveLength(5);
    expect(s.body.tasks[0]).toMatchObject({ kind: 'CASHIER', denominations: [100, 50, 20, 10, 5, 2, 1] });
    expect(s.body).not.toHaveProperty('seed'); // the seed stays on the server

    await timeTravel(s.body.id, 30);
    const r = await finish(p, s.body.id, best(s.body.tasks)).expect(200);
    expect(r.body).toMatchObject({ status: 'DONE', replayed: false, paidShiftsLeft: JOB_DAILY_PAID_SHIFTS - 1, gold: 560 });
    expect(r.body.result).toMatchObject({ paid: true, wageGold: 60, pay: { base: 40, perfectBonus: 10, shiftBonus: 10, skillBonus: 0, total: 60 }, workerXp: 13, playerXp: 5 });
    expect(r.body.result.checks.every((c: { correct: boolean; perfect: boolean }) => c.correct && c.perfect)).toBe(true);
    expect(r.body.worker).toMatchObject({ skill: 'WORKER', xp: 13 });
    const ledger = await prisma.walletTransaction.findMany({ where: { userId: p.userId, type: 'JOB_WAGE' } });
    expect(ledger).toHaveLength(1);
    expect(ledger[0]).toMatchObject({ amount: 60, idempotencyKey: `job:${s.body.id}:GOLD`, referenceType: 'job_shift', referenceId: s.body.id });
  });

  it('wrong answers cost nothing, are explained, and right-but-not-best answers earn the wage without the tip', async () => {
    const p = await newGuest(app);
    const s = await start(p, 'CASHIER').expect(200);
    await timeTravel(s.body.id, 30);
    const tasks = s.body.tasks as JobTask[];
    // task 1: change in 1-baht coins only (right, not the best way); the rest: nothing given back
    const answers = wrongAll(tasks);
    const t0 = tasks[0] as Extract<JobTask, { kind: 'CASHIER' }>;
    answers[0] = [0, 0, 0, 0, 0, 0, t0.paid - t0.total];
    const r = await finish(p, s.body.id, answers).expect(200);
    const checks = r.body.result.checks;
    expect(checks[0]).toMatchObject({ correct: true, perfect: t0.paid - t0.total === 1, expected: checkTask(t0, []).expected });
    expect(checks.slice(1).every((c: { correct: boolean }) => !c.correct)).toBe(true);
    expect(r.body.result.wageGold).toBe(8 + (t0.paid - t0.total === 1 ? 2 : 0));
    expect(await gold(p)).toBe(500 + r.body.result.wageGold);
  });

  it('handing in twice (even at once) pays once and returns the same payslip', async () => {
    const p = await newGuest(app);
    const s = await start(p, 'CASHIER').expect(200);
    await timeTravel(s.body.id, 30);
    const res = await Promise.all(Array.from({ length: 8 }, () => finish(p, s.body.id, best(s.body.tasks))));
    expect(res.every((r) => r.status === 200)).toBe(true);
    expect(res.filter((r) => !r.body.replayed)).toHaveLength(1);
    // a later different answer changes nothing
    const again = await finish(p, s.body.id, wrongAll(s.body.tasks)).expect(200);
    expect(again.body).toMatchObject({ replayed: true, result: { wageGold: 60 } });
    expect(await gold(p)).toBe(560);
    expect(await prisma.walletTransaction.count({ where: { userId: p.userId, type: 'JOB_WAGE' } })).toBe(1);
  });

  it('refuses a shift handed in too fast, answers of the wrong shape, and a shift left open too long', async () => {
    const p = await newGuest(app);
    const s = await start(p, 'FARMHAND').expect(200);
    const early = await finish(p, s.body.id, best(s.body.tasks)).expect(409);
    expect(early.body.error.code).toBe('JOB_SHIFT_TOO_EARLY');
    await timeTravel(s.body.id, 30);
    const shape = await finish(p, s.body.id, [[1], [2]]).expect(400);
    expect(shape.body.error.code).toBe('JOB_ANSWER_INVALID');
    const shape2 = await finish(p, s.body.id, s.body.tasks.map(() => [1, 2])).expect(400);
    expect(shape2.body.error.code).toBe('JOB_ANSWER_INVALID');
    const junk = await finish(p, s.body.id, s.body.tasks.map(() => [-1])).expect(400);
    expect(junk.body.error.code).toBe('VALIDATION_FAILED');

    await timeTravel(s.body.id, 900);
    const late = await finish(p, s.body.id, best(s.body.tasks)).expect(409);
    expect(late.body.error.code).toBe('JOB_SHIFT_EXPIRED');
    // a void shift never blocks the next one
    const next = await start(p, 'CASHIER').expect(200);
    expect(next.body.id).not.toBe(s.body.id);
    expect((await prisma.jobShift.findUniqueOrThrow({ where: { id: s.body.id } })).status).toBe('EXPIRED');
    expect(await gold(p)).toBe(500);
  });

  it('one open shift at a time: same job resumes it, another job is refused', async () => {
    const p = await newGuest(app);
    const a = await start(p, 'CASHIER').expect(200);
    const again = await start(p, 'CASHIER').expect(200);
    expect(again.body).toMatchObject({ id: a.body.id, resumed: true, tasks: a.body.tasks });
    const other = await start(p, 'FARMHAND').expect(409);
    expect(other.body.error).toMatchObject({ code: 'JOB_SHIFT_IN_PROGRESS', details: { shiftId: a.body.id, job: 'CASHIER' } });
    const list = await http().get('/api/v1/jobs').set(p.auth).expect(200);
    expect(list.body.active).toMatchObject({ id: a.body.id, tasks: a.body.tasks });
    // parallel starts make one shift
    const q = await newGuest(app);
    const many = await Promise.all(Array.from({ length: 6 }, () => start(q, 'FARMHAND')));
    expect(new Set(many.map((r) => r.body.id)).size).toBe(1);
    expect(await prisma.jobShift.count({ where: { userId: q.userId } })).toBe(1);
  });

  it('the packing plant opens at Worker level 2, which a few good shifts reach', async () => {
    const p = await newGuest(app);
    const locked = await start(p, 'PACKER').expect(409);
    expect(locked.body.error).toMatchObject({ code: 'JOB_LOCKED', details: { workerLevel: 2 } });
    await work(p, 'CASHIER');
    await work(p, 'CASHIER');
    const third = await work(p, 'FARMHAND');
    expect(third.body.worker).toMatchObject({ level: 1, xp: 39 });
    const fourth = await work(p, 'FARMHAND');
    expect(fourth.body.result).toMatchObject({ workerLeveledUp: true, workerLevelAfter: 2 });
    const pack = await work(p, 'PACKER');
    // level 2 adds 5%: (5*10 + 5*4 + 12) * 0.05 = 4.1 -> 4
    expect(pack.body.result.pay).toEqual({ base: 50, perfectBonus: 20, shiftBonus: 12, skillBonus: 4, total: 86 });
  });

  it('farm shifts also pay in produce through the item ledger', async () => {
    const p = await newGuest(app);
    const r = await work(p, 'FARMHAND');
    expect(r.body.result.pay.total).toBe(45);
    const [[crop, n]] = Object.entries(r.body.result.produce) as [string, number][];
    expect(n).toBe(2);
    const item = `mat_${crop!.toLowerCase()}`;
    const row = await prisma.inventoryTransaction.findFirstOrThrow({ where: { userId: p.userId, type: 'JOB_WAGE' } });
    expect(row).toMatchObject({ itemId: item, delta: 2, idempotencyKey: `job:${r.body.id}:${crop}` });
    // a farm shift with too few right answers pays the wage for what was right, no produce
    const s = await start(p, 'FARMHAND').expect(200);
    await timeTravel(s.body.id, 30);
    const answers = wrongAll(s.body.tasks);
    answers[0] = best(s.body.tasks)[0]!;
    const poor = await finish(p, s.body.id, answers).expect(200);
    expect(poor.body.result).toMatchObject({ wageGold: 7, produce: {} });
  });

  it(`after ${JOB_DAILY_PAID_SHIFTS} paid shifts a day, more shifts are free practice; the next Bangkok day pays again`, async () => {
    setDailyClock(app, '2026-10-14T10:00:00+07:00');
    try {
      const p = await newGuest(app);
      let earned = 0;
      for (let i = 0; i < JOB_DAILY_PAID_SHIFTS; i++) earned += (await work(p, 'CASHIER')).body.result.wageGold;
      // 60 a shift, a little more once the Worker skill levels up along the way
      expect(earned).toBeGreaterThan(60 * JOB_DAILY_PAID_SHIFTS);
      expect(await gold(p)).toBe(500 + earned);
      const practice = await work(p, 'CASHIER');
      expect(practice.body).toMatchObject({ paidShiftsLeft: 0, result: { paid: false, wageGold: 0, workerXp: 0, playerXp: 0, produce: {} } });
      expect(practice.body.result.pay.total).toBeGreaterThan(0); // the payslip it would have been
      expect(practice.body.result.checks).toHaveLength(5); // still checked and explained
      expect(await gold(p)).toBe(500 + earned);

      setDailyClock(app, '2026-10-15T00:01:00+07:00');
      const next = await work(p, 'CASHIER');
      expect(next.body).toMatchObject({ paidShiftsLeft: JOB_DAILY_PAID_SHIFTS - 1, result: { paid: true } });
      earned += next.body.result.wageGold;
      const list = await http().get('/api/v1/jobs').set(p.auth).expect(200);
      expect(list.body.totals).toEqual({ paidShifts: JOB_DAILY_PAID_SHIFTS + 1, goldEarned: earned });
      expect(list.body.recent).toHaveLength(5);
    } finally {
      setDailyClock(app, new Date().toISOString());
    }
  });

  it('a paid shift counts for the daily mission "work one shift" (when the player has it)', async () => {
    setDailyClock(app, '2026-10-14T10:00:00+07:00');
    try {
      // find a player whose missions today include the job mission
      let p: TestPlayer | null = null;
      for (let i = 0; i < 40 && !p; i++) {
        const g = await newGuest(app);
        if (dailyMissionsFor(g.userId, '2026-10-14').some((m) => m.id === 'd_job_1')) p = g;
      }
      expect(p).not.toBeNull();
      await http().get('/api/v1/daily').set(p!.auth).expect(200);
      await work(p!, 'CASHIER');
      const daily = await http().get('/api/v1/daily').set(p!.auth).expect(200);
      const m = daily.body.daily.missions.find((x: { id: string }) => x.id === 'd_job_1');
      expect(m).toMatchObject({ status: 'COMPLETED', progress: 1 });
      expect(DAILY_MISSION_POOL.find((x) => x.id === 'd_job_1')?.objective).toBe('JOB_SHIFT_COMPLETE');
    } finally {
      setDailyClock(app, new Date().toISOString());
    }
  });

  it("other players can't hand in my shift", async () => {
    const owner = await newGuest(app);
    const other = await newGuest(app);
    const s = await start(owner, 'CASHIER').expect(200);
    await timeTravel(s.body.id, 30);
    await finish(other, s.body.id, best(s.body.tasks)).expect(404);
    await finish(owner, s.body.id, best(s.body.tasks)).expect(200);
    expect(await gold(other)).toBe(500);
  });
});
