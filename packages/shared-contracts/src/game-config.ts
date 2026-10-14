/**
 * Game balance configuration shared by server (authoritative) and client (display only).
 * The client must never compute rewards from this; it only uses it to render UI hints.
 */

export const STARTING_GOLD = 500;
export const STARTING_GEMS = 0;
export const STARTER_ITEM_IDS = ['outfit_basic_tee', 'hair_short_brown', 'furn_wood_chair'] as const;

// ---- Player level ----
/** XP needed to reach `level` (level 1 = 0 XP). Quadratic curve: 50 * (L-1)^2 */
export function xpForLevel(level: number): number {
  return 50 * (level - 1) * (level - 1);
}
export function levelForXp(xp: number): number {
  return Math.floor(Math.sqrt(Math.max(0, xp) / 50)) + 1;
}

// ---- Business simulation ----
/** `unlockLevel` = shop level needed to stock the product (Phase 5). */
export const BUSINESS_PRODUCTS = {
  LEMONADE: { unitCost: 5, minPrice: 6, maxPrice: 20, basePrice: 10, baseDemand: 8, unlockLevel: 1 },
  BREAD: { unitCost: 8, minPrice: 10, maxPrice: 30, basePrice: 15, baseDemand: 6, unlockLevel: 1 },
  FLOWER: { unitCost: 12, minPrice: 15, maxPrice: 45, basePrice: 25, baseDemand: 4, unlockLevel: 1 },
  THAI_TEA: { unitCost: 7, minPrice: 9, maxPrice: 30, basePrice: 15, baseDemand: 7, unlockLevel: 2 },
  MEATBALL: { unitCost: 10, minPrice: 12, maxPrice: 40, basePrice: 20, baseDemand: 6, unlockLevel: 3 },
} as const;
export type BusinessProductCode = keyof typeof BUSINESS_PRODUCTS;
export const BUSINESS_PRODUCT_CODES = Object.keys(BUSINESS_PRODUCTS) as BusinessProductCode[];

export interface BusinessLevelConfig {
  level: number;
  /** max units sold per product per sale round */
  salesCapacity: number;
  demandMultiplier: number;
  /** max units of each product the shop can hold */
  stockCapacity: number;
  /** Gold cost to upgrade FROM this level to the next; null = max level */
  upgradeCost: number | null;
  /** Living shop: how many different products fit on the market stall */
  shelfSlots: number;
  /** Living shop: max units of one product on the stall */
  shelfCapacity: number;
  /** Living shop: average NPC customers per minute while the stall is open */
  customersPerMinute: number;
}

export const BUSINESS_LEVELS: readonly BusinessLevelConfig[] = [
  { level: 1, salesCapacity: 10, demandMultiplier: 1.0, stockCapacity: 50, upgradeCost: 300, shelfSlots: 2, shelfCapacity: 20, customersPerMinute: 1.0 },
  { level: 2, salesCapacity: 15, demandMultiplier: 1.2, stockCapacity: 80, upgradeCost: 800, shelfSlots: 3, shelfCapacity: 30, customersPerMinute: 1.3 },
  { level: 3, salesCapacity: 22, demandMultiplier: 1.4, stockCapacity: 120, upgradeCost: 1800, shelfSlots: 4, shelfCapacity: 45, customersPerMinute: 1.6 },
  { level: 4, salesCapacity: 30, demandMultiplier: 1.7, stockCapacity: 180, upgradeCost: 4000, shelfSlots: 5, shelfCapacity: 60, customersPerMinute: 2.0 },
  { level: 5, salesCapacity: 40, demandMultiplier: 2.0, stockCapacity: 250, upgradeCost: null, shelfSlots: 5, shelfCapacity: 80, customersPerMinute: 2.5 },
];
export const BUSINESS_MAX_LEVEL = BUSINESS_LEVELS.length;

export function businessLevelConfig(level: number): BusinessLevelConfig {
  const cfg = BUSINESS_LEVELS[level - 1];
  if (!cfg) throw new Error(`Unknown business level ${level}`);
  return cfg;
}

/**
 * Demand model: customers buy less when the price is above the reference price.
 * Deterministic on purpose so players can learn cause and effect.
 */
export function expectedDemand(code: BusinessProductCode, price: number, level: number): number {
  const p = BUSINESS_PRODUCTS[code];
  const cfg = businessLevelConfig(level);
  const priceFactor = Math.pow(p.basePrice / price, 1.5);
  return Math.max(0, Math.floor(p.baseDemand * cfg.demandMultiplier * priceFactor));
}

