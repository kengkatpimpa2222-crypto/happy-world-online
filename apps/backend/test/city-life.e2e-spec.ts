import { INestApplication } from '@nestjs/common';
import { BUSINESS_PRODUCTS, CITY_PROJECTS, cityProjectThresholds, SHOP_TICK_SECONDS, weatherAt } from '@hwo/shared-contracts';
import type { Socket } from 'socket.io-client';
import { simulateCustomers, StallLine } from '../src/business/living-shop-sim';
import { CityLifeService } from '../src/city-life/city-life.service';
import { PrismaService } from '../src/prisma/prisma.service';
import { connectWs, createTestApp, key, NEUTRAL_WORLD, newGuest, receivedWithin, setCityWorld, setFestivalClock, waitFor } from './helpers';

type AgeBand = 'UNDER_13' | 'TEEN_13_17' | 'ADULT_18_PLUS';
type Ack = { ok: boolean };

describe('Living city: time, weather and city growth (Phase 7)', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];
  let city: CityLifeService;
  const sockets: Socket[] = [];

  beforeAll(async () => {
    ({ app, prisma, http } = await createTestApp());
  });
  beforeEach(() => {
    setFestivalClock(app, 'weekday');
    city = setCityWorld(app, NEUTRAL_WORLD);
  });
  afterEach(() => {
    sockets.splice(0).forEach((s) => s.disconnect());
  });
  afterAll(async () => {
    await new Promise((r) => setTimeout(r, 200));
    await app.close();
  });

  const api = '/api/v1/businesses';
  const timeTravel = (id: string, minutes: number) =>
    prisma.$executeRaw`UPDATE "businesses" SET "settled_tick" = "settled_tick" - ${minutes} WHERE "id" = ${id}::uuid`;
  const setGrowth = async (ageBand: AgeBand, points: number) => {
    await prisma.cityProjectCompletion.deleteMany({ where: { ageBand } });
    await prisma.cityGrowth.upsert({ where: { ageBand }, create: { ageBand, points }, update: { points } });
  };

  async function openStall(ageBand: AgeBand = 'TEEN_13_17', quantity = 20) {
    const p = await newGuest(app, ageBand);
    const shop = (await http().get(api).set(p.auth).expect(200)).body[0];
    await http().post(`${api}/${shop.id}/restock`).set(p.auth).set('Idempotency-Key', key()).send({ productCode: 'BREAD', quantity }).expect(200);
    await http().patch(`${api}/${shop.id}/pricing`).set(p.auth).send({ productCode: 'BREAD', price: BUSINESS_PRODUCTS.BREAD.minPrice }).expect(200);
    await http().put(`${api}/${shop.id}/shelf`).set(p.auth).send({ productCode: 'BREAD', quantity }).expect(200);
    await http().post(`${api}/${shop.id}/status`).set(p.auth).send({ status: 'OPEN' }).expect(200);
    return { p, id: shop.id as string };
  }

  it('GET /city/now tells the Bangkok time, season and weather; the old GET /city still works', async () => {
    const p = await newGuest(app);
    await http().get('/api/v1/city/now').expect(401);

    const n = (await http().get('/api/v1/city/now').set(p.auth).expect(200)).body;
    expect(n).toMatchObject({ hour: 10, clock: '10:00', phase: 'MORNING', weather: 'CLOUDY', weatherTh: 'ครึ้มฟ้า', crowd: 1, demand: [], busyTypes: [] });
    expect(['HOT', 'RAINY', 'COOL']).toContain(n.season);
    expect(n.forecast).toHaveLength(4);

    setCityWorld(app, { hour: null, weather: null });
    const real = (await http().get('/api/v1/city/now').set(p.auth).expect(200)).body;
    expect(real.weather).toBe(weatherAt(new Date(real.now).getTime()));
    expect(new Date(real.nextChangeAt).getTime()).toBeGreaterThan(new Date(real.now).getTime());

    setCityWorld(app, { hour: 19, weather: 'RAIN' });
    const rain = (await http().get('/api/v1/city/now').set(p.auth).expect(200)).body;
    expect(rain).toMatchObject({ phase: 'EVENING', weather: 'RAIN', tipTh: expect.stringContaining('ฝนตก') });
    expect(rain.demand[0]).toEqual({ productCode: 'MEATBALL', demand: 1.5 });
    expect(rain.crowd).toBeCloseTo(1.6 * 0.7, 5);

    const layout = (await http().get('/api/v1/city').set(p.auth).expect(200)).body;
    expect(layout.zones.length).toBeGreaterThan(0);
    await http().get('/api/v1/city/players?zone=PLAZA').set(p.auth).expect(200);
  });

  it('the market sells by the city clock and weather (same customers the simulation predicts)', async () => {
    city.hourOverride = 3;
    city.weatherOverride = 'RAIN';
    const s = await openStall('TEEN_13_17', 20);
    const stall: StallLine[] = [{ productCode: 'BREAD', shelfQuantity: 20, avgUnitCost: BUSINESS_PRODUCTS.BREAD.unitCost, sellPrice: BUSINESS_PRODUCTS.BREAD.minPrice }];
    const before = await prisma.business.findUniqueOrThrow({ where: { id: s.id } });
    const to = Math.floor(Date.now() / (SHOP_TICK_SECONDS * 1000));
    await prisma.$executeRaw`UPDATE "businesses" SET "settled_tick" = ${to - 240} WHERE "id" = ${s.id}::uuid`;
    await http().get(api).set(s.p.auth).expect(200);
    const after = await prisma.business.findUniqueOrThrow({ where: { id: s.id } });
    const settled = await prisma.shopSettlement.findMany({ where: { businessId: s.id }, orderBy: { createdAt: 'asc' } });
    expect(settled).toHaveLength(1);
    const { fromTick, toTick } = settled[0]!;
    const world = city.worldFor(await city.builtFor(prisma, s.p.userId));
    const night = simulateCustomers(s.id, before.level, fromTick, toTick, stall, world);
    expect(after.totalCustomers - before.totalCustomers).toBe(night.totals.customers);
    const noon = simulateCustomers(s.id, before.level, fromTick, toTick, stall, () => ({ hour: 12, weather: 'CLOUDY' }));
    expect(night.totals.customers).toBeLessThan(noon.totals.customers);
  });

  it('every unit sold builds the city: a project is finished for good and the whole city (same age band) hears it', async () => {
    const ends = cityProjectThresholds();
    const seller = await openStall('UNDER_13');
    const kid = await newGuest(app, 'UNDER_13');
    const teen = await newGuest(app, 'TEEN_13_17');
    const sk = await connectWs(app, kid.accessToken);
    const st = await connectWs(app, teen.accessToken);
    sockets.push(sk, st);
    expect(((await sk.timeout(3000).emitWithAck('zone:join', { zone: 'PLAZA' })) as Ack).ok).toBe(true);
    expect(((await st.timeout(3000).emitWithAck('zone:join', { zone: 'PLAZA' })) as Ack).ok).toBe(true);

    await setGrowth('UNDER_13', ends[0]! - 3);
    const teenHeard = receivedWithin(st, 'city:growth', () => true, 4000);
    const heard = waitFor<{ projectCode: string; index: number; points: number; next: string | null }>(sk, 'city:growth', () => true, 10000);
    await timeTravel(seller.id, 120);
    await http().get(api).set(seller.p.auth).expect(200);
    const ev = await heard;
    expect(ev).toMatchObject({ projectCode: 'PARK', index: 0, next: 'BUS_STOP' });
    expect(await teenHeard).toBe(false);

    const sold = (await prisma.business.findUniqueOrThrow({ where: { id: seller.id } })).totalUnitsSold;
    expect(sold).toBeGreaterThanOrEqual(3);
    expect(ev.points).toBe(ends[0]! - 3 + sold);
    expect(await prisma.cityProjectCompletion.count({ where: { ageBand: 'UNDER_13', projectCode: 'PARK' } })).toBe(1);

    const g = (await http().get('/api/v1/city/growth').set(seller.p.auth).expect(200)).body;
    expect(g).toMatchObject({ points: ends[0]! - 3 + sold, builtCount: 1, current: 'BUS_STOP', me: { points: sold - 3, total: sold } });
    expect(g.projects.map((x: { status: string }) => x.status)).toEqual(['DONE', 'BUILDING', 'LOCKED', 'LOCKED', 'LOCKED']);
    expect(g.projects[0].completedAt).toEqual(expect.any(String));
    expect(g.projects[1].progress).toBe(sold - 3);
    if (sold > 3) expect(g.top[0]).toMatchObject({ isMe: true, displayName: expect.any(String), points: sold - 3 });

    // another kid sees the builder but not their name (child safety)
    const other = (await http().get('/api/v1/city/growth').set(kid.auth).expect(200)).body;
    if (sold > 3) expect(other.top[0]).toMatchObject({ isMe: false, displayName: null });
    // the teen city is a different city
    const teenCity = (await http().get('/api/v1/city/growth').set(teen.auth).expect(200)).body;
    expect(teenCity.points).not.toBe(g.points);
  });

  it('a finished project changes the market only from the moment it was finished', async () => {
    const ends = cityProjectThresholds();
    await setGrowth('ADULT_18_PLUS', ends[1]!);
    const at = new Date();
    await prisma.cityProjectCompletion.createMany({
      data: [
        { ageBand: 'ADULT_18_PLUS', projectCode: 'PARK', completedAt: at },
        { ageBand: 'ADULT_18_PLUS', projectCode: 'BUS_STOP', completedAt: at },
      ],
    });
    const p = await newGuest(app, 'ADULT_18_PLUS');
    const built = await city.builtFor(prisma, p.userId);
    expect(built.map((b) => b.def.code).sort()).toEqual(['BUS_STOP', 'PARK']);
    const w = city.worldFor(built);
    const tick = Math.floor(at.getTime() / 60_000);
    expect(w(tick + 1)!.built!.map((d) => d.code).sort()).toEqual(['BUS_STOP', 'PARK']);
    expect(w(tick - 1)!.built).toEqual([]);
    const kid = await newGuest(app, 'UNDER_13');
    expect((await city.builtFor(prisma, kid.userId)).some((b) => b.def.code === 'BUS_STOP')).toBe(false);
    const g = (await http().get('/api/v1/city/growth').set(p.auth).expect(200)).body;
    expect(g.current).toBe(CITY_PROJECTS[2]!.code);
  });

  it('owner actions that settle the stall now also publish the live update', async () => {
    const s = await openStall('TEEN_13_17', 20);
    const ws = await connectWs(app, s.p.accessToken);
    sockets.push(ws);
    await timeTravel(s.id, 120);
    const update = waitFor<{ businessId: string; cityPoints: number; unitsSold: number }>(ws, 'shop:updated', (u) => u.businessId === s.id, 5000);
    await http().patch(`${api}/${s.id}/pricing`).set(s.p.auth).send({ productCode: 'BREAD', price: BUSINESS_PRODUCTS.BREAD.minPrice }).expect(200);
    const u = await update;
    expect(u.cityPoints).toBe(u.unitsSold);
  });
});
