import {
  activeFestival,
  BUSINESS_PRODUCTS,
  FESTIVAL_EPOCH_MS,
  FESTIVAL_TIERS,
  FESTIVAL_WEEK_MS,
  festivalByKey,
  festivalTiersReached,
  festivalWeek,
  festivalWeekIndex,
  FESTIVALS,
} from '@hwo/shared-contracts';
import { simulateCustomers, StallLine } from '../business/living-shop-sim';

const DAY = 24 * 3600_000;

describe('festival calendar', () => {
  it('runs Friday 00:00 to Sunday 24:00 Bangkok time', () => {
    // 2026-10-16 is a Friday; 00:00 in Bangkok (UTC+7) is 17:00 UTC the day before.
    const fridayBkk = Date.UTC(2026, 9, 15, 17, 0, 0);
    expect(activeFestival(fridayBkk - 1)).toBeNull();
    const f = activeFestival(fridayBkk);
    expect(f).not.toBeNull();
    expect(f!.startsAt).toBe(fridayBkk);
    expect(f!.endsAt).toBe(fridayBkk + 3 * DAY);
    expect(activeFestival(fridayBkk + 3 * DAY - 1)?.key).toBe(f!.key);
    expect(activeFestival(fridayBkk + 3 * DAY)).toBeNull(); // Monday: the next week's festival is upcoming
  });

  it('rotates through every festival, one per week, with stable keys', () => {
    const seen = new Set<string>();
    for (let w = 0; w < FESTIVALS.length; w++) {
      const fw = festivalWeek(w);
      seen.add(fw.festival.code);
      expect(fw.startsAt).toBe(FESTIVAL_EPOCH_MS + w * FESTIVAL_WEEK_MS + 4 * DAY);
      expect(festivalWeekIndex(fw.startsAt)).toBe(w);
      expect(festivalByKey(fw.key)).toEqual(fw);
    }
    expect(seen.size).toBe(FESTIVALS.length);
    expect(festivalWeek(-1).festival.code).toBe(FESTIVALS[FESTIVALS.length - 1]!.code);
  });

  it('rejects keys that do not match the calendar', () => {
    const fw = festivalWeek(41);
    const other = FESTIVALS.find((f) => f.code !== fw.festival.code)!;
    expect(festivalByKey(`${other.code}-41`)).toBeNull();
    expect(festivalByKey('nope')).toBeNull();
    expect(festivalByKey(`${fw.festival.code}-41; DROP`)).toBeNull();
  });

  it('every festival features a product that level-1 shops can sell', () => {
    for (const f of FESTIVALS) {
      const unlocks = [f.mainProduct, f.sideProduct].map((p) => BUSINESS_PRODUCTS[p].unlockLevel);
      expect(Math.min(...unlocks)).toBe(1);
      expect(f.mainProduct).not.toBe(f.sideProduct);
    }
  });

  it('tiers get harder and count as reached at their target', () => {
    for (let i = 1; i < FESTIVAL_TIERS.length; i++) expect(FESTIVAL_TIERS[i]!.target).toBeGreaterThan(FESTIVAL_TIERS[i - 1]!.target);
    expect(festivalTiersReached(0)).toBe(0);
    expect(festivalTiersReached(FESTIVAL_TIERS[0]!.target - 1)).toBe(0);
    expect(festivalTiersReached(FESTIVAL_TIERS[0]!.target)).toBe(1);
    expect(festivalTiersReached(FESTIVAL_TIERS[2]!.target)).toBe(3);
  });
});

describe('festival in the NPC customer simulation', () => {
  // COOL_SUMMER: lemonade is the craving, Thai tea the side product
  const coolSummer = festivalWeek(FESTIVALS.findIndex((f) => f.code === 'COOL_SUMMER'));
  const always = () => coolSummer;
  const stall = (): StallLine[] => [
    { productCode: 'BREAD', shelfQuantity: 100_000, avgUnitCost: 8, sellPrice: 12 },
    { productCode: 'LEMONADE', shelfQuantity: 100_000, avgUnitCost: 5, sellPrice: 8 },
  ];

  it('brings more customers, festival-goers, and a craving for the featured goods', () => {
    const normal = simulateCustomers('shop-f', 1, 0, 3000, stall());
    const fest = simulateCustomers('shop-f', 1, 0, 3000, stall(), always);
    expect(fest.totals.customers).toBeGreaterThan(normal.totals.customers * 1.15);
    expect(normal.visits.some((v) => v.customerType === 'FESTIVAL_GOER')).toBe(false);
    expect(fest.visits.some((v) => v.customerType === 'FESTIVAL_GOER')).toBe(true);
    const share = (r: typeof normal) => (r.perProduct.LEMONADE?.wanted ?? 0) / r.totals.customers;
    expect(share(fest)).toBeGreaterThan(share(normal) * 1.25);
  });

  it('counts only featured units sold while the festival runs, under its key', () => {
    // festival only during ticks 1500-2999
    const half = (tick: number) => (tick >= 1500 ? coolSummer : null);
    const r = simulateCustomers('shop-g', 1, 0, 3000, stall(), half);
    const featuredDuring = r.visits.filter((v) => v.tick >= 1500 && v.productCode === 'LEMONADE').reduce((a, v) => a + v.quantity, 0);
    expect(featuredDuring).toBeGreaterThan(0);
    expect(r.festivalPoints).toEqual({ [coolSummer.key]: featuredDuring });
    expect(simulateCustomers('shop-g', 1, 0, 1499, stall(), half).festivalPoints).toEqual({});
  });

  it('stays deterministic: offline catch-up during a festival gives the same customers', () => {
    const all = simulateCustomers('shop-h', 2, 100, 160, stall(), always);
    let current = stall();
    const visits = [];
    const points: Record<string, number> = {};
    for (let t = 100; t < 160; t++) {
      const r = simulateCustomers('shop-h', 2, t, t + 1, current, always);
      visits.push(...r.visits);
      current = r.stall;
      for (const [k, v] of Object.entries(r.festivalPoints)) points[k] = (points[k] ?? 0) + v;
    }
    expect(visits).toEqual(all.visits);
    expect(points).toEqual(all.festivalPoints);
  });
});