// ---- Living shop (Phase 5) ----
/** NPC customers arrive in discrete ticks so offline catch-up gives exactly the same result as being online. */
export const SHOP_TICK_SECONDS = 60;
/** A stall sells on its own for at most this many ticks (8 hours) after the owner's last visit. */
export const SHOP_MAX_OFFLINE_TICKS = 8 * 60;
/** Chance that a customer wants a product the stall does not list (teaches assortment). */
export const NPC_WANTS_UNLISTED_CHANCE = 0.15;
/** Price at or below this share of the market price makes a buying customer take one extra unit. */
export const NPC_BARGAIN_PRICE_RATIO = 0.85;

export type ShopStatusValue = 'CLOSED' | 'OPEN' | 'PAUSED';

export interface NpcCustomerType {
  /** relative chance to be this type */
  weight: number;
  /** relative chance while a city festival is running (Phase 6); missing = same as weight */
  festivalWeight?: number;
  /** budget per unit = market (base) price x uniform[budgetMin, budgetMax] */
  budgetMin: number;
  budgetMax: number;
  qtyMin: number;
  qtyMax: number;
  /** how much this type prefers each product (missing = 1) */
  likes: Partial<Record<BusinessProductCode, number>>;
}

export const NPC_CUSTOMER_TYPES = {
  STUDENT: { weight: 35, budgetMin: 0.7, budgetMax: 1.15, qtyMin: 1, qtyMax: 2, likes: { LEMONADE: 3, THAI_TEA: 3, MEATBALL: 2 } },
  WORKER: { weight: 30, budgetMin: 0.9, budgetMax: 1.4, qtyMin: 1, qtyMax: 2, likes: { BREAD: 3, THAI_TEA: 2, MEATBALL: 3 } },
  TOURIST: { weight: 15, budgetMin: 1.0, budgetMax: 1.8, qtyMin: 1, qtyMax: 3, likes: { FLOWER: 4, LEMONADE: 2 } },
  GRANDMA: { weight: 20, budgetMin: 0.75, budgetMax: 1.25, qtyMin: 2, qtyMax: 3, likes: { BREAD: 3, FLOWER: 3 } },
  // Only comes to town for festivals (weight 0 otherwise, so ordinary days are unchanged).
  FESTIVAL_GOER: { weight: 0, festivalWeight: 25, budgetMin: 1.1, budgetMax: 1.7, qtyMin: 1, qtyMax: 3, likes: {} },
} as const satisfies Record<string, NpcCustomerType>;
export type NpcCustomerTypeCode = keyof typeof NPC_CUSTOMER_TYPES;
export const NPC_CUSTOMER_TYPE_CODES = Object.keys(NPC_CUSTOMER_TYPES) as NpcCustomerTypeCode[];

export type NpcVisitOutcome = 'BOUGHT' | 'TOO_EXPENSIVE' | 'SOLD_OUT' | 'NOT_SELLING';

/** Stalls visible in one market (per age band). Extra shops still sell, just without a spot on the map. */
export const MARKET_STALL_SLOTS = 24;
export const MARKET_ZONE_ID = 'MARKET';

/**
 * Where stall `slot` stands in the 40x40 MARKET zone: 4 rows of 6, leaving the y=20 walkway
 * from the portal (x=2) clear. `face` is the side customers stand on (+1 = larger y).
 */
export function marketStallPosition(slot: number): { x: number; y: number; face: 1 | -1 } {
  if (!Number.isInteger(slot) || slot < 0 || slot >= MARKET_STALL_SLOTS) throw new Error(`Unknown stall slot ${slot}`);
  const row = Math.floor(slot / 6);
  const col = slot % 6;
  const ys = [7, 14, 26, 33] as const;
  return { x: 10 + col * 5, y: ys[row]!, face: row % 2 === 0 ? 1 : -1 };
}

// ---- Real-money products (prices are set in Google Play Console; values here are test assumptions) ----
export const IAP_PRODUCTS = {
  gems_small: { kind: 'consumable', gems: 60, referencePriceThb: 39 },
  gems_medium: { kind: 'consumable', gems: 180, referencePriceThb: 99 },
  vip_monthly: { kind: 'subscription', gems: 0, referencePriceThb: 99 },
} as const;
export type IapProductId = keyof typeof IAP_PRODUCTS;
