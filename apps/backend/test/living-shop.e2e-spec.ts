import { INestApplication } from '@nestjs/common';
import { MARKET_STALL_SLOTS, SHOP_MAX_OFFLINE_TICKS } from '@hwo/shared-contracts';
import type { Socket } from 'socket.io-client';
import { LivingShopService } from '../src/business/living-shop.service';
import { ShopSettlerService } from '../src/business/shop-settler.service';
import { PrismaService } from '../src/prisma/prisma.service';
import { connectWs, createTestApp, grantGold, key, newGuest, TestPlayer, waitFor } from './helpers';

type Ack = { ok: boolean; error?: { code: string }; [k: string]: unknown };

describe('Living shop (Phase 5)', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];
  let shopSvc: LivingShopService;
  const sockets: Socket[] = [];

  beforeAll(async () => {
    ({ app, prisma, http } = await createTestApp());
    shopSvc = app.get(LivingShopService);
  });
  afterEach(() => {
    sockets.splice(0).forEach((s) => s.disconnect());
  });
  afterAll(async () => {
    await new Promise((r) => setTimeout(r, 200));
    await app.close();
  });

  const api = '/api/v1/businesses';
  const shopOf = async (p: TestPlayer) => (await http().get(api).set(p.auth).expect(200)).body[0];
  const gold = async (p: TestPlayer) => (await http().get('/api/v1/player/wallet').set(p.auth)).body.balances.GOLD as number;
  const restock = (p: TestPlayer, id: string, productCode: string, quantity: number) =>
    http().post(`${api}/${id}/restock`).set(p.auth).set('Idempotency-Key', key()).send({ productCode, quantity });
  const shelf = (p: TestPlayer, id: string, productCode: string, quantity: number) => http().put(`${api}/${id}/shelf`).set(p.auth).send({ productCode, quantity });
  const status = (p: TestPlayer, id: string, s: string) => http().post(`${api}/${id}/status`).set(p.auth).send({ status: s });
  /** Pretends `minutes` passed since the last settle (the simulation only looks at tick numbers). */
  const timeTravel = (id: string, minutes: number) =>
    prisma.$executeRaw`UPDATE "businesses" SET "settled_tick" = "settled_tick" - ${minutes} WHERE "id" = ${id}::uuid`;

  /** A player with an open lemonade stall: 20 on the stall at the market price, 10 more in storage. */
  async function openStall(ageBand = 'TEEN_13_17', price = 10) {
    const p = await newGuest(app, ageBand);
    const shop = await shopOf(p);
    await restock(p, shop.id, 'LEMONADE', 30).expect(200);
    await http().patch(`${api}/${shop.id}/pricing`).set(p.auth).send({ productCode: 'LEMONADE', price }).expect(200);
    await shelf(p, shop.id, 'LEMONADE', 20).expect(200);
    const opened = await status(p, shop.id, 'OPEN').expect(200);
    return { p, id: shop.id as string, opened: opened.body };
  }

  it('a new shop is closed, shows stall limits and which products unlock later', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    expect(shop.stall).toEqual({ status: 'CLOSED', slot: null, cashBox: 0, totalCustomers: 0, shelfSlots: 2, shelfCapacity: 20, customersPerMinute: 1, slotsUsed: 0 });
    expect(shop.catalog.map((c: { productCode: string; unlocked: boolean }) => [c.productCode, c.unlocked])).toEqual([
      ['LEMONADE', true],
      ['BREAD', true],
      ['FLOWER', true],
      ['THAI_TEA', false],
      ['MEATBALL', false],
    ]);
    // the Phase 2 fields are still there
    expect(shop.products[0]).toMatchObject({ productCode: 'LEMONADE', quantity: 0, shelfQuantity: 0, onStall: false, unitCost: 5 });
  });

  it('names the shop through the word filter', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    const ok = await http().patch(`${api}/${shop.id}`).set(p.auth).send({ name: 'ร้านน้ำมะนาวป้าแดง' }).expect(200);
    expect(ok.body.name).toBe('ร้านน้ำมะนาวป้าแดง');
    expect((await http().patch(`${api}/${shop.id}`).set(p.auth).send({ name: 'admin shop' }).expect(400)).body.error.code).toBe('SHOP_NAME_REJECTED');
    expect((await http().patch(`${api}/${shop.id}`).set(p.auth).send({ name: 'line.me/x' }).expect(400)).body.error.code).toBe('VALIDATION_FAILED');
    expect((await http().patch(`${api}/${shop.id}`).set(p.auth).send({ name: 'โทร 0812345678' }).expect(400)).body.error.code).toBe('VALIDATION_FAILED');
  });

  it('stall rules: goods come from storage, limited slots and capacity, locked products refused', async () => {
    const p = await newGuest(app);
    await grantGold(prisma, p.userId, 1000);
    const { id } = await shopOf(p);
    expect((await shelf(p, id, 'LEMONADE', 5).expect(409)).body.error.code).toBe('SHOP_NOT_ENOUGH_STOCK');
    await restock(p, id, 'LEMONADE', 30).expect(200);
    expect((await shelf(p, id, 'LEMONADE', 25).expect(409)).body.error.code).toBe('SHOP_SHELF_FULL'); // capacity 20 at level 1

    const after = (await shelf(p, id, 'LEMONADE', 15).expect(200)).body;
    const lemon = after.products.find((x: { productCode: string }) => x.productCode === 'LEMONADE');
    expect(lemon).toMatchObject({ quantity: 15, shelfQuantity: 15, onStall: true });
    expect(after.stall.slotsUsed).toBe(1);

    await restock(p, id, 'BREAD', 10).expect(200);
    await shelf(p, id, 'BREAD', 10).expect(200);
    await restock(p, id, 'FLOWER', 5).expect(200);
    expect((await shelf(p, id, 'FLOWER', 5).expect(409)).body.error.code).toBe('SHOP_SHELF_FULL'); // 2 slots at level 1
    expect((await restock(p, id, 'THAI_TEA', 5).expect(409)).body.error.code).toBe('BUSINESS_PRODUCT_LOCKED');
    expect((await shelf(p, id, 'THAI_TEA', 1).expect(409)).body.error.code).toBe('BUSINESS_PRODUCT_LOCKED');

    // taking goods off the stall returns them to storage and frees the slot
    const back = (await shelf(p, id, 'LEMONADE', 0).expect(200)).body;
    expect(back.products.find((x: { productCode: string }) => x.productCode === 'LEMONADE')).toMatchObject({ quantity: 30, shelfQuantity: 0, onStall: false });
    await shelf(p, id, 'FLOWER', 5).expect(200);
  });

  it('cannot open an empty stall; opening takes a market spot and starts the SHOP_OPEN quest', async () => {
    const p = await newGuest(app);
    const { id } = await shopOf(p);
    await http().post('/api/v1/quests/shop_open_stall/start').set(p.auth).expect(201);
    expect((await status(p, id, 'OPEN').expect(409)).body.error.code).toBe('SHOP_SHELF_EMPTY');
    await restock(p, id, 'LEMONADE', 10).expect(200);
    await shelf(p, id, 'LEMONADE', 10).expect(200);
    const opened = (await status(p, id, 'OPEN').expect(200)).body;
    expect(opened.stall.status).toBe('OPEN');
    expect(opened.stall.slot).toEqual(expect.any(Number));
    const quests = (await http().get('/api/v1/quests').set(p.auth).expect(200)).body;
    expect(quests.find((q: { id: string }) => q.id === 'shop_open_stall').progress.status).toBe('COMPLETED');
    await status(p, id, 'CLOSED').expect(200);
  });

  it('customers buy while the owner is away; money waits in the cash box, not the wallet', async () => {
    const { p, id } = await openStall();
    const walletBefore = await gold(p);
    await timeTravel(id, 60); // one hour away
    const shop = (await http().get(`${api}/${id}`).set(p.auth).expect(200)).body;
    const lemon = shop.products.find((x: { productCode: string }) => x.productCode === 'LEMONADE');
    const sold = 20 - lemon.shelfQuantity;
    expect(sold).toBeGreaterThan(0);
    expect(lemon.quantity).toBe(10); // storage untouched
    expect(shop.stall.cashBox).toBe(sold * 10);
    expect(shop.finance.totalRevenue).toBe(sold * 10);
    expect(shop.totalUnitsSold).toBe(sold);
    expect(shop.stall.totalCustomers).toBeGreaterThan(20);
    expect(await gold(p)).toBe(walletBefore); // nothing moved yet

    const report = (await http().get(`${api}/${id}/report`).set(p.auth).expect(200)).body;
    expect(report).toMatchObject({ hasNews: true, unitsSold: sold, revenue: sold * 10, costOfGoods: sold * 5, profit: sold * 5, cashBox: sold * 10, bestSeller: 'LEMONADE' });
    expect(report.customers).toBe(report.buyers + report.lostTooExpensive + report.lostSoldOut + report.lostNotSelling);
    expect(report.tips.length).toBeGreaterThan(0);
    expect(report.tips[0].textTh).toEqual(expect.any(String));
    expect(report.products.find((x: { productCode: string }) => x.productCode === 'LEMONADE')).toMatchObject({ unitsSold: sold, shelfLeft: 20 - sold, price: 10 });

    // acknowledging the report starts a fresh one
    await http().post(`${api}/${id}/report/ack`).set(p.auth).send({ until: report.until }).expect(204);
    expect((await http().get(`${api}/${id}/report`).set(p.auth).expect(200)).body).toMatchObject({ hasNews: false, customers: 0, tips: [{ code: 'NO_CUSTOMERS' }] });
    await http().post(`${api}/${id}/report/ack`).set(p.auth).send({}).expect(204);
    await status(p, id, 'CLOSED').expect(200);
  });

  it('collect moves the cash box into the wallet exactly once (ledger + idempotency)', async () => {
    const { p, id } = await openStall();
    await timeTravel(id, 90);
    const before = await gold(p);
    const k = key();
    const first = (await http().post(`${api}/${id}/collect`).set(p.auth).set('Idempotency-Key', k).expect(200)).body;
    expect(first.replayed).toBe(false);
    expect(first.collected).toBeGreaterThan(0);
    expect(first.wallet.GOLD).toBe(before + first.collected);
    expect(first.business.stall.cashBox).toBe(0);

    const again = (await http().post(`${api}/${id}/collect`).set(p.auth).set('Idempotency-Key', k).expect(200)).body;
    expect(again).toMatchObject({ replayed: true, collected: first.collected });
    expect(await gold(p)).toBe(before + first.collected);
    expect((await http().post(`${api}/${id}/collect`).set(p.auth).set('Idempotency-Key', key()).expect(409)).body.error.code).toBe('SHOP_NOTHING_TO_COLLECT');
    await http().post(`${api}/${id}/collect`).set(p.auth).expect(400); // key required

    const ledger = await prisma.walletTransaction.findMany({ where: { userId: p.userId, referenceType: 'shop-collect' } });
    expect(ledger).toHaveLength(1);
    expect(ledger[0]).toMatchObject({ type: 'BUSINESS_SALE', amount: first.collected, referenceId: id, balanceAfter: before + first.collected });
    await status(p, id, 'CLOSED').expect(200);
  });

  it('parallel collects with different keys pay the cash box only once', async () => {
    const { p, id } = await openStall();
    await timeTravel(id, 120);
    const box = (await http().get(`${api}/${id}`).set(p.auth).expect(200)).body.stall.cashBox;
    expect(box).toBeGreaterThan(0);
    const before = await gold(p);
    const results = await Promise.all(Array.from({ length: 5 }, () => http().post(`${api}/${id}/collect`).set(p.auth).set('Idempotency-Key', key())));
    expect(results.filter((r) => r.status === 200)).toHaveLength(1);
    expect(results.filter((r) => r.status === 409).every((r) => r.body.error.code === 'SHOP_NOTHING_TO_COLLECT')).toBe(true);
    expect(await gold(p)).toBe(before + box);
    await status(p, id, 'CLOSED').expect(200);
  });

  it('a price change never applies to customers who came before it', async () => {
    const { p, id } = await openStall();
    await timeTravel(id, 45);
    await http().patch(`${api}/${id}/pricing`).set(p.auth).send({ productCode: 'LEMONADE', price: 20 }).expect(200);
    const rows = await prisma.shopSettlement.findMany({ where: { businessId: id } });
    expect(rows).toHaveLength(1);
    expect(rows[0]!.revenue).toBe(rows[0]!.unitsSold * 10); // settled at the OLD price first
    await status(p, id, 'CLOSED').expect(200);
  });

  it('settling twice at the same time never pays a tick twice', async () => {
    const { p, id } = await openStall();
    await timeTravel(id, 60);
    await Promise.all([shopSvc.settleOwned(p.userId, id), shopSvc.settleOwned(p.userId, id), shopSvc.settleDue(), app.get(ShopSettlerService).runOnce()]);
    const rows = await prisma.shopSettlement.findMany({ where: { businessId: id } });
    expect(rows).toHaveLength(1);
    const shop = await prisma.business.findUniqueOrThrow({ where: { id } });
    expect(shop.cashBox).toBe(rows[0]!.revenue);
    expect(shop.settledTick).toBe(shopSvc.nowTick());
    await status(p, id, 'CLOSED').expect(200);
  });

  it('offline selling stops counting after 8 hours', async () => {
    const { p, id } = await openStall();
    await timeTravel(id, 60 * 24 * 3); // three days away
    await http().get(`${api}/${id}`).set(p.auth).expect(200);
    const rows = await prisma.shopSettlement.findMany({ where: { businessId: id } });
    expect(rows).toHaveLength(1);
    expect(rows[0]!.toTick - rows[0]!.fromTick).toBe(SHOP_MAX_OFFLINE_TICKS);
    expect(rows[0]!.unitsSold).toBe(20); // the whole stall sold out
    expect(rows[0]!.lostSoldOut).toBeGreaterThan(0);
    const report = (await http().get(`${api}/${id}/report`).set(p.auth).expect(200)).body;
    expect(report.tips.map((t: { code: string }) => t.code)).toContain('SOLD_OUT_LOST_CUSTOMERS');
    await status(p, id, 'CLOSED').expect(200);
  });

  it('a paused or closed stall gets no customers; closing frees the market spot', async () => {
    const { p, id } = await openStall();
    await status(p, id, 'PAUSED').expect(200);
    await prisma.business.update({ where: { id }, data: { settledTick: { decrement: 120 } } });
    const paused = (await http().get(`${api}/${id}`).set(p.auth).expect(200)).body;
    expect(paused.stall).toMatchObject({ status: 'PAUSED', cashBox: 0 });
    expect(paused.stall.slot).toEqual(expect.any(Number));
    // re-opening starts from now, not from when it was paused
    await status(p, id, 'OPEN').expect(200);
    expect((await http().get(`${api}/${id}`).set(p.auth).expect(200)).body.stall.cashBox).toBe(0);
    const closed = (await status(p, id, 'CLOSED').expect(200)).body;
    expect(closed.stall).toMatchObject({ status: 'CLOSED', slot: null });
    expect(await prisma.shopSettlement.count({ where: { businessId: id } })).toBe(0);
  });

  it('market shows stalls of the same age band only, never a blocked player', async () => {
    const owner = await openStall('UNDER_13');
    const kid = await newGuest(app, 'UNDER_13');
    const adult = await newGuest(app, 'ADULT_18_PLUS');

    const seen = (await http().get('/api/v1/market').set(kid.auth).expect(200)).body;
    expect(seen.slots).toBe(MARKET_STALL_SLOTS);
    const stall = seen.stalls.find((s: { businessId: string }) => s.businessId === owner.id);
    expect(stall).toMatchObject({ isMine: false, status: 'OPEN', level: 1, products: [{ productCode: 'LEMONADE', price: 10, shelfQuantity: 20 }] });
    expect(stall).toMatchObject({ x: expect.any(Number), y: expect.any(Number), ownerName: expect.any(String) });

    const adultView = (await http().get('/api/v1/market').set(adult.auth).expect(200)).body;
    expect(adultView.stalls.some((s: { businessId: string }) => s.businessId === owner.id)).toBe(false);

    const mine = (await http().get('/api/v1/market').set(owner.p.auth).expect(200)).body;
    expect(mine.myShop).toMatchObject({ businessId: owner.id, status: 'OPEN', stallSlot: stall.slot });
    expect(mine.stalls.find((s: { businessId: string }) => s.businessId === owner.id).isMine).toBe(true);

    await http().post(`/api/v1/players/${owner.p.userId}/block`).set(kid.auth).expect(200);
    const afterBlock = (await http().get('/api/v1/market').set(kid.auth).expect(200)).body;
    expect(afterBlock.stalls.some((s: { businessId: string }) => s.businessId === owner.id)).toBe(false);
    await status(owner.p, owner.id, 'CLOSED').expect(200);
  });

  it('a full market still lets a shop open and sell, just without a spot on the map', async () => {
    const filler = await Promise.all(Array.from({ length: MARKET_STALL_SLOTS }, () => newGuest(app, 'ADULT_18_PLUS')));
    for (const [slot, f] of filler.entries()) {
      await prisma.business.updateMany({ where: { userId: f.userId }, data: { stallSlot: slot, marketAgeBand: 'ADULT_18_PLUS', shopStatus: 'PAUSED' } });
    }
    const { p, id, opened } = await openStall('ADULT_18_PLUS');
    expect(opened.stall).toMatchObject({ status: 'OPEN', slot: null });
    await timeTravel(id, 30);
    expect((await http().get(`${api}/${id}`).set(p.auth).expect(200)).body.stall.cashBox).toBeGreaterThan(0);
    await status(p, id, 'CLOSED').expect(200);
    await prisma.business.updateMany({ where: { userId: { in: filler.map((f) => f.userId) } }, data: { stallSlot: null, marketAgeBand: null, shopStatus: 'CLOSED' } });
  });

  it('nobody can touch someone else’s shop', async () => {
    const { p, id } = await openStall();
    const other = await newGuest(app);
    await http().get(`${api}/${id}/report`).set(other.auth).expect(404);
    await http().post(`${api}/${id}/collect`).set(other.auth).set('Idempotency-Key', key()).expect(404);
    await http().put(`${api}/${id}/shelf`).set(other.auth).send({ productCode: 'LEMONADE', quantity: 0 }).expect(404);
    await http().post(`${api}/${id}/status`).set(other.auth).send({ status: 'CLOSED' }).expect(404);
    await http().patch(`${api}/${id}`).set(other.auth).send({ name: 'ร้านของฉัน' }).expect(404);
    await http().post(`${api}/${id}/report/ack`).set(other.auth).send({}).expect(404);
    await http().get(`${api}/${id}/report`).expect(401);
    await status(p, id, 'CLOSED').expect(200);
  });

  it('players in the market see NPC customers at a stall; the owner gets a live cash-box update', async () => {
    const { p, id, opened } = await openStall('TEEN_13_17');
    expect(opened.stall.slot).toEqual(expect.any(Number)); // only stalls with a market spot are animated
    const viewer = await newGuest(app, 'TEEN_13_17');
    const sv = await connectWs(app, viewer.accessToken);
    const so = await connectWs(app, p.accessToken);
    sockets.push(sv, so);
    const join = (await sv.timeout(3000).emitWithAck('zone:join', { zone: 'MARKET' })) as Ack;
    expect(join.ok).toBe(true);

    const activities: { slot: number; visits: { outcome: string; customerType: string; productCode: string }[] }[] = [];
    sv.on('market:activity', (m) => {
      if (m.slot === opened.stall.slot) activities.push(m);
    });
    const update = waitFor<{ businessId: string; cashBox: number }>(so, 'shop:updated', (m) => m.businessId === id, 10000);
    // only the latest minute is animated and it may have had no customers: let a few minutes "pass"
    for (let tries = 0; tries < 20 && activities.length === 0; tries++) {
      await timeTravel(id, 5);
      await app.get(ShopSettlerService).runOnce();
      await new Promise((r) => setTimeout(r, 100));
    }
    expect(activities.length).toBeGreaterThan(0);
    const a = activities[0]!;
    expect(a.visits.length).toBeGreaterThan(0);
    expect(['BOUGHT', 'TOO_EXPENSIVE', 'SOLD_OUT', 'NOT_SELLING']).toContain(a.visits[0]!.outcome);
    expect((await update).cashBox).toBeGreaterThanOrEqual(0);
    await status(p, id, 'CLOSED').expect(200);
  });

  it('the old one-tap sale still works (API kept for compatibility)', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    await restock(p, shop.id, 'LEMONADE', 20).expect(200);
    const sale = await http().post(`${api}/${shop.id}/sell`).set(p.auth).expect(200);
    expect(sale.body.totals).toEqual({ unitsSold: 8, revenue: 80, costOfGoods: 40, profit: 40 });
  });
});
