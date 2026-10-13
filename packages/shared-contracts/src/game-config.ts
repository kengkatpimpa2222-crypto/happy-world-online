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
export const BUSINESS_PRODUCTS = {
  LEMONADE: { unitCost: 5, minPrice: 6, maxPrice: 20, basePrice: 10, baseDemand: 8 },
  BREAD: { unitCost: 8, minPrice: 10, maxPrice: 30, basePrice: 15, baseDemand: 6 },
  FLOWER: { unitCost: 12, minPrice: 15, maxPrice: 45, basePrice: 25, baseDemand: 4 },
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
}

export const BUSINESS_LEVELS: readonly BusinessLevelConfig[] = [
  { level: 1, salesCapacity: 10, demandMultiplier: 1.0, stockCapacity: 50, upgradeCost: 300 },
  { level: 2, salesCapacity: 15, demandMultiplier: 1.2, stockCapacity: 80, upgradeCost: 800 },
  { level: 3, salesCapacity: 22, demandMultiplier: 1.4, stockCapacity: 120, upgradeCost: 1800 },
  { level: 4, salesCapacity: 30, demandMultiplier: 1.7, stockCapacity: 180, upgradeCost: 4000 },
  { level: 5, salesCapacity: 40, demandMultiplier: 2.0, stockCapacity: 250, upgradeCost: null },
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

// ---- Real-money products (prices are set in Google Play Console; values here are test assumptions) ----
export const IAP_PRODUCTS = {
  gems_small: { kind: 'consumable', gems: 60, referencePriceThb: 39 },
  gems_medium: { kind: 'consumable', gems: 180, referencePriceThb: 99 },
  vip_monthly: { kind: 'subscription', gems: 0, referencePriceThb: 99 },
} as const;
export type IapProductId = keyof typeof IAP_PRODUCTS;
