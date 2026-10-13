import { INestApplication } from '@nestjs/common';
import { PrismaService } from '../src/prisma/prisma.service';
import { createTestApp, grantGold, key, newGuest, TestPlayer } from './helpers';

describe('Business simulation', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];

  beforeAll(async () => ({ app, prisma, http } = await createTestApp()));
  afterAll(async () => app.close());

  const shopOf = async (p: TestPlayer) => (await http().get('/api/v1/businesses').set(p.auth).expect(200)).body[0];
  const gold = async (p: TestPlayer) => (await http().get('/api/v1/player/wallet').set(p.auth)).body.balances.GOLD as number;

  it('every new player owns a level 1 lemonade stand', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    expect(shop).toMatchObject({ level: 1, upgradeCost: 300, stockCapacity: 50, finance: { totalRevenue: 0, totalExpense: 0, profit: 0 } });
    const one = await http().get(`/api/v1/businesses/${shop.id}`).set(p.auth).expect(200);
    expect(one.body.id).toBe(shop.id);
  });

  it('restock -> set price -> sell: revenue, cost and profit are computed by the server', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    const rs = await http().post(`/api/v1/businesses/${shop.id}/restock`).set(p.auth).set('Idempotency-Key', key()).send({ productCode: 'LEMONADE', quantity: 20 }).expect(200);
    expect(rs.body.cost).toBe(100);
    expect(await gold(p)).toBe(400);

    await http().patch(`/api/v1/businesses/${shop.id}/pricing`).set(p.auth).send({ productCode: 'LEMONADE', price: 10 }).expect(200);
    const sale = await http().post(`/api/v1/businesses/${shop.id}/sell`).set(p.auth).expect(200);
    // level 1, price 10 = base price -> demand 8, capacity 10
    expect(sale.body.totals).toEqual({ unitsSold: 8, revenue: 80, costOfGoods: 40, profit: 40 });
    expect(sale.body.lines[0].tipsText.length).toBeGreaterThan(0);
    expect(sale.body.business.products[0].quantity).toBe(12);
    expect(await gold(p)).toBe(480);
  });

  it('price must stay within the allowed range', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    const res = await http().patch(`/api/v1/businesses/${shop.id}/pricing`).set(p.auth).send({ productCode: 'LEMONADE', price: 999 }).expect(400);
    expect(res.body.error.code).toBe('BUSINESS_PRICE_OUT_OF_RANGE');
  });

  it('high prices reduce sales and explain why', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    await http().post(`/api/v1/businesses/${shop.id}/restock`).set(p.auth).set('Idempotency-Key', key()).send({ productCode: 'LEMONADE', quantity: 10 }).expect(200);
    await http().patch(`/api/v1/businesses/${shop.id}/pricing`).set(p.auth).send({ productCode: 'LEMONADE', price: 20 }).expect(200);
    const sale = await http().post(`/api/v1/businesses/${shop.id}/sell`).set(p.auth).expect(200);
    expect(sale.body.lines[0].tips).toContain('PRICE_TOO_HIGH');
    expect(sale.body.totals.unitsSold).toBeLessThan(8);
  });

  it('cannot sell with an empty shop', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    const res = await http().post(`/api/v1/businesses/${shop.id}/sell`).set(p.auth).expect(409);
    expect(res.body.error.code).toBe('BUSINESS_NOTHING_TO_SELL');
  });

  it('restock respects stock capacity and is idempotent', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    const k = key();
    await http().post(`/api/v1/businesses/${shop.id}/restock`).set(p.auth).set('Idempotency-Key', k).send({ productCode: 'LEMONADE', quantity: 40 }).expect(200);
    const replay = await http().post(`/api/v1/businesses/${shop.id}/restock`).set(p.auth).set('Idempotency-Key', k).send({ productCode: 'LEMONADE', quantity: 40 }).expect(200);
    expect(replay.body.replayed).toBe(true);
    expect(await gold(p)).toBe(300);
    const full = await http().post(`/api/v1/businesses/${shop.id}/restock`).set(p.auth).set('Idempotency-Key', key()).send({ productCode: 'LEMONADE', quantity: 11 }).expect(409);
    expect(full.body.error.code).toBe('BUSINESS_STOCK_FULL');
    expect(await gold(p)).toBe(300); // failed restock refunded by rollback
  });

  it('upgrades the shop once even on double-tap, then needs enough Gold', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    const res = await Promise.all([
      http().post(`/api/v1/businesses/${shop.id}/upgrade`).set(p.auth).send({ expectedLevel: 1 }),
      http().post(`/api/v1/businesses/${shop.id}/upgrade`).set(p.auth).send({ expectedLevel: 1 }),
    ]);
    expect(res.map((r) => r.status)).toEqual([200, 200]);
    expect(res.map((r) => r.body.replayed).sort()).toEqual([false, true]);
    expect((await shopOf(p)).level).toBe(2);
    expect(await gold(p)).toBe(200);

    const poor = await http().post(`/api/v1/businesses/${shop.id}/upgrade`).set(p.auth).send({ expectedLevel: 2 }).expect(409);
    expect(poor.body.error.code).toBe('INSUFFICIENT_FUNDS');
    const stale = await http().post(`/api/v1/businesses/${shop.id}/upgrade`).set(p.auth).send({ expectedLevel: 3 }).expect(409);
    expect(stale.body.error.code).toBe('BUSINESS_LEVEL_CHANGED');
  });

  it('stops at max level', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    await grantGold(prisma, p.userId, 10_000);
    for (let lvl = 1; lvl < 5; lvl++) {
      await http().post(`/api/v1/businesses/${shop.id}/upgrade`).set(p.auth).send({ expectedLevel: lvl }).expect(200);
    }
    const res = await http().post(`/api/v1/businesses/${shop.id}/upgrade`).set(p.auth).send({ expectedLevel: 5 }).expect(409);
    expect(res.body.error.code).toBe('BUSINESS_MAX_LEVEL');
    expect(await gold(p)).toBe(10_500 - 300 - 800 - 1800 - 4000);
  });
});
