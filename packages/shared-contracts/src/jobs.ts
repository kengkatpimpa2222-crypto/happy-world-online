/**
 * Phase 12 — the first job system: part-time shifts at the city's workplaces.
 *
 * The GDD's prototype needs "one job system". A shift is five short real-life tasks that train a useful skill
 * (project goal #3: learn by playing), paid in Gold through the normal wallet ledger:
 *
 * - CASHIER (minimart in the Plaza, Downtown): give the right change with Thai coins and notes.
 *   Fewest pieces earns a tip, which teaches counting change the way a real cashier does.
 * - FARMHAND (Countryside farm): harvested kilos per row, how many crates are needed (round UP, a crate
 *   that is only half full still counts). Part of the pay is fresh produce for crafting.
 * - PACKER (Industrial packing plant): pack an order exactly with the box sizes on the line, fewest boxes
 *   earns a bonus. Greedy packing is not always best (12 with boxes 9/4/1: 4+4+4 beats 9+1+1+1), so it
 *   rewards planning.
 *
 * Everything is checked on the server: it builds the tasks from a seed it stores, checks every answer, decides
 * the pay and explains the right answer for each task (wrong answers cost nothing, they are a lesson).
 * A non-punishing daily limit: the first JOB_DAILY_PAID_SHIFTS shifts of a Bangkok day are paid, later ones
 * are practice (still checked and explained, no Gold), and skipping a day loses nothing.
 */
import type { MaterialBag, MaterialCode } from './adventure';

export const JOB_CODES = ['CASHIER', 'FARMHAND', 'PACKER'] as const;
export type JobCode = (typeof JOB_CODES)[number];

export interface JobDef {
  /** zone the workplace is in, and where its job board stands */
  zone: string;
  x: number;
  y: number;
  nameTh: string;
  placeTh: string;
  skillTh: string;
  /** Worker skill level needed */
  workerLevel: number;
  tasksPerShift: number;
  /** Gold per correctly done task */
  wagePerTask: number;
  /** extra Gold per task done the best possible way (fewest coins / boxes); 0 = no such bonus */
  perfectBonus: number;
  /** extra Gold when every task of the shift is right */
  shiftBonus: number;
  /** farm work: part of the pay is produce (needs at least JOB_PRODUCE_MIN_CORRECT right) */
  produce: readonly MaterialCode[];
}

export const JOBS: Record<JobCode, JobDef> = {
  CASHIER: {
    zone: 'PLAZA',
    x: 44,
    y: 20,
    nameTh: 'พนักงานแคชเชียร์',
    placeTh: 'มินิมาร์ทหน้าลานเมือง',
    skillTh: 'ทอนเงินให้ถูกและใช้เหรียญน้อยที่สุด',
    workerLevel: 1,
    tasksPerShift: 5,
    wagePerTask: 8,
    perfectBonus: 2,
    shiftBonus: 10,
    produce: [],
  },
  FARMHAND: {
    zone: 'COUNTRYSIDE',
    x: 30,
    y: 26,
    nameTh: 'ผู้ช่วยชาวสวน',
    placeTh: 'สวนลุงมีชนบท',
    skillTh: 'คำนวณลังให้พอใส่ผลผลิต (ปัดขึ้นเสมอ)',
    workerLevel: 1,
    tasksPerShift: 5,
    wagePerTask: 7,
    perfectBonus: 0,
    shiftBonus: 10,
    produce: ['MANGO', 'COCONUT', 'STICKY_RICE'],
  },
  PACKER: {
    zone: 'INDUSTRIAL',
    x: 25,
    y: 20,
    nameTh: 'พนักงานแพ็กของ',
    placeTh: 'โรงงานแพ็กสินค้าเขตอุตสาหกรรม',
    skillTh: 'วางแผนแพ็กให้พอดีและใช้กล่องน้อยที่สุด',
    workerLevel: 2,
    tasksPerShift: 5,
    wagePerTask: 10,
    perfectBonus: 4,
    shiftBonus: 12,
    produce: [],
  },
};

