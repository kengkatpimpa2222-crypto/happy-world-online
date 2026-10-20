import {
  answerLength,
  CashierTask,
  CHANGE_DENOMINATIONS,
  checkTask,
  CITY_ZONES,
  DAILY_MISSION_POOL,
  fewestPieces,
  JOB_CODES,
  JOB_DAILY_PAID_SHIFTS,
  JOBS,
  jobTasks,
  PackTask,
  shiftPay,
  shiftProduce,
  shiftWorkerXp,
  SKILL_MAX_LEVEL,
  TaskCheck,
  zoneById,
} from '@hwo/shared-contracts';

const ok = (n: number, perfect = true): TaskCheck[] => Array.from({ length: n }, () => ({ correct: true, perfect, expected: [] }));
const wrong: TaskCheck = { correct: false, perfect: false, expected: [] };

/** What a first-time cashier does: biggest coin that still fits, every time (the greedy way). */
function greedy(amount: number, sizes: readonly number[]): number[] {
  return sizes.map((s) => {
    const n = Math.floor(amount / s);
    amount -= n * s;
    return n;
  });
}

describe('part-time jobs (Phase 12)', () => {
  it('every workplace stands inside its zone, and the new zones are reachable from the Plaza and back', () => {
    for (const job of JOB_CODES) {
      const d = JOBS[job];
      const zone = zoneById(d.zone);
      expect(zone).toBeDefined();
      expect(d.x).toBeGreaterThan(0);
      expect(d.x).toBeLessThan(zone!.width);
      expect(d.y).toBeLessThan(zone!.height);
      // far enough from portals that walking to the board never changes zone
      for (const p of zone!.portals) expect(Math.hypot(p.x - d.x, p.y - d.y)).toBeGreaterThan(6);
    }
    const plaza = zoneById('PLAZA')!;
    for (const id of ['COUNTRYSIDE', 'INDUSTRIAL']) {
      expect(plaza.portals.some((p) => p.to === id)).toBe(true);
      expect(zoneById(id)!.portals.some((p) => p.to === 'PLAZA')).toBe(true);
    }
    expect(new Set(CITY_ZONES.map((z) => z.id)).size).toBe(CITY_ZONES.length);
  });

  it('tasks are rebuilt the same from the seed and differ between seeds', () => {
    for (const job of JOB_CODES) {
      expect(jobTasks(job, 12345)).toEqual(jobTasks(job, 12345));
      expect(jobTasks(job, 12345)).not.toEqual(jobTasks(job, 54321));
      expect(jobTasks(job, 1)).toHaveLength(JOBS[job].tasksPerShift);
    }
  });

  it('cashier: the customer always pays with one note bigger than the bill', () => {
    for (let seed = 1; seed < 400; seed++) {
      for (const t of jobTasks('CASHIER', seed) as CashierTask[]) {
        expect(t.total).toBe(t.items.reduce((s, i) => s + i.price, 0));
        expect([20, 50, 100, 500]).toContain(t.paid);
        expect(t.paid).toBeGreaterThan(t.total);
        expect(answerLength(t)).toBe(CHANGE_DENOMINATIONS.length);
      }
    }
  });

  it('fewest pieces: Thai change and the packing puzzles', () => {
    // 100 - 63 = 37 = 20 + 10 + 5 + 2
    expect(fewestPieces(37, CHANGE_DENOMINATIONS)).toEqual([0, 0, 1, 1, 1, 1, 0]);
    // 12 with boxes 9/4/1: three 4-boxes, not 9+1+1+1
    expect(fewestPieces(12, [9, 4, 1])).toEqual([0, 3, 0]);
    expect(greedy(12, [9, 4, 1])).toEqual([1, 0, 3]);
  });

  it('packing really needs planning: greedy is not always the best way', () => {
    let tasks = 0;
    let greedyLoses = 0;
    for (let seed = 1; seed < 300; seed++) {
      for (const t of jobTasks('PACKER', seed) as PackTask[]) {
        tasks++;
        const g = greedy(t.quantity, t.boxSizes);
        const check = checkTask(t, g);
        expect(check.correct).toBe(true); // greedy always packs exactly (there is a 1-box) ...
        if (!check.perfect) greedyLoses++; // ... but not always with the fewest boxes
      }
    }
    expect(greedyLoses / tasks).toBeGreaterThan(0.1);
  });

  it('checks answers: right, right the best way, wrong, and the explanation', () => {
    const cashier: CashierTask = { kind: 'CASHIER', items: [{ nameTh: 'x', price: 63 }], total: 63, paid: 100, denominations: CHANGE_DENOMINATIONS };
    expect(checkTask(cashier, [0, 0, 1, 1, 1, 1, 0])).toEqual({ correct: true, perfect: true, expected: [0, 0, 1, 1, 1, 1, 0] });
    expect(checkTask(cashier, [0, 0, 0, 3, 1, 0, 2])).toMatchObject({ correct: true, perfect: false }); // 37 in 6 pieces
    expect(checkTask(cashier, [0, 0, 1, 1, 1, 0, 0])).toMatchObject({ correct: false, perfect: false }); // 35: short by 2
    expect(checkTask(cashier, [0, 0, 1])).toMatchObject({ correct: false }); // wrong shape

    const farm = { kind: 'FARMHAND' as const, rowsKg: [5, 5, 1], crateKg: 5 };
    expect(checkTask(farm, [3])).toEqual({ correct: true, perfect: true, expected: [3] }); // 11 kg needs 3 crates, not 2
    expect(checkTask(farm, [2]).correct).toBe(false);

    const pack: PackTask = { kind: 'PACKER', quantity: 12, boxSizes: [9, 4, 1] };
    expect(checkTask(pack, [0, 3, 0])).toMatchObject({ correct: true, perfect: true });
    expect(checkTask(pack, [1, 0, 3])).toMatchObject({ correct: true, perfect: false });
    expect(checkTask(pack, [1, 1, 0])).toMatchObject({ correct: false }); // 13 pieces for an order of 12
  });

  it('payslip: wage, best-way tips, full-shift bonus and the Worker skill bonus', () => {
    expect(shiftPay('CASHIER', ok(5), 1)).toEqual({ base: 40, perfectBonus: 10, shiftBonus: 10, skillBonus: 0, total: 60 });
    expect(shiftPay('CASHIER', ok(5), 3)).toMatchObject({ skillBonus: 6, total: 66 });
    expect(shiftPay('CASHIER', [...ok(4, false), wrong], 1)).toEqual({ base: 32, perfectBonus: 0, shiftBonus: 0, skillBonus: 0, total: 32 });
    // the farm has no best-way tip (one right number of crates)
    expect(shiftPay('FARMHAND', ok(5), 1)).toMatchObject({ perfectBonus: 0, total: 45 });
    expect(shiftPay('PACKER', [wrong, wrong, wrong, wrong, wrong], 5)).toMatchObject({ total: 0 });
    expect(shiftWorkerXp(ok(5))).toBe(13);
    expect(shiftWorkerXp([...ok(2), wrong, wrong, wrong])).toBe(4);
  });

  it('farm produce: one crop by seed, only for a shift with enough right answers', () => {
    expect(shiftProduce('FARMHAND', 7, 3)).toEqual({ [JOBS.FARMHAND.produce[7 % 3]!]: 2 });
    expect(shiftProduce('FARMHAND', 7, 2)).toEqual({});
    expect(shiftProduce('CASHIER', 7, 5)).toEqual({});
  });

  it('economy: a full day of paid shifts pays at most 1000 Gold, even at Worker level 10', () => {
    const best = Math.max(...JOB_CODES.map((j) => shiftPay(j, ok(JOBS[j].tasksPerShift), SKILL_MAX_LEVEL).total));
    expect(best * JOB_DAILY_PAID_SHIFTS).toBeLessThanOrEqual(1000);
    // a new player can earn a first upgrade's worth in a few shifts (cashier is open from level 1)
    expect(JOBS.CASHIER.workerLevel).toBe(1);
    expect(JOBS.FARMHAND.workerLevel).toBe(1);
  });

  it('a daily mission counts shifts', () => {
    expect(DAILY_MISSION_POOL.some((m) => m.objective === 'JOB_SHIFT_COMPLETE')).toBe(true);
  });
});
