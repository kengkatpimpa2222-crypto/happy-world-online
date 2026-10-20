import { INestApplication } from '@nestjs/common';
import { dailyMissionsFor } from '@hwo/shared-contracts';
import { PrismaService } from '../src/prisma/prisma.service';
import { DailyRewardsService } from '../src/quests/daily-rewards.service';
import { createTestApp, key, newGuest, setDailyClock, TestPlayer } from './helpers';

/** Bangkok noon of a date (UTC+7). 2026-10-12 is a Monday. */
const bkkNoon = (day: string) => `${day}T05:00:00Z`;

describe('Daily rewards & missions (Phase 11)', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];
  let daily: DailyRewardsService;

  beforeAll(async () => {
    ({ app, prisma, http } = await createTestApp());
    daily = app.get(DailyRewardsService);
  });
  afterAll(async () => app.close());
  beforeEach(() => setDailyClock(app, bkkNoon('2026-10-14')));

  const gold = async (p: TestPlayer) => (await http().get('/api/v1/player/wallet').set(p.auth)).body.balances.GOLD as number;
  const itemQty = async (p: TestPlayer, itemId: string) =>
    (await prisma.playerInventory.findUnique({ where: { userId_itemId: { userId: p.userId, itemId } } }))?.quantity ?? 0;
  const state = async (p: TestPlayer) => (await http().get('/api/v1/daily').set(p.auth).expect(200)).body;
  const checkin = (p: TestPlayer) => http().post('/api/v1/daily/checkin').set(p.auth);
  const claim = (p: TestPlayer, periodKey: string, missionId: string) => http().post('/api/v1/daily/missions/claim').set(p.auth).send({ periodKey, missionId });
  /** Counts progress the way a real action would (inside a transaction). */
  const record = (p: TestPlayer, objective: Parameters<DailyRewardsService['record']>[2], amount: number) =>
    prisma.$transaction((tx) => daily.record(tx, p.userId, objective, amount));
  /** First day from `from` on which this player's daily missions include `missionId`. */
  const dayWith = (p: TestPlayer, missionId: string, from = '2026-10-14') => {
    for (let i = 0; i < 120; i++) {
      const d = new Date(Date.parse(`${from}T00:00:00Z`) + i * 86_400_000).toISOString().slice(0, 10);
      if (dailyMissionsFor(p.userId, d).some((m) => m.id === missionId)) return d;
    }
    throw new Error('no such day');
  };

  it('a new player sees step 1 waiting, 3 daily missions and the weekly missions, from server time', async () => {
    const p = await newGuest(app);
    const s = await state(p);
    expect(s.now).toBe(new Date(s.now).toISOString());
    expect(s.checkin).toMatchObject({ today: '2026-10-14', claimedToday: false, todayStep: 1, totalDays: 0, nextResetAt: '2026-10-14T17:00:00.000Z' });
    expect(s.checkin.track.map((t: { state: string }) => t.state)).toEqual(['TODAY', 'UPCOMING', 'UPCOMING', 'UPCOMING', 'UPCOMING', 'UPCOMING', 'UPCOMING']);
    expect(s.daily.periodKey).toBe('D2026-10-14');
    expect(s.daily.missions).toHaveLength(3);
    expect(s.daily.missions.map((m: { id: string }) => m.id)).toEqual(dailyMissionsFor(p.userId, '2026-10-14').map((m) => m.id));
    expect(s.weekly).toMatchObject({ periodKey: 'W2026-10-12', endsAt: '2026-10-18T17:00:00.000Z' });
    expect(s.weekly.missions.map((m: { id: string }) => m.id)).toEqual(['w_visit_4', 'w_daily_10', 'w_sell_100', 'w_explore_5']);
    expect(s.daily.missions[0]).toMatchObject({ progress: 0, status: 'IN_PROGRESS', fromPreviousPeriod: false });
    expect(s.claimable).toBe(1);
  });

  it('check-in pays through the wallet ledger once per Bangkok day', async () => {
    const p = await newGuest(app);
    const res = await checkin(p).expect(200);
    expect(res.body).toMatchObject({ day: '2026-10-14', step: 1, totalDays: 1, rewards: { gold: 50, item: null }, wallet: { GOLD: 550 } });
    const again = await checkin(p).expect(409);
    expect(again.body.error.code).toBe('CHECKIN_ALREADY_CLAIMED');
    expect(await gold(p)).toBe(550);
    const ledger = await prisma.walletTransaction.findMany({ where: { userId: p.userId, type: 'DAILY_CHECKIN' } });
    expect(ledger).toHaveLength(1);
    expect(ledger[0]).toMatchObject({ amount: 50, balanceBefore: 500, balanceAfter: 550, referenceType: 'checkin', referenceId: '2026-10-14' });
    const s = await state(p);
    expect(s.checkin).toMatchObject({ claimedToday: true, todayStep: 1 });
    expect(s.checkin.track[0].state).toBe('CLAIMED');
    expect(s.claimable).toBe(0);
  });

  it('10 parallel check-ins pay exactly once', async () => {
    const p = await newGuest(app);
    const res = await Promise.all(Array.from({ length: 10 }, () => checkin(p)));
    expect(res.filter((r) => r.status === 200)).toHaveLength(1);
    expect(res.filter((r) => r.status === 409).every((r) => r.body.error.code === 'CHECKIN_ALREADY_CLAIMED')).toBe(true);
    expect(await gold(p)).toBe(550);
    expect(await prisma.walletTransaction.count({ where: { userId: p.userId, type: 'DAILY_CHECKIN' } })).toBe(1);
    expect(await prisma.dailyCheckin.count({ where: { userId: p.userId } })).toBe(1);
  });

  it('the day turns at Bangkok midnight: 23:59 and 00:01 are two check-ins', async () => {
    const p = await newGuest(app);
    setDailyClock(app, '2026-10-14T16:59:00Z'); // 23:59 Bangkok
    expect((await checkin(p).expect(200)).body.day).toBe('2026-10-14');
    setDailyClock(app, '2026-10-14T17:01:00Z'); // 00:01 Bangkok, next day
    expect((await checkin(p).expect(200)).body).toMatchObject({ day: '2026-10-15', step: 2 });
  });

  it('missed days never reset the track; items go through the item ledger; step 7 gives the Nang Kwak and wraps to 1', async () => {
    const p = await newGuest(app);
    // 7 check-ins spread over 3 weeks, with gaps
    const days = ['2026-10-01', '2026-10-02', '2026-10-05', '2026-10-09', '2026-10-10', '2026-10-15', '2026-10-21', '2026-10-22'];
    const steps: number[] = [];
    for (const d of days) {
      setDailyClock(app, bkkNoon(d));
      steps.push((await checkin(p).expect(200)).body.step);
    }
    expect(steps).toEqual([1, 2, 3, 4, 5, 6, 7, 1]);
    expect(await itemQty(p, 'mat_mango')).toBe(3);
    expect(await itemQty(p, 'mat_coconut')).toBe(3);
    expect(await itemQty(p, 'furn_nang_kwak')).toBe(1);
    const itemLedger = await prisma.inventoryTransaction.findMany({ where: { userId: p.userId, type: 'DAILY_CHECKIN' }, orderBy: { createdAt: 'asc' } });
    expect(itemLedger.map((t) => [t.itemId, t.delta])).toEqual([
      ['mat_mango', 3],
      ['furn_plant_pot', 1],
      ['mat_coconut', 3],
      ['furn_nang_kwak', 1],
    ]);
    // 50 + 80 + 120 + 200 + 50 = 500 Gold from check-ins
    expect(await gold(p)).toBe(1000);
    setDailyClock(app, bkkNoon('2026-10-22'));
    const s = await state(p);
    expect(s.checkin).toMatchObject({ claimedToday: true, todayStep: 1, totalDays: 8 });
  });

  it('weekly "check in on 4 days" counts days, not streaks, and pays gold + items once', async () => {
    const p = await newGuest(app);
    for (const d of ['2026-10-12', '2026-10-14', '2026-10-16', '2026-10-18']) {
      setDailyClock(app, bkkNoon(d));
      await checkin(p).expect(200);
    }
    const s = await state(p);
    expect(s.weekly.missions.find((m: { id: string }) => m.id === 'w_visit_4')).toMatchObject({ progress: 4, status: 'COMPLETED' });
    const before = await gold(p);
    const res = await claim(p, 'W2026-10-12', 'w_visit_4').expect(200);
    expect(res.body).toMatchObject({ rewards: { gold: 150, xp: 30, item: { itemId: 'mat_sticky_rice', quantity: 3 } }, wallet: { GOLD: before + 150 } });
    expect(await itemQty(p, 'mat_sticky_rice')).toBe(3);
    expect((await claim(p, 'W2026-10-12', 'w_visit_4').expect(409)).body.error.code).toBe('MISSION_ALREADY_CLAIMED');
    expect(await prisma.inventoryTransaction.count({ where: { userId: p.userId, type: 'MISSION_REWARD' } })).toBe(1);
    expect(await prisma.walletTransaction.count({ where: { userId: p.userId, type: 'MISSION_REWARD' } })).toBe(1);
    const profile = await http().get('/api/v1/player/profile').set(p.auth);
    expect(profile.body.xp).toBe(30);
  });

  it('a real game action (restocking the stall) completes a daily mission, and claiming it feeds the weekly one', async () => {
    const p = await newGuest(app);
    const day = dayWith(p, 'd_restock_10');
    setDailyClock(app, bkkNoon(day));
    const shop = (await http().get('/api/v1/businesses').set(p.auth).expect(200)).body[0];
    await http().post(`/api/v1/businesses/${shop.id}/restock`).set(p.auth).set('Idempotency-Key', key()).send({ productCode: 'LEMONADE', quantity: 6 }).expect(200);
    let m = (await state(p)).daily.missions.find((x: { id: string }) => x.id === 'd_restock_10');
    expect(m).toMatchObject({ progress: 6, status: 'IN_PROGRESS' });
    expect((await claim(p, `D${day}`, 'd_restock_10').expect(409)).body.error.code).toBe('MISSION_NOT_COMPLETED');
    await http().post(`/api/v1/businesses/${shop.id}/restock`).set(p.auth).set('Idempotency-Key', key()).send({ productCode: 'LEMONADE', quantity: 6 }).expect(200);
    const s = await state(p);
    m = s.daily.missions.find((x: { id: string }) => x.id === 'd_restock_10');
    expect(m).toMatchObject({ progress: 10, status: 'COMPLETED' }); // capped at the target
    expect(s.claimable).toBe(2); // the mission + today's check-in
    const before = await gold(p);
    await claim(p, `D${day}`, 'd_restock_10').expect(200);
    expect(await gold(p)).toBe(before + 30);
    const weekly = (await state(p)).weekly.missions.find((x: { id: string }) => x.id === 'w_daily_10');
    expect(weekly.progress).toBe(1);
  });

  it('a rejected action does not count (progress lives in the action transaction)', async () => {
    const p = await newGuest(app);
    const day = dayWith(p, 'd_restock_10');
    setDailyClock(app, bkkNoon(day));
    const shop = (await http().get('/api/v1/businesses').set(p.auth).expect(200)).body[0];
    // more than the stall can hold -> rejected, nothing counted
    await http().post(`/api/v1/businesses/${shop.id}/restock`).set(p.auth).set('Idempotency-Key', key()).send({ productCode: 'LEMONADE', quantity: 51 }).expect((r) => expect(r.status).toBeGreaterThanOrEqual(400));
    expect(await prisma.missionProgress.count({ where: { userId: p.userId } })).toBe(0);
  });

  it('10 parallel mission claims pay exactly once', async () => {
    const p = await newGuest(app);
    const m = dailyMissionsFor(p.userId, '2026-10-14')[0]!;
    await record(p, m.objective, m.target);
    const res = await Promise.all(Array.from({ length: 10 }, () => claim(p, 'D2026-10-14', m.id)));
    expect(res.filter((r) => r.status === 200)).toHaveLength(1);
    expect(res.filter((r) => r.status === 409)).toHaveLength(9);
    expect(await gold(p)).toBe(500 + m.rewardGold);
    expect(await prisma.walletTransaction.count({ where: { userId: p.userId, type: 'MISSION_REWARD' } })).toBe(1);
    const weekly = await prisma.missionProgress.findUnique({ where: { userId_periodKey_missionId: { userId: p.userId, periodKey: 'W2026-10-12', missionId: 'w_daily_10' } } });
    expect(weekly?.progress).toBe(1);
  });

  it("yesterday's finished mission can still be claimed today, but not the day after", async () => {
    const p = await newGuest(app);
    const m = dailyMissionsFor(p.userId, '2026-10-14')[1]!;
    await record(p, m.objective, m.target);
    setDailyClock(app, bkkNoon('2026-10-15'));
    const s = await state(p);
    const leftover = s.daily.missions.find((x: { periodKey: string }) => x.periodKey === 'D2026-10-14');
    expect(leftover).toMatchObject({ id: m.id, status: 'COMPLETED', fromPreviousPeriod: true });
    expect(s.daily.missions.filter((x: { fromPreviousPeriod: boolean }) => !x.fromPreviousPeriod)).toHaveLength(3);
    // a second finished-yesterday mission: let it pass two days
    const m2 = dailyMissionsFor(p.userId, '2026-10-14')[2]!;
    setDailyClock(app, bkkNoon('2026-10-14'));
    await record(p, m2.objective, m2.target);
    setDailyClock(app, bkkNoon('2026-10-15'));
    await claim(p, 'D2026-10-14', m.id).expect(200);
    setDailyClock(app, bkkNoon('2026-10-16'));
    expect((await claim(p, 'D2026-10-14', m2.id).expect(409)).body.error.code).toBe('MISSION_EXPIRED');
  });

  it('an unfinished mission from yesterday is expired, not "not completed"', async () => {
    const p = await newGuest(app);
    const m = dailyMissionsFor(p.userId, '2026-10-14')[0]!;
    setDailyClock(app, bkkNoon('2026-10-15'));
    expect((await claim(p, 'D2026-10-14', m.id).expect(409)).body.error.code).toBe('MISSION_EXPIRED');
  });

  it('progress after midnight counts for the new day, not the old one', async () => {
    const p = await newGuest(app);
    setDailyClock(app, '2026-10-14T17:00:30Z'); // 00:00:30 Bangkok on the 15th
    await checkin(p).expect(200);
    const rows = await prisma.missionProgress.findMany({ where: { userId: p.userId } });
    expect(rows.map((r) => r.periodKey)).toEqual(['W2026-10-12']);
    expect((await prisma.dailyCheckin.findFirst({ where: { userId: p.userId } }))?.bkkDay).toBe('2026-10-15');
  });

  it('rejects missions that are not mine, unknown, from the future or malformed', async () => {
    const p = await newGuest(app);
    const mine = new Set(dailyMissionsFor(p.userId, '2026-10-14').map((m) => m.id));
    const notMine = ['d_sell_10', 'd_serve_5', 'd_profit_50', 'd_restock_10', 'd_minigame_1', 'd_explore_1'].find((id) => !mine.has(id))!;
    expect((await claim(p, 'D2026-10-14', notMine).expect(404)).body.error.code).toBe('MISSION_NOT_FOUND');
    expect((await claim(p, 'D2026-10-14', 'd_nope').expect(404)).body.error.code).toBe('MISSION_NOT_FOUND');
    expect((await claim(p, 'W2026-10-12', 'd_sell_10').expect(404)).body.error.code).toBe('MISSION_NOT_FOUND');
    await claim(p, 'D2026-10-20', [...mine][0]!).expect(404);
    expect((await claim(p, 'D2026-10-01', [...mine][0]!).expect(409)).body.error.code).toBe('MISSION_EXPIRED');
    await claim(p, '2026-10-14', 'd_sell_10').expect(400);
    await http().post('/api/v1/daily/missions/claim').set(p.auth).send({ periodKey: 'D2026-10-14', missionId: 'd_sell_10', gold: 9999 }).expect(400);
  });

  it('needs a login', async () => {
    await http().get('/api/v1/daily').expect(401);
    await http().post('/api/v1/daily/checkin').expect(401);
  });
});