/** Paid shifts per Bangkok day (all jobs together). Later shifts are free practice. */
export const JOB_DAILY_PAID_SHIFTS = 8;
/** A shift can't be handed in faster than this per task (nobody reads a task in under 2 s). */
export const JOB_MIN_SECONDS_PER_TASK = 2;
/** A shift left open longer than this is void (app closed mid-shift); starting a new one is always possible. */
export const JOB_SHIFT_TTL_SECONDS = 900;
/** Each Worker level above 1 adds this share to the shift's pay. */
export const JOB_WAGE_BONUS_PER_LEVEL = 0.05;
export const JOB_WORKER_XP_PER_CORRECT = 2;
export const JOB_WORKER_XP_ALL_CORRECT = 3;
/** Farm produce: given when at least this many tasks were right, this many units of one crop. */
export const JOB_PRODUCE_MIN_CORRECT = 3;
export const JOB_PRODUCE_UNITS = 2;

/** Thai coins and notes a cashier gives change with, biggest first. */
export const CHANGE_DENOMINATIONS = [100, 50, 20, 10, 5, 2, 1] as const;
export const JOB_MAX_PIECES = 99;

// ---------- tasks ----------

export interface ShopItem {
  nameTh: string;
  price: number;
}
export const MINIMART_ITEMS: readonly ShopItem[] = [
  { nameTh: 'น้ำดื่ม', price: 7 },
  { nameTh: 'นมกล่อง', price: 12 },
  { nameTh: 'ขนมปัง', price: 15 },
  { nameTh: 'ไข่ไก่', price: 5 },
  { nameTh: 'บะหมี่กึ่งสำเร็จรูป', price: 6 },
  { nameTh: 'ขนมขบเคี้ยว', price: 20 },
  { nameTh: 'สมุด', price: 25 },
  { nameTh: 'ดินสอ', price: 4 },
  { nameTh: 'ไอศกรีม', price: 18 },
  { nameTh: 'กล้วยหอม', price: 9 },
  { nameTh: 'ยาสีฟัน', price: 35 },
  { nameTh: 'ข้าวกล่อง', price: 39 },
];

export interface CashierTask {
  kind: 'CASHIER';
  items: ShopItem[];
  total: number;
  paid: number;
  /** answer: how many of each of CHANGE_DENOMINATIONS to give back */
  denominations: readonly number[];
}
export interface FarmTask {
  kind: 'FARMHAND';
  rowsKg: number[];
  crateKg: number;
}
export interface PackTask {
  kind: 'PACKER';
  quantity: number;
  /** answer: how many boxes of each size */
  boxSizes: number[];
}
export type JobTask = CashierTask | FarmTask | PackTask;

/** Box lines at the plant; each has a 1-piece box so every order can be packed. */
export const PACK_BOX_SETS: readonly (readonly number[])[] = [
  [9, 4, 1],
  [6, 4, 1],
  [10, 6, 1],
  [8, 5, 1],
];

