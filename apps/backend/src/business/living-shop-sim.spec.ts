import { marketStallPosition, MARKET_STALL_SLOTS } from '@hwo/shared-contracts';
import { seededRandom, shopTips, simulateCustomers, StallLine } from './living-shop-sim';

const lemonade = (shelfQuantity: number, sellPrice = 10): StallLine => ({ productCode: 'LEMONADE', shelfQuantity, avgUnitCost: 5, sellPrice });

describe('living shop simulation', () => {
  it('is deterministic per (seed, tick): settling late gives the same customers as settling on time', () => {
    const stall = [lemonade(200), { productCode: 'BREAD' as const, shelfQuantity: 200, avgUnitCost: 8, sellPrice: 15 }];
    const allAtOnce = simulateCustomers('shop-a', 1, 1000, 1060, stall);
    // the same hour settled minute by minute
    let current = stall;
    const visits = [];
    for (let t = 1000; t < 1060; t++) {
      const r = simulateCustomers('shop-a', 1, t, t + 1, current);
      visits.push(...r.visits);
      current = r.stall;
    }
    expect(visits).toEqual(allAtOnce.visits);
    expect(current).toEqual(allAtOnce.stall);
  });

  it('different shops get different customers', () => {
    const a = simulateCustomers('shop-a', 1, 0, 30, [lemonade(100)]);
    const b = simulateCustomers('shop-b', 1, 0, 30, [lemonade(100)]);
    expect(a.visits).not.toEqual(b.visits);
  });

  it('money adds up: revenue = units x price, cost = units x average cost, stock never goes negative', () => {
    const r = simulateCustomers('shop-c', 3, 0, 600, [lemonade(15, 9), { productCode: 'FLOWER', shelfQuantity: 40, avgUnitCost: 12, sellPrice: 30 }]);
    const units = r.visits.reduce((a, v) => a + v.quantity, 0);
    expect(r.totals.unitsSold).toBe(units);
    expect(r.totals.revenue).toBe(r.visits.reduce((a, v) => a + v.quantity * v.unitPrice, 0));
    expect(r.totals.profit).toBe(r.totals.revenue - r.totals.costOfGoods);
    expect(r.stall.every((l) => l.shelfQuantity >= 0)).toBe(true);
    expect(r.stall[0]!.shelfQuantity).toBe(0); // 15 lemonades cannot last 10 hours
    expect(r.totals.lostSoldOut).toBeGreaterThan(0);
    expect(r.totals.customers).toBe(r.totals.buyers + r.totals.lostTooExpensive + r.totals.lostSoldOut + r.totals.lostNotSelling);
    // nobody pays more than their budget
    for (const v of r.visits.filter((x) => x.outcome === 'BOUGHT')) expect(v.unitPrice).toBeLessThanOrEqual(v.budget);
  });

  it('teaches pricing: a very high price loses most customers, the market price sells', () => {
    const fair = simulateCustomers('shop-d', 1, 0, 120, [lemonade(1000, 10)]);
    const greedy = simulateCustomers('shop-d', 1, 0, 120, [lemonade(1000, 19)]);
    expect(greedy.totals.lostTooExpensive).toBeGreaterThan(greedy.totals.customers * 0.8);
    expect(fair.totals.buyers).toBeGreaterThan(greedy.totals.buyers * 3);
    expect(fair.totals.profit).toBeGreaterThan(greedy.totals.profit);
  });

  it('level 1 economy: about one customer a minute, a 20-unit stall lasts tens of minutes', () => {
    const hour = simulateCustomers('shop-e', 1, 0, 60, [lemonade(1000)]);
    expect(hour.totals.customers).toBeGreaterThan(35);
    expect(hour.totals.customers).toBeLessThan(90);
    const twenty = simulateCustomers('shop-e', 1, 0, 480, [lemonade(20)]);
    const soldOutAt = twenty.visits.find((v) => v.outcome === 'SOLD_OUT')?.tick ?? 480;
    expect(soldOutAt).toBeGreaterThan(10);
    expect(soldOutAt).toBeLessThan(120);
    expect(twenty.totals.unitsSold).toBe(20);
    expect(twenty.totals.profit).toBe(20 * 5);
  });

  it('customers sometimes ask for unlocked products the stall does not list', () => {
    const r = simulateCustomers('shop-f', 1, 0, 300, [lemonade(1000)]);
    expect(r.totals.lostNotSelling).toBeGreaterThan(0);
    const wanted = new Set(r.visits.filter((v) => v.outcome === 'NOT_SELLING').map((v) => v.productCode));
    expect(wanted.has('THAI_TEA')).toBe(false); // locked until shop level 2
  });

  it('an empty stall or no elapsed ticks means no customers', () => {
    expect(simulateCustomers('x', 1, 0, 100, []).totals.customers).toBe(0);
    expect(simulateCustomers('x', 1, 10, 10, [lemonade(5)]).totals.customers).toBe(0);
  });

  it('tips explain what went wrong, or praise a good day', () => {
    expect(shopTips({ customers: 0, buyers: 0, unitsSold: 0, revenue: 0, costOfGoods: 0, lostTooExpensive: 0, lostSoldOut: 0, lostNotSelling: 0 }, false)).toEqual(['NO_CUSTOMERS']);
    expect(shopTips({ customers: 20, buyers: 5, unitsSold: 8, revenue: 120, costOfGoods: 40, lostTooExpensive: 12, lostSoldOut: 0, lostNotSelling: 3 }, false)).toEqual(['MANY_TOO_EXPENSIVE', 'CUSTOMERS_WANT_MORE_GOODS']);
    expect(shopTips({ customers: 20, buyers: 18, unitsSold: 30, revenue: 300, costOfGoods: 150, lostTooExpensive: 1, lostSoldOut: 1, lostNotSelling: 0 }, false)).toEqual(['GREAT_DAY']);
    expect(shopTips({ customers: 10, buyers: 10, unitsSold: 10, revenue: 50, costOfGoods: 50, lostTooExpensive: 0, lostSoldOut: 0, lostNotSelling: 0 }, true)).toEqual(['SELLING_BELOW_COST']);
  });

  it('seeded random numbers are in [0, 1) and repeatable', () => {
    const a = seededRandom('s');
    const b = seededRandom('s');
    for (let i = 0; i < 1000; i++) {
      const x = a();
      expect(x).toBe(b());
      expect(x).toBeGreaterThanOrEqual(0);
      expect(x).toBeLessThan(1);
    }
  });

  it('stall spots are inside the market and never overlap', () => {
    const seen = new Set<string>();
    for (let s = 0; s < MARKET_STALL_SLOTS; s++) {
      const p = marketStallPosition(s);
      expect(p.x).toBeGreaterThan(2);
      expect(p.x).toBeLessThan(38);
      expect(p.y).toBeGreaterThan(2);
      expect(p.y).toBeLessThan(38);
      expect(Math.abs(p.y - 20)).toBeGreaterThan(4); // the walkway from the portal stays clear
      seen.add(`${p.x},${p.y}`);
    }
    expect(seen.size).toBe(MARKET_STALL_SLOTS);
    expect(() => marketStallPosition(MARKET_STALL_SLOTS)).toThrow();
  });
});
