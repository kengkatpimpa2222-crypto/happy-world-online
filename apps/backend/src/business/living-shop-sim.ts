import {
  BUSINESS_PRODUCT_CODES,
  BUSINESS_PRODUCTS,
  BusinessProductCode,
  businessLevelConfig,
  CityProjectDef,
  crowdMix,
  FESTIVAL_CUSTOMER_RATE,
  festivalDemand,
  FestivalWeek,
  HOURLY_CROWD,
  NPC_BARGAIN_PRICE_RATIO,
  NPC_CUSTOMER_TYPE_CODES,
  NPC_CUSTOMER_TYPES,
  NPC_WANTS_UNLISTED_CHANCE,
  NpcCustomerType,
  NpcCustomerTypeCode,
  NpcVisitOutcome,
  Weather,
  WEATHER_EFFECTS,
} from '@hwo/shared-contracts';

/** One product line on the market stall. */
export interface StallLine {
  productCode: BusinessProductCode;
  shelfQuantity: number;
  avgUnitCost: number;
  sellPrice: number;
}

export interface CustomerVisit {
  tick: number;
  /** order of arrival within the tick (stable ids for the client animation) */
  index: number;
  customerType: NpcCustomerTypeCode;
  productCode: BusinessProductCode;
  outcome: NpcVisitOutcome;
  quantity: number;
  unitPrice: number;
  /** most this customer would pay per unit */
  budget: number;
}

export interface ProductTotals {
  unitsSold: number;
  revenue: number;
  costOfGoods: number;
  /** customers who came for this product (bought or not) */
  wanted: number;
  lostTooExpensive: number;
  lostSoldOut: number;
}

export interface VisitTotals {
  customers: number;
  buyers: number;
  unitsSold: number;
  revenue: number;
  costOfGoods: number;
  profit: number;
  lostTooExpensive: number;
  lostSoldOut: number;
  lostNotSelling: number;
}

export interface SimulationResult {
  visits: CustomerVisit[];
  /** stall after the customers left (same order as the input) */
  stall: StallLine[];
  totals: VisitTotals;
  perProduct: Partial<Record<BusinessProductCode, ProductTotals>>;
  /** festival key -> featured units sold during that festival (Phase 6 city goal) */
  festivalPoints: Record<string, number>;
}

/**
 * What the city is like during one tick (Phase 6 festival, Phase 7 time of day, weather and finished
 * city projects). Missing fields = no effect, so `() => null` is exactly the Phase 5 market.
 */
export interface TickWorld {
  festival?: FestivalWeek | null;
  /** Bangkok hour 0..23.99 */
  hour?: number;
  weather?: Weather;
  built?: readonly CityProjectDef[];
}
export type WorldAt = (tick: number) => TickWorld | null;

// ---------- deterministic randomness ----------

/** FNV-1a 32-bit hash of a string. */
function hash32(s: string): number {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 0x01000193);
  }
  return h >>> 0;
}

