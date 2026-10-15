import { QuestObjectiveType } from '@prisma/client';
import {
  bangkokWeekStart,
  CHECKIN_TRACK,
  checkinReward,
  DAILY_MISSION_POOL,
  DAILY_MISSIONS_PER_DAY,
  dailyMissionsFor,
  missionById,
  missionPeriodEndsAt,
  missionPeriodKey,
  missionsForPeriod,
  nextCheckinStep,
  previousMissionPeriodKey,
  QUEST_OBJECTIVES,
  WEEKLY_MISSIONS,
} from '@hwo/shared-contracts';
import { ITEMS } from '../database/seed-data';

const at = (iso: string) => Date.parse(iso);

describe('daily rewards (Phase 11)', () => {
  it('shared QUEST_OBJECTIVES mirror the Prisma enum', () => {
    expect([...QUEST_OBJECTIVES].sort()).toEqual(Object.values(QuestObjectiveType).sort());
  });

  it('check-in track: 7 steps, wraps 7 -> 1, a missed day never resets it', () => {
    expect(CHECKIN_TRACK.map((r) => r.step)).toEqual([1, 2, 3, 4, 5, 6, 7]);
    expect(nextCheckinStep(null)).toBe(1);
    expect(nextCheckinStep(1)).toBe(2);
    expect(nextCheckinStep(6)).toBe(7);
    expect(nextCheckinStep(7)).toBe(1);
    expect(checkinReward(7).item?.itemId).toBe('furn_nang_kwak');
    expect(() => checkinReward(8)).toThrow();
    for (const r of CHECKIN_TRACK) expect(r.gold > 0 || r.item !== null).toBe(true);
  });

  it('every reward item exists in the seeded catalog', () => {
    const ids = new Set(ITEMS.map((i) => i.id));
    const items = [...CHECKIN_TRACK.map((r) => r.item), ...WEEKLY_MISSIONS.map((m) => m.rewardItem), ...DAILY_MISSION_POOL.map((m) => m.rewardItem)];
    for (const it of items) if (it) expect(ids.has(it.itemId)).toBe(true);
  });

  it('Bangkok week starts on Monday 00:00 (UTC+7)', () => {
    // Wed 2026-10-14 noon Bangkok -> Monday 2026-10-12
    expect(bangkokWeekStart(at('2026-10-14T05:00:00Z'))).toBe('2026-10-12');
    // Sunday 23:59 Bangkok still belongs to the week before
    expect(bangkokWeekStart(at('2026-10-18T16:59:00Z'))).toBe('2026-10-12');
    // Monday 00:00 Bangkok = Sunday 17:00 UTC starts a new week
    expect(bangkokWeekStart(at('2026-10-18T17:00:00Z'))).toBe('2026-10-19');
    expect(missionPeriodEndsAt('WEEKLY', at('2026-10-14T05:00:00Z'))).toBe(at('2026-10-18T17:00:00Z'));
  });

  it('period keys roll over at Bangkok midnight, not UTC midnight', () => {
    expect(missionPeriodKey('DAILY', at('2026-10-15T16:59:59Z'))).toBe('D2026-10-15');
    expect(missionPeriodKey('DAILY', at('2026-10-15T17:00:00Z'))).toBe('D2026-10-16');
    expect(previousMissionPeriodKey('DAILY', at('2026-10-15T17:00:00Z'))).toBe('D2026-10-15');
    expect(missionPeriodKey('WEEKLY', at('2026-10-14T05:00:00Z'))).toBe('W2026-10-12');
    expect(previousMissionPeriodKey('WEEKLY', at('2026-10-14T05:00:00Z'))).toBe('W2026-10-05');
  });

  it('daily missions: 3 distinct, stable for a player and day, varied across players and days', () => {
    const a = dailyMissionsFor('user-a', '2026-10-15').map((m) => m.id);
    expect(a).toHaveLength(DAILY_MISSIONS_PER_DAY);
    expect(new Set(a).size).toBe(DAILY_MISSIONS_PER_DAY);
    expect(dailyMissionsFor('user-a', '2026-10-15').map((m) => m.id)).toEqual(a);
    expect(missionsForPeriod('user-a', 'D2026-10-15').map((m) => m.id)).toEqual(a);
    const sets = new Set<string>();
    for (let i = 0; i < 60; i++) sets.add(dailyMissionsFor(`user-${i}`, '2026-10-15').map((m) => m.id).join(','));
    for (let d = 1; d <= 28; d++) sets.add(dailyMissionsFor('user-a', `2026-02-${String(d).padStart(2, '0')}`).map((m) => m.id).join(','));
    expect(sets.size).toBeGreaterThan(30);
  });

  it('over many players every pool mission is used and the spread is fair', () => {
    const count = new Map<string, number>();
    for (let i = 0; i < 3000; i++) for (const m of dailyMissionsFor(`u${i}`, '2026-10-15')) count.set(m.id, (count.get(m.id) ?? 0) + 1);
    const expected = (3000 * DAILY_MISSIONS_PER_DAY) / DAILY_MISSION_POOL.length;
    for (const m of DAILY_MISSION_POOL) {
      expect(count.get(m.id) ?? 0).toBeGreaterThan(expected * 0.7);
      expect(count.get(m.id) ?? 0).toBeLessThan(expected * 1.3);
    }
  });

  it('no mission pushes players to add friends, and ids are unique', () => {
    const all = [...DAILY_MISSION_POOL, ...WEEKLY_MISSIONS];
    expect(all.some((m) => m.objective === 'ADD_FRIEND')).toBe(false);
    expect(new Set(all.map((m) => m.id)).size).toBe(all.length);
    for (const m of all) expect(missionById(m.id)).toBe(m);
    expect(missionsForPeriod('x', 'W2026-10-12').map((m) => m.id)).toEqual(WEEKLY_MISSIONS.map((m) => m.id));
  });
});
