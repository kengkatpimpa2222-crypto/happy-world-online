import { NpcCustomerTypeCode } from './game-config';

/**
 * Phase 7 — the city grows. Every unit any player sells adds a "build point" to their city (one city
 * per age band). Points fund a fixed roadmap of public projects, one after another. A finished project
 * stands in the city for good and changes life there (more people, more students, ...), so what players
 * did together last month still matters today.
 */

export interface CityProjectDef {
  code: string;
  nameKey: string;
  nameTh: string;
  /** what it does, in plain Thai */
  effectTh: string;
  /** points this project needs (on top of the projects before it) */
  cost: number;
  /** where it stands (server units, like the rest of the city layout) */
  zone: string;
  x: number;
  y: number;
  /** lasting effects on the market once built */
  crowd?: number;
  typeBoost?: Partial<Record<NpcCustomerTypeCode, number>>;
}

export const CITY_PROJECTS: readonly CityProjectDef[] = [
  { code: 'PARK', nameKey: 'project.park', nameTh: 'สวนสาธารณะ', effectTh: 'คนมาเดินเล่นในเมืองมากขึ้น 5%', cost: 1500, zone: 'PLAZA', x: 15, y: 16, crowd: 1.05 },
  { code: 'BUS_STOP', nameKey: 'project.bus_stop', nameTh: 'ป้ายรถเมล์หน้าตลาด', effectTh: 'นักท่องเที่ยวมาตลาดมากขึ้น 30%', cost: 3500, zone: 'MARKET', x: 6, y: 26, typeBoost: { TOURIST: 1.3 } },
  { code: 'SCHOOL', nameKey: 'project.school', nameTh: 'โรงเรียนประจำเมือง', effectTh: 'นักเรียนมาตลาดมากขึ้น 30%', cost: 7000, zone: 'ACTIVITY', x: 25, y: 28, typeBoost: { STUDENT: 1.3 } },
  { code: 'OFFICE', nameKey: 'project.office', nameTh: 'อาคารสำนักงาน', effectTh: 'พนักงานออฟฟิศมาตลาดมากขึ้น 30%', cost: 12000, zone: 'HOMES', x: 12, y: 30, typeBoost: { WORKER: 1.3 } },
  { code: 'CLOCK_TOWER', nameKey: 'project.clock_tower', nameTh: 'หอนาฬิกากลางเมือง', effectTh: 'เมืองคึกคักขึ้นอีก 5% และคุณยายมาตลาดมากขึ้น 20%', cost: 20000, zone: 'PLAZA', x: 42, y: 42, crowd: 1.05, typeBoost: { GRANDMA: 1.2 } },
];

/** Points a player must give to a project to be listed among its builders. */
export const CITY_BUILDER_MIN_POINTS = 1;

/** Cumulative point totals at which each project is finished. */
export function cityProjectThresholds(): number[] {
  let sum = 0;
  return CITY_PROJECTS.map((p) => (sum += p.cost));
}

/** Index of the project being built at a city total, or CITY_PROJECTS.length when all are done. */
export function cityProjectIndexAt(totalPoints: number): number {
  const t = cityProjectThresholds();
  const i = t.findIndex((x) => totalPoints < x);
  return i < 0 ? CITY_PROJECTS.length : i;
}
