import { BusinessProductCode, NpcCustomerTypeCode } from './game-config';

/**
 * Phase 7 — the time dimension. The city runs on real Bangkok time: day and night, a daily rhythm of
 * customers (students before school, office workers at lunch, tourists at night), three Thai seasons
 * and weather that changes every 3 hours. Everything is pure arithmetic on the clock (plus a hash for
 * the weather), so every backend instance, the offline catch-up of a shop and the client agree.
 */

const HOUR_MS = 3600_000;
export const BANGKOK_UTC_OFFSET_MS = 7 * HOUR_MS;
export const WEATHER_BLOCK_MS = 3 * HOUR_MS;

export type DayPhase = 'NIGHT' | 'DAWN' | 'MORNING' | 'NOON' | 'AFTERNOON' | 'EVENING';
export type Season = 'HOT' | 'RAINY' | 'COOL';
export type Weather = 'SUNNY' | 'HOT' | 'CLOUDY' | 'RAIN';
export const WEATHERS: readonly Weather[] = ['SUNNY', 'HOT', 'CLOUDY', 'RAIN'];

export const DAY_PHASE_TH: Record<DayPhase, string> = {
  NIGHT: 'กลางคืน',
  DAWN: 'เช้ามืด',
  MORNING: 'ช่วงเช้า',
  NOON: 'ช่วงเที่ยง',
  AFTERNOON: 'ช่วงบ่าย',
  EVENING: 'ช่วงเย็น',
};
export const SEASON_TH: Record<Season, string> = { HOT: 'หน้าร้อน', RAINY: 'หน้าฝน', COOL: 'หน้าหนาว' };
export const WEATHER_TH: Record<Weather, string> = { SUNNY: 'แดดดี', HOT: 'ร้อนจัด', CLOUDY: 'ครึ้มฟ้า', RAIN: 'ฝนตก' };

/** Hour of the day in Bangkok, 0..23.999 */
export function bangkokHour(atMs: number): number {
  const d = (atMs + BANGKOK_UTC_OFFSET_MS) % (24 * HOUR_MS);
  return (d < 0 ? d + 24 * HOUR_MS : d) / HOUR_MS;
}

/** Month in Bangkok, 1..12 */
export function bangkokMonth(atMs: number): number {
  return new Date(atMs + BANGKOK_UTC_OFFSET_MS).getUTCMonth() + 1;
}

export function dayPhase(hour: number): DayPhase {
  if (hour < 5) return 'NIGHT';
  if (hour < 7) return 'DAWN';
  if (hour < 11) return 'MORNING';
  if (hour < 14) return 'NOON';
  if (hour < 17) return 'AFTERNOON';
  if (hour < 20) return 'EVENING';
  return 'NIGHT';
}

/** Thai seasons: hot Mar-May, rainy Jun-Oct, cool Nov-Feb. */
export function seasonOf(month: number): Season {
  if (month >= 3 && month <= 5) return 'HOT';
  if (month >= 6 && month <= 10) return 'RAINY';
  return 'COOL';
}

/** Chance of each weather per season (each block of 3 hours draws once). */
export const SEASON_WEATHER: Record<Season, Record<Weather, number>> = {
  HOT: { SUNNY: 0.35, HOT: 0.45, CLOUDY: 0.15, RAIN: 0.05 },
  RAINY: { SUNNY: 0.2, HOT: 0.05, CLOUDY: 0.35, RAIN: 0.4 },
  COOL: { SUNNY: 0.5, HOT: 0.1, CLOUDY: 0.35, RAIN: 0.05 },
};

/** FNV-1a then a mulberry32 step: a stable 0..1 number for a string. */
export function hashUnit(s: string): number {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 0x01000193);
  }
  let t = (h + 0x6d2b79f5) >>> 0;
  t = Math.imul(t ^ (t >>> 15), t | 1);
  t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
}

export function weatherBlock(atMs: number): number {
  return Math.floor(atMs / WEATHER_BLOCK_MS);
}

/** The weather of a 3-hour block. Same for the whole country (and every age band). */
export function weatherOfBlock(block: number): Weather {
  const season = seasonOf(bangkokMonth(block * WEATHER_BLOCK_MS + WEATHER_BLOCK_MS / 2));
  const odds = SEASON_WEATHER[season];
  let r = hashUnit(`weather:${block}`);
  for (const w of WEATHERS) {
    r -= odds[w];
    if (r < 0) return w;
  }
  return 'CLOUDY';
}

export function weatherAt(atMs: number): Weather {
  return weatherOfBlock(weatherBlock(atMs));
}

/** How many people walk the market per hour of the day (averages a little under 1 over 24 h). */
export const HOURLY_CROWD: readonly number[] = [
  0.25, 0.25, 0.25, 0.25, 0.25, 0.25, // 00-05 night
  0.6, // 06 dawn
  1.4, 1.4, // 07-08 going to school / work
  1.0, 1.0, // 09-10
  1.5, 1.5, 1.5, // 11-13 lunch
  1.0, 1.0, // 14-15
  1.4, 1.4, // 16-17 after school
  1.6, 1.6, 1.6, // 18-20 evening market
  1.0, // 21
  0.5, 0.5, // 22-23
];

/** Who is out at which hour: multiplier on each customer type's weight. */
export function crowdMix(hour: number): Partial<Record<NpcCustomerTypeCode, number>> {
  const h = Math.floor(hour);
  if (h >= 6 && h < 10) return { STUDENT: 1.6, WORKER: 1.5, TOURIST: 0.4, GRANDMA: 1.3 };
  if (h >= 11 && h < 14) return { STUDENT: 0.6, WORKER: 2.0, TOURIST: 1.2, GRANDMA: 0.8 };
  if (h >= 15 && h < 18) return { STUDENT: 2.0, WORKER: 0.8 };
  if (h >= 18 && h < 22) return { STUDENT: 1.0, WORKER: 1.3, TOURIST: 1.6, GRANDMA: 0.7 };
  if (h >= 22 || h < 6) return { STUDENT: 0.2, WORKER: 0.6, TOURIST: 2.0, GRANDMA: 0.2 };
  return {};
}

export interface WeatherEffect {
  crowd: number;
  demand: Partial<Record<BusinessProductCode, number>>;
}

export const WEATHER_EFFECTS: Record<Weather, WeatherEffect> = {
  SUNNY: { crowd: 1.1, demand: { FLOWER: 1.3, LEMONADE: 1.2 } },
  HOT: { crowd: 0.9, demand: { LEMONADE: 1.8, THAI_TEA: 1.4, BREAD: 0.8, MEATBALL: 0.8 } },
  CLOUDY: { crowd: 1.0, demand: {} },
  RAIN: { crowd: 0.7, demand: { MEATBALL: 1.5, BREAD: 1.3, THAI_TEA: 1.2, LEMONADE: 0.6, FLOWER: 0.5 } },
};

/** One line the client shows so players can learn the rhythm ("ฝนตก ลูกชิ้นปิ้งขายดี"). */
export const WEATHER_TIP_TH: Record<Weather, string> = {
  SUNNY: 'แดดดี คนออกมาเดินเยอะ ดอกไม้กับน้ำมะนาวขายดี',
  HOT: 'ร้อนจัด! คนอยากได้น้ำมะนาวกับชาไทยเย็น ๆ',
  CLOUDY: 'ครึ้มฟ้า ตลาดเงียบ ๆ สบาย ๆ',
  RAIN: 'ฝนตก คนน้อยลง แต่ลูกชิ้นปิ้งกับขนมปังอุ่น ๆ ขายดี',
};