/** mulberry32: small, fast, and the same on every server for a given seed. */
export function jobRng(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function int(rnd: () => number, min: number, max: number): number {
  return min + Math.floor(rnd() * (max - min + 1));
}

/** The shift's tasks, rebuilt the same way from its seed whenever the server needs them. */
export function jobTasks(job: JobCode, seed: number): JobTask[] {
  const rnd = jobRng(seed);
  const n = JOBS[job].tasksPerShift;
  const out: JobTask[] = [];
  for (let i = 0; i < n; i++) {
    if (job === 'CASHIER') {
      const count = int(rnd, 1, 3);
      const items: ShopItem[] = [];
      for (let k = 0; k < count; k++) items.push(MINIMART_ITEMS[int(rnd, 0, MINIMART_ITEMS.length - 1)]!);
      const total = items.reduce((s, it) => s + it.price, 0);
      // the customer pays with the smallest note that covers it, sometimes with a bigger one
      const notes = [20, 50, 100, 500].filter((v) => v > total);
      const paid = notes[rnd() < 0.7 || notes.length === 1 ? 0 : 1]!;
      out.push({ kind: 'CASHIER', items, total, paid, denominations: CHANGE_DENOMINATIONS });
    } else if (job === 'FARMHAND') {
      const rows = int(rnd, 3, 5);
      const rowsKg = Array.from({ length: rows }, () => int(rnd, 2, 12));
      out.push({ kind: 'FARMHAND', rowsKg, crateKg: [5, 6, 8, 10][int(rnd, 0, 3)]! });
    } else {
      const boxSizes = [...PACK_BOX_SETS[int(rnd, 0, PACK_BOX_SETS.length - 1)]!];
      out.push({ kind: 'PACKER', quantity: int(rnd, 7, 30), boxSizes });
    }
  }
  return out;
}

/** How many numbers an answer to this task has. */
export function answerLength(t: JobTask): number {
  return t.kind === 'CASHIER' ? t.denominations.length : t.kind === 'FARMHAND' ? 1 : t.boxSizes.length;
}

/** Fewest pieces adding up to `amount` with `sizes` (exact; sizes include 1), as counts per size. */
export function fewestPieces(amount: number, sizes: readonly number[]): number[] {
  const best: number[] = [0];
  const from: number[] = [-1];
  for (let a = 1; a <= amount; a++) {
    best[a] = Infinity;
    from[a] = -1;
    sizes.forEach((s, i) => {
      if (s <= a && best[a - s]! + 1 < best[a]!) {
        best[a] = best[a - s]! + 1;
        from[a] = i;
      }
    });
  }
  const counts = sizes.map(() => 0);
  for (let a = amount; a > 0; a -= sizes[from[a]!]!) counts[from[a]!]!++;
  return counts;
}

export interface TaskCheck {
  correct: boolean;
  /** right AND done the best way (fewest coins/boxes); for the farm, same as correct */
  perfect: boolean;
  /** the best answer, shown to the player as the lesson */
  expected: number[];
}

/** Checks one answer. Answers of the wrong shape are never correct (the API rejects them before this). */
export function checkTask(t: JobTask, answer: readonly number[]): TaskCheck {
  if (t.kind === 'FARMHAND') {
    const total = t.rowsKg.reduce((s, v) => s + v, 0);
    const crates = Math.ceil(total / t.crateKg);
    const ok = answer.length === 1 && answer[0] === crates;
    return { correct: ok, perfect: ok, expected: [crates] };
  }
  const amount = t.kind === 'CASHIER' ? t.paid - t.total : t.quantity;
  const sizes = t.kind === 'CASHIER' ? t.denominations : t.boxSizes;
  const expected = fewestPieces(amount, sizes);
  if (answer.length !== sizes.length || answer.some((c) => !Number.isInteger(c) || c < 0)) return { correct: false, perfect: false, expected };
  const sum = answer.reduce((s, c, i) => s + c * sizes[i]!, 0);
  const pieces = answer.reduce((s, c) => s + c, 0);
  const correct = sum === amount;
  return { correct, perfect: correct && pieces === expected.reduce((s, c) => s + c, 0), expected };
}

export interface ShiftPay {
  base: number;
  perfectBonus: number;
  shiftBonus: number;
  skillBonus: number;
  total: number;
}

/** The payslip for a shift (before the daily limit: a practice shift pays 0 of it). */
export function shiftPay(job: JobCode, checks: readonly TaskCheck[], workerLevel: number): ShiftPay {
  const d = JOBS[job];
  const correct = checks.filter((c) => c.correct).length;
  const perfect = d.perfectBonus > 0 ? checks.filter((c) => c.perfect).length : 0;
  const base = correct * d.wagePerTask;
  const perfectBonus = perfect * d.perfectBonus;
  const shiftBonus = correct === checks.length && checks.length > 0 ? d.shiftBonus : 0;
  const skillBonus = Math.floor((base + perfectBonus + shiftBonus) * JOB_WAGE_BONUS_PER_LEVEL * Math.max(0, workerLevel - 1));
  return { base, perfectBonus, shiftBonus, skillBonus, total: base + perfectBonus + shiftBonus + skillBonus };
}

/** Farm produce for a shift: one crop picked by the seed, only when enough tasks were right. */
export function shiftProduce(job: JobCode, seed: number, correct: number): MaterialBag {
  const crops = JOBS[job].produce;
  if (crops.length === 0 || correct < JOB_PRODUCE_MIN_CORRECT) return {};
  return { [crops[(seed >>> 0) % crops.length]!]: JOB_PRODUCE_UNITS };
}

export function shiftWorkerXp(checks: readonly TaskCheck[]): number {
  const correct = checks.filter((c) => c.correct).length;
  return correct * JOB_WORKER_XP_PER_CORRECT + (correct === checks.length && checks.length > 0 ? JOB_WORKER_XP_ALL_CORRECT : 0);
}

/** Least time a shift must have lasted before it can be handed in. */
export function jobMinSeconds(job: JobCode): number {
  return JOBS[job].tasksPerShift * JOB_MIN_SECONDS_PER_TASK;
}
