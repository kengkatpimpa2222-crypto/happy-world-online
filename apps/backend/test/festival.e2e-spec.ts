import { INestApplication } from '@nestjs/common';
import { BUSINESS_PRODUCT_CODES, BUSINESS_PRODUCTS, BusinessProductCode, FESTIVAL_MIN_CONTRIBUTION, FESTIVAL_TIERS, festivalWeek, festivalWeekIndex } from '@hwo/shared-contracts';
import type { Socket } from 'socket.io-client';
import { FestivalService } from '../src/festival/festival.service';
import { PrismaService } from '../src/prisma/prisma.service';
import { connectWs, createTestApp, key, newGuest, setFestivalClock, TestPlayer, waitFor } from './helpers';

type AgeBand = 'UNDER_13' | 'TEEN_13_17' | 'ADULT_18_PLUS';
type Ack = { ok: boolean };
interface Tier {
  tier: number;
  target: number;
  reached: boolean;
  claimed: boolean;
  claimable: boolean;
  rewardItemId: string | null;
}

describe('City festival (Phase 6)', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];
  let festival: FestivalService;
  const sockets: Socket[] = [];

  beforeAll(async () => {
    ({ app, prisma, http } = await createTestApp());
  });
  beforeEach(() => {
    festival = setFestivalClock(app, 'festival');
  });
  afterEach(() => {
    sockets.splice(0).forEach((s) => s.disconnect());
  });
  afterAll(async () => {
    await new Promise((r) => setTimeout(r, 200));
    await app.close();
  });

  const api = '/api/v1/businesses';
  const thisWeek = () => festivalWeek(festivalWeekIndex(festival.nowMs()));
  const state = async (p: TestPlayer) => (await http().get('/api/v1/festival').set(p.auth).expect(200)).body;
  const claim = (p: TestPlayer, festivalKey: string, tier: number) => http().post('/api/v1/festival/claim').set(p.auth).send({ festivalKey, tier });
  const gold = async (p: TestPlayer) => (await http().get('/api/v1/player/wallet').set(p.auth)).body.balances.GOLD as number;
  const timeTravel = (id: string, minutes: number) =>
    prisma.$executeRaw`UPDATE "businesses" SET "settled_tick" = "settled_tick" - ${minutes} WHERE "id" = ${id}::uuid`;
  const setCity = (festivalKey: string, ageBand: AgeBand, points: number) =>
    prisma.festivalCityProgress.upsert({
      where: { festivalKey_ageBand: { festivalKey, ageBand } },
      create: { festivalKey, ageBand, points, contributors: 1 },
      update: { points },
    });
  const setShare = (festivalKey: string, userId: string, ageBand: AgeBand, points: number) =>
    prisma.festivalContribution.upsert({
      where: { festivalKey_userId: { festivalKey, userId } },
      create: { festivalKey, userId, ageBand, points },
      update: { points },
    });

  /** A level-1 product this week's festival features, and one it does not. */
  const products = () => {
    const f = thisWeek().festival;
    const featured = BUSINESS_PRODUCTS[f.mainProduct].unlockLevel === 1 ? f.mainProduct : f.sideProduct;
    const plain = BUSINESS_PRODUCT_CODES.find((c) => BUSINESS_PRODUCTS[c].unlockLevel === 1 && c !== f.mainProduct && c !== f.sideProduct)!;
    return { featured, plain };
  };

  /** A player with an open stall: 20 of `product` on the stall, cheap enough that most customers buy. */
  async function openStall(product: BusinessProductCode, ageBand: AgeBand = 'TEEN_13_17') {
    const p = await newGuest(app, ageBand);
    const shop = (await http().get(api).set(p.auth).expect(200)).body[0];
    await http().post(`${api}/${shop.id}/restock`).set(p.auth).set('Idempotency-Key', key()).send({ productCode: product, quantity: 20 }).expect(200);
    await http().patch(`${api}/${shop.id}/pricing`).set(p.auth).send({ productCode: product, price: BUSINESS_PRODUCTS[product].minPrice }).expect(200);
    await http().put(`${api}/${shop.id}/shelf`).set(p.auth).send({ productCode: product, quantity: 20 }).expect(200);
    await http().post(`${api}/${shop.id}/status`).set(p.auth).send({ status: 'OPEN' }).expect(200);
    return { p, id: shop.id as string };
  }

  /** Units of `product` the shop's settlements sold (the shop's own books). */
  async function unitsSold(businessId: string, product: string): Promise<number> {
    const rows = await prisma.shopSettlement.findMany({ where: { businessId } });
    return rows.reduce((a, r) => a + ((r.lines as Record<string, { unitsSold?: number }>)[product]?.unitsSold ?? 0), 0);
  }

  it("shows this week's festival as upcoming on a weekday and running at the weekend", async () => {
    const p = await newGuest(app);
    setFestivalClock(app, 'weekday');
    const before = await state(p);
    expect(before.current.status).toBe('UPCOMING');
    expect(before.previous).toBeNull();

    setFestivalClock(app, 'festival');
    const s = await state(p);
    const w = thisWeek();
    expect(s.current).toMatchObject({
      key: w.key,
      code: w.festival.code,
      nameKey: w.festival.nameKey,
      status: 'ACTIVE',
      featured: [
        { productCode: w.festival.mainProduct, demand: 3 },
        { productCode: w.festival.sideProduct, demand: 2 },
      ],
      me: { points: 0, minToClaim: FESTIVAL_MIN_CONTRIBUTION },
    });
    expect(s.current.key).toBe(before.current.key);
    expect(s.current.tiers.map((t: Tier) => t.target)).toEqual(FESTIVAL_TIERS.map((t) => t.target));
    expect(s.current.tiers[2].rewardItemId).toBe(w.festival.rewardItemId);
  });

  it("featured goods sold during the festival count for the player and the city, and advance the festival quest", async () => {
    const { featured } = products();
    const p = await newGuest(app, 'TEEN_13_17');
    await http().post('/api/v1/quests/shop_open_stall/start').set(p.auth).expect(201);
    const shop = (await http().get(api).set(p.auth).expect(200)).body[0];
    await http().post(`${api}/${shop.id}/restock`).set(p.auth).set('Idempotency-Key', key()).send({ productCode: featured, quantity: 20 }).expect(200);
    await http().patch(`${api}/${shop.id}/pricing`).set(p.auth).send({ productCode: featured, price: BUSINESS_PRODUCTS[featured].minPrice }).expect(200);
    await http().put(`${api}/${shop.id}/shelf`).set(p.auth).send({ productCode: featured, quantity: 20 }).expect(200);
    await http().post(`${api}/${shop.id}/status`).set(p.auth).send({ status: 'OPEN' }).expect(200);
    await http().post('/api/v1/quests/shop_open_stall/claim').set(p.auth).expect(200);
    await http().post('/api/v1/quests/festival_first_help/start').set(p.auth).expect(201);

    const cityBefore = (await state(p)).current.city;
    await timeTravel(shop.id, 120);
    await http().get(api).set(p.auth).expect(200); // settles

    const sold = await unitsSold(shop.id, featured);
    expect(sold).toBeGreaterThanOrEqual(FESTIVAL_MIN_CONTRIBUTION);
    const s = await state(p);
    expect(s.current.me.points).toBe(sold);
    expect(s.current.city.points - cityBefore.points).toBe(sold);
    expect(s.current.city.contributors - cityBefore.contributors).toBe(1);
    expect(s.current.top.find((t: { isMe: boolean }) => t.isMe)).toMatchObject({ points: sold, displayName: expect.any(String) });
    const quest = (await http().get('/api/v1/quests').set(p.auth).expect(200)).body.find((q: { id: string }) => q.id === 'festival_first_help');
    expect(quest.progress.status).toBe('COMPLETED');
  });

  it('ordinary goods and ordinary days do not count', async () => {
    const { plain, featured } = products();
    const a = await openStall(plain);
    await timeTravel(a.id, 120);
    await http().get(api).set(a.p.auth).expect(200);
    expect(await unitsSold(a.id, plain)).toBeGreaterThan(0);
    expect((await state(a.p)).current.me.points).toBe(0);

    setFestivalClock(app, 'weekday');
    const b = await openStall(featured);
    await timeTravel(b.id, 120);
    await http().get(api).set(b.p.auth).expect(200);
    expect(await unitsSold(b.id, featured)).toBeGreaterThan(0);
    expect(await prisma.festivalContribution.count({ where: { userId: b.p.userId } })).toBe(0);
  });

  it('pays each tier once, through the ledger, only to players who helped and only after the city reached it', async () => {
    const w = thisWeek();
    const p = await newGuest(app, 'ADULT_18_PLUS');
    const lazy = await newGuest(app, 'ADULT_18_PLUS');
    await setCity(w.key, 'ADULT_18_PLUS', FESTIVAL_TIERS[0]!.target - 1);
    await setShare(w.key, p.userId, 'ADULT_18_PLUS', FESTIVAL_MIN_CONTRIBUTION);
    await setShare(w.key, lazy.userId, 'ADULT_18_PLUS', FESTIVAL_MIN_CONTRIBUTION - 1);

    expect((await claim(p, w.key, 1).expect(409)).body.error.code).toBe('FESTIVAL_TIER_NOT_REACHED');
    await setCity(w.key, 'ADULT_18_PLUS', FESTIVAL_TIERS[2]!.target);
    expect((await claim(lazy, w.key, 1).expect(409)).body.error.code).toBe('FESTIVAL_NOT_ENOUGH_CONTRIBUTION');
    expect((await state(lazy)).current.tiers.every((t: Tier) => t.reached && !t.claimable)).toBe(true);
    expect((await state(p)).current.tiers.every((t: Tier) => t.claimable)).toBe(true);

    const g0 = await gold(p);
    const r1 = await claim(p, w.key, 1).expect(200);
    expect(r1.body).toMatchObject({ festivalKey: w.key, tier: 1, rewards: { gold: FESTIVAL_TIERS[0]!.rewardGold, xp: FESTIVAL_TIERS[0]!.rewardXp, itemId: null } });
    expect(await gold(p)).toBe(g0 + FESTIVAL_TIERS[0]!.rewardGold);
    expect((await claim(p, w.key, 1).expect(409)).body.error.code).toBe('FESTIVAL_ALREADY_CLAIMED');

    const r3 = await claim(p, w.key, 3).expect(200);
    expect(r3.body.rewards.itemId).toBe(w.festival.rewardItemId);
    const inv = await prisma.playerInventory.findUnique({ where: { userId_itemId: { userId: p.userId, itemId: w.festival.rewardItemId } } });
    expect(inv?.quantity).toBe(1);

    const ledger = await prisma.walletTransaction.findMany({ where: { userId: p.userId, type: 'FESTIVAL_REWARD' }, orderBy: { createdAt: 'asc' } });
    expect(ledger.map((l) => [l.amount, l.referenceId])).toEqual([
      [FESTIVAL_TIERS[0]!.rewardGold, `${w.key}:1`],
      [FESTIVAL_TIERS[2]!.rewardGold, `${w.key}:3`],
    ]);
    const tiers = (await state(p)).current.tiers as Tier[];
    expect(tiers.map((t) => [t.claimed, t.claimable])).toEqual([
      [true, false],
      [false, true],
      [true, false],
    ]);
  });

  it('two claims at once pay once', async () => {
    const w = thisWeek();
    const p = await newGuest(app, 'ADULT_18_PLUS');
    await setCity(w.key, 'ADULT_18_PLUS', FESTIVAL_TIERS[2]!.target);
    await setShare(w.key, p.userId, 'ADULT_18_PLUS', 50);
    const g0 = await gold(p);
    const res = await Promise.all([claim(p, w.key, 2), claim(p, w.key, 2), claim(p, w.key, 2)]);
    expect(res.map((r) => r.status).sort()).toEqual([200, 409, 409]);
    expect(await gold(p)).toBe(g0 + FESTIVAL_TIERS[1]!.rewardGold);
  });

  it('last week rewards stay claimable for one more week, then expire', async () => {
    const w = thisWeek();
    const p = await newGuest(app, 'ADULT_18_PLUS');
    await setCity(w.key, 'ADULT_18_PLUS', FESTIVAL_TIERS[2]!.target);
    await setShare(w.key, p.userId, 'ADULT_18_PLUS', 30);

    setFestivalClock(app, 'next-weekday');
    const s = await state(p);
    expect(s.current.key).not.toBe(w.key);
    expect(s.previous).toMatchObject({ key: w.key, status: 'ENDED' });
    await claim(p, w.key, 1).expect(200);

    const old = festivalWeek(w.weekIndex - 1);
    expect((await claim(p, old.key, 1).expect(409)).body.error.code).toBe('FESTIVAL_CLAIM_EXPIRED');
    const future = festivalWeek(w.weekIndex + 3);
    expect((await claim(p, future.key, 1).expect(404)).body.error.code).toBe('FESTIVAL_NOT_FOUND');
    const wrongCode = festivalWeek(w.weekIndex + 1).festival.code === w.festival.code ? 'NOPE' : festivalWeek(w.weekIndex + 1).festival.code;
    expect((await claim(p, `${wrongCode}-${w.weekIndex}`, 1).expect(404)).body.error.code).toBe('FESTIVAL_NOT_FOUND');
    expect((await claim(p, w.key, 4).expect(400)).body.error.code).toBe('VALIDATION_FAILED');
  });

  it('top helpers: own age band only, no blocked players, no names for under-13s', async () => {
    const w = thisWeek();
    const kids = await Promise.all([1, 2, 3].map(() => newGuest(app, 'UNDER_13')));
    const teen = await newGuest(app, 'TEEN_13_17');
    await setShare(w.key, kids[0]!.userId, 'UNDER_13', 900);
    await setShare(w.key, kids[1]!.userId, 'UNDER_13', 800);
    await setShare(w.key, kids[2]!.userId, 'UNDER_13', 700);
    await setShare(w.key, teen.userId, 'TEEN_13_17', 5000);
    await http().post(`/api/v1/players/${kids[0]!.userId}/block`).set(kids[2]!.auth).expect(200);

    const top = (await state(kids[2]!)).current.top as { displayName: string | null; points: number; isMe: boolean }[];
    expect(top.map((t) => t.points)).toEqual([800, 700]); // kid 0 is blocked, the teen is in another city
    expect(top[0]).toMatchObject({ displayName: null, isMe: false });
    expect(top[1]).toMatchObject({ isMe: true, displayName: expect.any(String) });

    const teenTop = (await state(teen)).current.top;
    expect(teenTop[0]).toMatchObject({ points: 5000, isMe: true });
    expect(teenTop.every((t: { points: number }) => t.points !== 900)).toBe(true);
  });

  it('when a sale pushes the city over a tier, the whole city of that age band hears about it', async () => {
    const { featured } = products();
    const w = thisWeek();
    const seller = await openStall(featured, 'UNDER_13');
    const kid = await newGuest(app, 'UNDER_13');
    const adult = await newGuest(app, 'ADULT_18_PLUS');
    const sk = await connectWs(app, kid.accessToken);
    const sa = await connectWs(app, adult.accessToken);
    sockets.push(sk, sa);
    expect(((await sk.timeout(3000).emitWithAck('zone:join', { zone: 'PLAZA' })) as Ack).ok).toBe(true);
    expect(((await sa.timeout(3000).emitWithAck('zone:join', { zone: 'PLAZA' })) as Ack).ok).toBe(true);
    let adultHeard = false;
    sa.on('festival:milestone', () => (adultHeard = true));

    const next = FESTIVAL_TIERS[0]!;
    await setCity(w.key, 'UNDER_13', next.target - 1);

    const heard = waitFor<{ festivalKey: string; tier: number; points: number }>(sk, 'festival:milestone', (m) => m.festivalKey === w.key, 10000);
    await timeTravel(seller.id, 120);
    await http().get(api).set(seller.p.auth).expect(200);
    const m = await heard;
    expect(m.tier).toBe(next.tier);
    expect(m.points).toBeGreaterThanOrEqual(next.target);
    await new Promise((r) => setTimeout(r, 300));
    expect(adultHeard).toBe(false);
  });
});
