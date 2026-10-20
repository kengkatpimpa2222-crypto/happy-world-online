/**
 * Phase 11 — Daily rewards & missions.
 *
 * Design rule (project goal #1): give players a reason to come back, never a punishment for staying away.
 * - The 7-day check-in track moves one step on every day a player checks in. A missed day does NOT reset it:
 *   someone who plays Monday and Thursday still reaches the day-7 gift, just later.
 * - Daily missions are three small things a normal session does anyway (sell, explore, make goods).
 *   A mission finished just before midnight can still be claimed the next day.
 * - Weekly missions are counted in days and totals, not streaks ("visit 4 days this week").
 *
 * Everything here is pure clock maths and tables; the server owns the clock and the rewards.
 */
import { BANGKOK_UTC_OFFSET_MS } from './city-time';
import { bangkokDay, nextBangkokMidnight } from './minigame';
import type { QuestObjective } from './quest-objectives';

const DAY_MS = 24 * 3600_000;

// ---------- check-in ----------

export interface RewardItem {
  itemId: string;
  quantity: number;
}

export interface CheckinReward {
  /** 1..7 */
  step: number;
  gold: number;
  item: RewardItem | null;
}

/** Day-7 gift: a Nang Kwak figure (the Thai shopkeepers' good-luck lady), only obtainable here. */
export const CHECKIN_GIFT_ITEM_ID = 'furn_nang_kwak';

export const CHECKIN_TRACK: readonly CheckinReward[] = [
  { step: 1, gold: 50, item: null },
  { step: 2, gold: 0, item: { itemId: 'mat_mango', quantity: 3 } },
  { step: 3, gold: 80, item: null },
  { step: 4, gold: 0, item: { itemId: 'furn_plant_pot', quantity: 1 } },
  { step: 5, gold: 120, item: null },
  { step: 6, gold: 0, item: { itemId: 'mat_coconut', quantity: 3 } },
  { step: 7, gold: 200, item: { itemId: CHECKIN_GIFT_ITEM_ID, quantity: 1 } },
];

export const CHECKIN_TRACK_LENGTH = CHECKIN_TRACK.length;

/** The step a check-in lands on after `lastStep` (null = never checked in). 7 wraps back to 1. */
export function nextCheckinStep(lastStep: number | null): number {
  if (lastStep === null || lastStep < 1) return 1;
  return (lastStep % CHECKIN_TRACK_LENGTH) + 1;
}

export function checkinReward(step: number): CheckinReward {
  const r = CHECKIN_TRACK[step - 1];
  if (!r) throw new Error(`No check-in step ${step}`);
  return r;
}

// ---------- periods ----------

export type MissionPeriod = 'DAILY' | 'WEEKLY';

/** Monday (Bangkok) of the week containing `atMs`, as "YYYY-MM-DD". */
export function bangkokWeekStart(atMs: number): string {
  const local = atMs + BANGKOK_UTC_OFFSET_MS;
  const dayIndex = Math.floor(local / DAY_MS);
  // 1970-01-01 was a Thursday: (dayIndex + 3) % 7 = days since Monday
  const sinceMonday = (((dayIndex + 3) % 7) + 7) % 7;
  return new Date((dayIndex - sinceMonday) * DAY_MS).toISOString().slice(0, 10);
}

/** Next Monday 00:00 Bangkok after `atMs`, as epoch ms. */
export function nextBangkokWeekStart(atMs: number): number {
  const monday = Date.parse(`${bangkokWeekStart(atMs)}T00:00:00Z`) - BANGKOK_UTC_OFFSET_MS;
  return monday + 7 * DAY_MS;
}

/** Period key stored with each mission row: "D2026-10-15" for a day, "W2026-10-12" for the week starting that Monday. */
export function missionPeriodKey(period: MissionPeriod, atMs: number): string {
  return period === 'DAILY' ? `D${bangkokDay(atMs)}` : `W${bangkokWeekStart(atMs)}`;
}

/** The period just before the one containing `atMs` (its finished missions can still be claimed). */
export function previousMissionPeriodKey(period: MissionPeriod, atMs: number): string {
  return missionPeriodKey(period, atMs - (period === 'DAILY' ? DAY_MS : 7 * DAY_MS));
}

export function missionPeriodEndsAt(period: MissionPeriod, atMs: number): number {
  return period === 'DAILY' ? nextBangkokMidnight(atMs) : nextBangkokWeekStart(atMs);
}

export const MISSION_PERIOD_KEY_PATTERN = /^[DW]\d{4}-\d{2}-\d{2}$/;

export function missionPeriodOfKey(key: string): MissionPeriod {
  return key.startsWith('W') ? 'WEEKLY' : 'DAILY';
}

// ---------- missions ----------

/** What a mission counts: any quest objective, plus two that only missions use. */
export type MissionObjective = QuestObjective | 'CHECK_IN' | 'DAILY_MISSION_CLAIM';

export interface MissionDef {
  id: string;
  period: MissionPeriod;
  objective: MissionObjective;
  target: number;
  rewardGold: number;
  rewardXp: number;
  rewardItem: RewardItem | null;
  titleTh: string;
  titleEn: string;
}

const daily = (id: string, objective: MissionObjective, target: number, rewardGold: number, rewardXp: number, titleTh: string, titleEn: string): MissionDef => ({
  id,
  period: 'DAILY',
  objective,
  target,
  rewardGold,
  rewardXp,
  rewardItem: null,
  titleTh,
  titleEn,
});