/** mulberry32: tiny, fast, good enough for gameplay. Returns floats in [0, 1). */
export function seededRandom(seed: string): () => number {
  let a = hash32(seed);
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function pickWeighted<T>(rng: () => number, items: readonly T[], weight: (t: T) => number): T {
  const total = items.reduce((a, t) => a + weight(t), 0);
  let r = rng() * total;
  let last: T = items[items.length - 1]!;
  for (const t of items) {
    const w = weight(t);
    if (w <= 0) continue;
    last = t;
    r -= w;
    if (r < 0) return t;
  }
  return last;
}

/** Customers arriving in one tick: binomial with mean `rate`, so the stream feels lively but fair. */
function arrivals(rng: () => number, rate: number): number {
  if (rate <= 0) return 0;
  const n = Math.max(1, Math.ceil(rate * 2));
  const p = rate / n;
  let k = 0;
  for (let i = 0; i < n; i++) if (rng() < p) k++;
  return k;
}

// ---------- the simulation ----------

const emptyProduct = (): ProductTotals => ({ unitsSold: 0, revenue: 0, costOfGoods: 0, wanted: 0, lostTooExpensive: 0, lostSoldOut: 0 });

/**
 * NPC customers for ticks (fromTick, toTick]. Pure and deterministic: the customers of tick T depend
 * only on (seed, T), so settling late (offline) gives exactly the same customers as settling on time.
 * What they DO depends on the stall at that moment (price, stock left).
 *
 * Each customer: picks a type, then a product they want (weighted by type preference and product
 * popularity); sometimes a product the stall does not list. They buy 1-3 units if the price is within
 * their budget (one extra unit when it is a bargain), otherwise they leave and the reason is counted
 * so the end-of-day report can teach cause and effect.
 *
 * During a city festival (`festivalAt`) the market is busier, festival-goers come to town and
 * everyone craves the festival's goods; featured units sold are counted per festival.
 */
export function simulateCustomers(
  seed: string,
  level: number,
  fromTick: number,
  toTick: number,
  stallIn: readonly StallLine[],
  worldAt: WorldAt = () => null,
): SimulationResult {
  const stall = stallIn.map((l) => ({ ...l }));
  const visits: CustomerVisit[] = [];
  const totals: VisitTotals = { customers: 0, buyers: 0, unitsSold: 0, revenue: 0, costOfGoods: 0, profit: 0, lostTooExpensive: 0, lostSoldOut: 0, lostNotSelling: 0 };
  const perProduct: Partial<Record<BusinessProductCode, ProductTotals>> = {};
  const festivalPoints: Record<string, number> = {};
  if (stall.length === 0 || toTick <= fromTick) return { visits, stall, totals, perProduct, festivalPoints };

  const { customersPerMinute } = businessLevelConfig(level);
  const unlisted = BUSINESS_PRODUCT_CODES.filter((c) => BUSINESS_PRODUCTS[c].unlockLevel <= level && !stall.some((l) => l.productCode === c));

  for (let tick = fromTick + 1; tick <= toTick; tick++) {
    const rng = seededRandom(`${seed}:${tick}`);
    const world = worldAt(tick) ?? {};
    const fest = world.festival ?? null;
    const built = world.built ?? [];
    const mix = world.hour !== undefined ? crowdMix(world.hour) : {};
    const weather = world.weather ? WEATHER_EFFECTS[world.weather] : null;
    let crowd = fest ? FESTIVAL_CUSTOMER_RATE : 1;
    if (world.hour !== undefined) crowd *= HOURLY_CROWD[Math.floor(world.hour) % 24]!;
    if (weather) crowd *= weather.crowd;
    for (const b of built) crowd *= b.crowd ?? 1;
    const count = arrivals(rng, customersPerMinute * crowd);
    for (let index = 0; index < count; index++) {
      const typeCode = pickWeighted(rng, NPC_CUSTOMER_TYPE_CODES, (c) => {
        const t = NPC_CUSTOMER_TYPES[c] as NpcCustomerType;
        let w = (fest ? (t.festivalWeight ?? t.weight) : t.weight) * (mix[c] ?? 1);
        for (const b of built) w *= b.typeBoost?.[c] ?? 1;
        return w;
      });
      const type = NPC_CUSTOMER_TYPES[typeCode];
      const likes = type.likes as Partial<Record<BusinessProductCode, number>>;
      const like = (c: BusinessProductCode) =>
        (likes[c] ?? 1) * BUSINESS_PRODUCTS[c].baseDemand * festivalDemand(fest?.festival, c) * (weather?.demand[c] ?? 1);
      // Always draw the same number of random values per customer so one customer's choice
      // never shifts the next customer's dice.
      const wantsUnlisted = rng() < NPC_WANTS_UNLISTED_CHANCE;
      const listedPick = pickWeighted(rng, stall, (l) => like(l.productCode));
      const unlistedPick = unlisted.length > 0 ? pickWeighted(rng, unlisted, like) : null;
      const budgetRoll = rng();
      const qtyRoll = rng();

      totals.customers++;
      if (wantsUnlisted && unlistedPick) {
        totals.lostNotSelling++;
        const p = (perProduct[unlistedPick] ??= emptyProduct());
        p.wanted++;
        visits.push({ tick, index, customerType: typeCode, productCode: unlistedPick, outcome: 'NOT_SELLING', quantity: 0, unitPrice: 0, budget: 0 });
        continue;
      }

      const line = listedPick;
      const base = BUSINESS_PRODUCTS[line.productCode].basePrice;
      const budget = Math.round(base * (type.budgetMin + budgetRoll * (type.budgetMax - type.budgetMin)));
      const p = (perProduct[line.productCode] ??= emptyProduct());
      p.wanted++;
      const visit: CustomerVisit = { tick, index, customerType: typeCode, productCode: line.productCode, outcome: 'BOUGHT', quantity: 0, unitPrice: line.sellPrice, budget };

      if (line.shelfQuantity <= 0) {
        visit.outcome = 'SOLD_OUT';
        totals.lostSoldOut++;
        p.lostSoldOut++;
      } else if (line.sellPrice > budget) {
        visit.outcome = 'TOO_EXPENSIVE';
        totals.lostTooExpensive++;
        p.lostTooExpensive++;
      } else {
        let qty = type.qtyMin + Math.floor(qtyRoll * (type.qtyMax - type.qtyMin + 1));
        if (line.sellPrice <= base * NPC_BARGAIN_PRICE_RATIO) qty++;
        qty = Math.min(qty, line.shelfQuantity);
        line.shelfQuantity -= qty;
        visit.quantity = qty;
        const revenue = qty * line.sellPrice;
        const cost = qty * line.avgUnitCost;
        totals.buyers++;
        totals.unitsSold += qty;
        totals.revenue += revenue;
        totals.costOfGoods += cost;
        p.unitsSold += qty;
        p.revenue += revenue;
        p.costOfGoods += cost;
        if (fest && festivalDemand(fest.festival, line.productCode) > 1) {
          festivalPoints[fest.key] = (festivalPoints[fest.key] ?? 0) + qty;
        }
      }
      visits.push(visit);
    }
  }
  totals.profit = totals.revenue - totals.costOfGoods;
  return { visits, stall, totals, perProduct, festivalPoints };
}

// ---------- end-of-day advice ----------

export type ShopTip = 'MANY_TOO_EXPENSIVE' | 'SOLD_OUT_LOST_CUSTOMERS' | 'CUSTOMERS_WANT_MORE_GOODS' | 'SELLING_BELOW_COST' | 'GREAT_DAY' | 'NO_CUSTOMERS';

export const SHOP_TIP_TEXT_TH: Record<ShopTip, string> = {
  MANY_TOO_EXPENSIVE: 'ลูกค้าหลายคนเดินหนีเพราะราคาแพงกว่างบที่มี ลองลดราคาลงนิดนึงดูนะ',
  SOLD_OUT_LOST_CUSTOMERS: 'ของบนแผงหมดก่อนลูกค้าหมด ใส่ของขึ้นแผงเพิ่มจะขายได้อีก',
  CUSTOMERS_WANT_MORE_GOODS: 'มีลูกค้ามาหาสินค้าที่ร้านยังไม่มี ลองวางสินค้าให้หลากหลายขึ้น',
  SELLING_BELOW_COST: 'มีสินค้าที่ขายต่ำกว่าต้นทุน ยิ่งขายยิ่งขาดทุน',
  GREAT_DAY: 'ตั้งราคาและจัดแผงได้ดีมาก ร้านมีกำไร',
  NO_CUSTOMERS: 'ช่วงนี้ยังไม่มีลูกค้า เปิดร้านและวางของบนแผงไว้ ลูกค้าจะเดินมาเอง',
};

/** Picks the lessons worth showing, most useful first (max 3). */
export function shopTips(t: Omit<VisitTotals, 'profit'> & { profit?: number }, sellingBelowCost: boolean): ShopTip[] {
  const tips: ShopTip[] = [];
  if (t.customers === 0) return ['NO_CUSTOMERS'];
  if (sellingBelowCost) tips.push('SELLING_BELOW_COST');
  if (t.lostTooExpensive >= Math.max(2, t.customers * 0.25)) tips.push('MANY_TOO_EXPENSIVE');
  if (t.lostSoldOut >= Math.max(2, t.customers * 0.15)) tips.push('SOLD_OUT_LOST_CUSTOMERS');
  if (t.lostNotSelling >= Math.max(2, t.customers * 0.1)) tips.push('CUSTOMERS_WANT_MORE_GOODS');
  const profit = t.profit ?? t.revenue - t.costOfGoods;
  if (tips.length === 0 && profit > 0) tips.push('GREAT_DAY');
  return tips.slice(0, 3);
}
