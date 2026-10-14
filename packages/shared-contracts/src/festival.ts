import { BusinessProductCode } from './game-config';

/**
 * Phase 6 — City festivals.
 *
 * Every week the city prepares for a festival that runs Friday 00:00 to Sunday 24:00 (Bangkok time).
 * While it runs, NPC customers crave the festival's goods, festival-goers come to town, and every
 * featured unit any player sells counts toward a city-wide goal (one goal per age band, because each
 * age band has its own city). Reaching a tier decorates the city and unlocks a reward for everyone
 * who helped.
 *
 * The calendar is pure arithmetic on the clock, so every backend instance (and the offline catch-up
 * of a shop) agrees on which festival a given minute belongs to.
 */

export interface FestivalTierDef {
  tier: 1 | 2 | 3;
  /** featured units the whole city (age band) must sell */
  target: number;
  rewardGold: number;
  rewardXp: number;
  /** tier 3 gives the festival's keepsake furniture */
  rewardItem: boolean;
}

export interface FestivalDef {
  code: string;
  /** Unity Texts key for the name; Thai name kept here for logs and docs */
  nameKey: string;
  nameTh: string;
  /** the craving: demand x FESTIVAL_MAIN_DEMAND */
  mainProduct: BusinessProductCode;
  /** demand x FESTIVAL_SIDE_DEMAND */
  sideProduct: BusinessProductCode;
  /** keepsake furniture for tier 3 (seeded item, not sold in the store) */
  rewardItemId: string;
  /** decoration colour for the client, #RRGGBB */
  color: string;
}

export const FESTIVALS: readonly FestivalDef[] = [
  { code: 'COOL_SUMMER', nameKey: 'festival.cool_summer', nameTh: 'เทศกาลคลายร้อน', mainProduct: 'LEMONADE', sideProduct: 'THAI_TEA', rewardItemId: 'furn_fest_beach_umbrella', color: '#4FC3F7' },
  { code: 'BAKERY', nameKey: 'festival.bakery', nameTh: 'งานขนมอบหอมกรุ่น', mainProduct: 'BREAD', sideProduct: 'THAI_TEA', rewardItemId: 'furn_fest_bread_oven', color: '#FFB74D' },
  { code: 'FLOWER', nameKey: 'festival.flower', nameTh: 'เทศกาลดอกไม้บาน', mainProduct: 'FLOWER', sideProduct: 'LEMONADE', rewardItemId: 'furn_fest_flower_arch', color: '#F06292' },
  { code: 'STREET_FOOD', nameKey: 'festival.street_food', nameTh: 'ถนนคนเดินของกินเล่น', mainProduct: 'MEATBALL', sideProduct: 'BREAD', rewardItemId: 'furn_fest_food_cart', color: '#E57373' },
  { code: 'LANTERN', nameKey: 'festival.lantern', nameTh: 'คืนโคมไฟ', mainProduct: 'THAI_TEA', sideProduct: 'FLOWER', rewardItemId: 'furn_fest_lantern', color: '#FFD54F' },
];

export const FESTIVAL_TIERS: readonly FestivalTierDef[] = [
  { tier: 1, target: 150, rewardGold: 50, rewardXp: 20, rewardItem: false },
  { tier: 2, target: 500, rewardGold: 100, rewardXp: 40, rewardItem: false },
  { tier: 3, target: 1200, rewardGold: 150, rewardXp: 60, rewardItem: true },
];

/** Featured units a player must sell during the festival to share the city's rewards. */
export const FESTIVAL_MIN_CONTRIBUTION = 10;
export const FESTIVAL_MAIN_DEMAND = 3;
export const FESTIVAL_SIDE_DEMAND = 2;
/** more people in town while the festival runs */
export const FESTIVAL_CUSTOMER_RATE = 1.3;

const HOUR = 3600_000;
const DAY = 24 * HOUR;
export const FESTIVAL_WEEK_MS = 7 * DAY;
/** Monday 2026-01-05 00:00 in Bangkok (UTC+7). Week 0 of the calendar. */
export const FESTIVAL_EPOCH_MS = Date.UTC(2026, 0, 4, 17, 0, 0);
/** Friday 00:00 Bangkok, measured from Monday 00:00 */
export const FESTIVAL_START_OFFSET_MS = 4 * DAY;

export interface FestivalWeek {
  weekIndex: number;
  /** unique per run, e.g. "BAKERY-41"; keys the contributions and claims */
  key: string;
  festival: FestivalDef;
  startsAt: number;
  endsAt: number;
}

export function festivalWeek(weekIndex: number): FestivalWeek {
  const n = FESTIVALS.length;
  const festival = FESTIVALS[((weekIndex % n) + n) % n]!;
  const weekStart = FESTIVAL_EPOCH_MS + weekIndex * FESTIVAL_WEEK_MS;
  return {
    weekIndex,
    key: `${festival.code}-${weekIndex}`,
    festival,
    startsAt: weekStart + FESTIVAL_START_OFFSET_MS,
    endsAt: weekStart + FESTIVAL_WEEK_MS,
  };
}

export function festivalWeekIndex(atMs: number): number {
  return Math.floor((atMs - FESTIVAL_EPOCH_MS) / FESTIVAL_WEEK_MS);
}

/** The festival running at `atMs`, or null between festivals. */
export function activeFestival(atMs: number): FestivalWeek | null {
  const w = festivalWeek(festivalWeekIndex(atMs));
  return atMs >= w.startsAt && atMs < w.endsAt ? w : null;
}

/** Parses a festival key back to its week ("BAKERY-41"); null when it does not match the calendar. */
export function festivalByKey(key: string): FestivalWeek | null {
  const m = /^([A-Z_]+)-(-?\d{1,6})$/.exec(key);
  if (!m) return null;
  const w = festivalWeek(Number(m[2]));
  return w.festival.code === m[1] ? w : null;
}

/** How much more NPCs want `product` during `festival` (1 = no change). */
export function festivalDemand(festival: FestivalDef | null | undefined, product: BusinessProductCode): number {
  if (!festival) return 1;
  if (product === festival.mainProduct) return FESTIVAL_MAIN_DEMAND;
  if (product === festival.sideProduct) return FESTIVAL_SIDE_DEMAND;
  return 1;
}

export function festivalTiersReached(points: number): number {
  return FESTIVAL_TIERS.filter((t) => points >= t.target).length;
}