/**
 * Daily pool: every mission is something a level-1 player can finish in one 5-15 minute session.
 * ADD_FRIEND is left out on purpose: no reward should push children to friend strangers.
 */
export const DAILY_MISSION_POOL: readonly MissionDef[] = [
  daily('d_sell_10', 'BUSINESS_SELL_UNITS', 10, 40, 10, 'ขายสินค้าให้ได้ 10 ชิ้น', 'Sell 10 goods'),
  daily('d_serve_5', 'SHOP_SERVE_CUSTOMERS', 5, 40, 10, 'ต้อนรับลูกค้าที่แผงร้าน 5 คน', 'Serve 5 customers at your stall'),
  daily('d_profit_50', 'BUSINESS_EARN_PROFIT', 50, 40, 10, 'ทำกำไรให้ได้ 50 Gold', 'Make 50 Gold profit'),
  daily('d_restock_10', 'BUSINESS_RESTOCK', 10, 30, 8, 'เติมสต็อกสินค้า 10 ชิ้น', 'Restock 10 goods'),
  daily('d_minigame_1', 'PLAY_MINIGAME', 1, 30, 10, 'ทำสินค้าเองด้วยมินิเกม 1 รอบ', 'Make goods by hand once'),
  daily('d_explore_1', 'EXPEDITION_COMPLETE', 1, 40, 10, 'ออกสำรวจให้สำเร็จ 1 ครั้ง', 'Finish 1 expedition'),
  daily('d_gather_8', 'GATHER_MATERIAL', 8, 30, 8, 'เก็บวัตถุดิบ 8 ชิ้น', 'Gather 8 materials'),
  daily('d_craft_1', 'CRAFT_ITEM', 1, 50, 12, 'แปรรูปสินค้า 1 ครั้ง', 'Craft once'),
  daily('d_decorate_1', 'PLACE_FURNITURE', 1, 30, 8, 'จัดเฟอร์นิเจอร์ในบ้าน 1 ชิ้น', 'Place 1 piece of furniture at home'),
  daily('d_job_1', 'JOB_SHIFT_COMPLETE', 1, 30, 8, 'ทำงานพาร์ตไทม์ให้ครบ 1 กะ', 'Work 1 part-time shift'),
];

export const DAILY_MISSIONS_PER_DAY = 3;

export const WEEKLY_MISSIONS: readonly MissionDef[] = [
  {
    id: 'w_visit_4',
    period: 'WEEKLY',
    objective: 'CHECK_IN',
    target: 4,
    rewardGold: 150,
    rewardXp: 30,
    rewardItem: { itemId: 'mat_sticky_rice', quantity: 3 },
    titleTh: 'แวะมาเช็กอิน 4 วันในสัปดาห์นี้ (ไม่ต้องติดกัน)',
    titleEn: 'Check in on 4 days this week (any days)',
  },
  {
    id: 'w_daily_10',
    period: 'WEEKLY',
    objective: 'DAILY_MISSION_CLAIM',
    target: 10,
    rewardGold: 250,
    rewardXp: 50,
    rewardItem: { itemId: 'furn_wood_chair', quantity: 1 },
    titleTh: 'รับรางวัลภารกิจรายวัน 10 ครั้ง',
    titleEn: 'Claim 10 daily missions',
  },
  {
    id: 'w_sell_100',
    period: 'WEEKLY',
    objective: 'BUSINESS_SELL_UNITS',
    target: 100,
    rewardGold: 200,
    rewardXp: 40,
    rewardItem: null,
    titleTh: 'ขายสินค้ารวม 100 ชิ้น',
    titleEn: 'Sell 100 goods',
  },
  {
    id: 'w_explore_5',
    period: 'WEEKLY',
    objective: 'EXPEDITION_COMPLETE',
    target: 5,
    rewardGold: 150,
    rewardXp: 40,
    rewardItem: null,
    titleTh: 'ออกสำรวจให้สำเร็จ 5 ครั้ง',
    titleEn: 'Finish 5 expeditions',
  },
];

const ALL_MISSIONS = new Map([...DAILY_MISSION_POOL, ...WEEKLY_MISSIONS].map((m) => [m.id, m]));

export function missionById(id: string): MissionDef | undefined {
  return ALL_MISSIONS.get(id);
}

/** 32-bit FNV-1a, so every server (and the client, if it wants) picks the same missions. */
function fnv1a(s: string): number {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 0x01000193) >>> 0;
  }
  return h >>> 0;
}

/**
 * The player's three daily missions for a Bangkok day: a deterministic shuffle of the pool seeded by
 * player and day, so they differ between players and days but never change during the day.
 */
export function dailyMissionsFor(userId: string, day: string): MissionDef[] {
  const pool = [...DAILY_MISSION_POOL];
  let seed = fnv1a(`${userId}|${day}`);
  const picked: MissionDef[] = [];
  while (picked.length < DAILY_MISSIONS_PER_DAY && pool.length > 0) {
    seed = Math.imul(seed ^ (seed >>> 15), 0x2c1b3c6d) >>> 0;
    seed = (seed ^ (seed >>> 12)) >>> 0;
    picked.push(pool.splice(seed % pool.length, 1)[0]!);
  }
  return picked;
}

/** Missions of a period key for this player (daily: the day's three; weekly: all weekly missions). */
export function missionsForPeriod(userId: string, periodKey: string): MissionDef[] {
  return missionPeriodOfKey(periodKey) === 'DAILY' ? dailyMissionsFor(userId, periodKey.slice(1)) : [...WEEKLY_MISSIONS];
}
