import { EXPEDITION_PLAN_CODES, MATERIAL_CODES, RECIPES, EXPEDITION_SITE_CODES, EXPEDITION_SITES, ExpeditionPlan, ExpeditionSite, expeditionEventChance, skillLevelForXp } from '@hwo/shared-contracts';
import { bagTotal, rollExpedition } from './expedition-sim';

const SEEDS = Array.from({ length: 2000 }, (_, i) => i * 7919 + 13);
const avg = (site: ExpeditionSite, plan: ExpeditionPlan, level: number, f: (o: ReturnType<typeof rollExpedition>) => number) =>
  SEEDS.reduce((a, s) => a + f(rollExpedition(s, site, plan, level)), 0) / SEEDS.length;

describe('expedition rolls', () => {
  it('are deterministic: same seed, site, plan and level give the same story', () => {
    for (const site of EXPEDITION_SITE_CODES) {
      for (const plan of EXPEDITION_PLAN_CODES) expect(rollExpedition(42, site, plan, 3)).toEqual(rollExpedition(42, site, plan, 3));
    }
    expect(rollExpedition(1, 'ORCHARD', 'STEADY', 1)).not.toEqual(rollExpedition(2, 'ORCHARD', 'STEADY', 1));
  });

  it('have the site event count with no repeats, always bring something home and never go negative', () => {
    for (const site of EXPEDITION_SITE_CODES) {
      for (const plan of EXPEDITION_PLAN_CODES) {
        for (const seed of SEEDS.slice(0, 300)) {
          const o = rollExpedition(seed, site, plan, 1);
          expect(o.events).toHaveLength(EXPEDITION_SITES[site].eventCount);
          expect(new Set(o.events.map((e) => e.event)).size).toBe(o.events.length);
          expect(bagTotal(o.loot)).toBeGreaterThan(0);
          for (const n of Object.values(o.loot)) expect(n).toBeGreaterThan(0);
          // loot = gathered + gained - lost
          const gained = o.events.reduce((a, e) => a + bagTotal(e.gained), 0);
          const lost = o.events.reduce((a, e) => a + bagTotal(e.lost), 0);
          if (bagTotal(o.gathered) + gained - lost > 0) expect(bagTotal(o.loot)).toBe(bagTotal(o.gathered) + gained - lost);
        }
      }
    }
  });

  it('only BOLD trips lose materials on a failed event', () => {
    for (const seed of SEEDS.slice(0, 500)) {
      for (const plan of ['CAREFUL', 'STEADY'] as const) {
        expect(rollExpedition(seed, 'HERB_VALLEY', plan, 1).events.every((e) => bagTotal(e.lost) === 0)).toBe(true);
      }
    }
    expect(SEEDS.some((s) => rollExpedition(s, 'HERB_VALLEY', 'BOLD', 1).events.some((e) => bagTotal(e.lost) > 0))).toBe(true);
  });

  it('plans trade safety for loot: careful < steady < bold on average, careful succeeds most', () => {
    for (const site of EXPEDITION_SITE_CODES) {
      const loot = (p: ExpeditionPlan) => avg(site, p, 1, (o) => bagTotal(o.loot));
      const wins = (p: ExpeditionPlan) => avg(site, p, 1, (o) => o.events.filter((e) => e.success).length);
      expect(loot('CAREFUL')).toBeLessThan(loot('STEADY'));
      expect(loot('STEADY')).toBeLessThan(loot('BOLD'));
      expect(wins('CAREFUL')).toBeGreaterThan(wins('STEADY'));
      expect(wins('STEADY')).toBeGreaterThan(wins('BOLD'));
    }
  });

  it('a better explorer handles more events well', () => {
    const wins = (level: number) => avg('FLOWER_FOREST', 'STEADY', level, (o) => o.events.filter((e) => e.success).length);
    expect(wins(5)).toBeGreaterThan(wins(1) + 0.3);
    expect(expeditionEventChance(0.9, 10, 'CAREFUL')).toBe(0.95); // capped
    expect(expeditionEventChance(0.45, 1, 'BOLD')).toBeCloseTo(0.3);
  });

  it('pacing: Explorer 2 after about 3 orchard trips, 4 (herb valley) after about a dozen more', () => {
    const orchard = avg('ORCHARD', 'STEADY', 1, (o) => o.explorerXp);
    const forest = avg('FLOWER_FOREST', 'STEADY', 2, (o) => o.explorerXp);
    expect(Math.ceil(40 / orchard)).toBe(3);
    const tripsTo4 = 3 + Math.ceil((360 - 3 * orchard) / forest);
    expect(tripsTo4).toBeGreaterThanOrEqual(12);
    expect(tripsTo4).toBeLessThanOrEqual(18);
    expect(skillLevelForXp(39)).toBe(1);
    expect(skillLevelForXp(40)).toBe(2);
    expect(skillLevelForXp(360)).toBe(4);
    expect(skillLevelForXp(1e9)).toBe(10);
  });

  it('every material found on a trip is used by some recipe', () => {
    for (const m of MATERIAL_CODES) expect(Object.values(RECIPES).some((r) => (r.inputs[m] ?? 0) > 0)).toBe(true);
  });

  describe('first visit to a site (Phase 9.1 new-player safety net)', () => {
    const sets = (bag: Partial<Record<string, number>>, recipe: keyof typeof RECIPES) =>
      Math.min(...Object.entries(RECIPES[recipe].inputs).map(([c, n]) => Math.floor((bag[c] ?? 0) / n!)));

    it('always brings home at least one set of the site recipe, whatever the seed, plan or level', () => {
      for (const site of EXPEDITION_SITE_CODES) {
        for (const plan of EXPEDITION_PLAN_CODES) {
          for (const level of [1, 4, 10]) {
            for (const seed of SEEDS.slice(0, 500)) {
              const o = rollExpedition(seed, site, plan, level, { firstVisit: true });
              expect(sets(o.loot, EXPEDITION_SITES[site].firstVisitRecipe)).toBeGreaterThanOrEqual(1);
            }
          }
        }
      }
    });

    it('only tops up what is missing: same story, same loot plus the bonus, bonus limited to the recipe inputs', () => {
      for (const site of EXPEDITION_SITE_CODES) {
        const inputs = RECIPES[EXPEDITION_SITES[site].firstVisitRecipe].inputs as Record<string, number>;
        for (const plan of EXPEDITION_PLAN_CODES) {
          for (const seed of SEEDS.slice(0, 500)) {
            const normal = rollExpedition(seed, site, plan, 1);
            const first = rollExpedition(seed, site, plan, 1, { firstVisit: true });
            expect(first.gathered).toEqual(normal.gathered);
            expect(first.events).toEqual(normal.events);
            expect(first.explorerXp).toBe(normal.explorerXp);
            for (const m of MATERIAL_CODES) {
              const bonus = first.firstVisitBonus[m] ?? 0;
              expect((first.loot[m] ?? 0) - (normal.loot[m] ?? 0)).toBe(bonus);
              if (bonus > 0) {
                expect(inputs[m]).toBeDefined();
                expect(first.loot[m]).toBe(inputs[m]); // raised exactly to one set, never past it
              }
            }
          }
        }
      }
    });

    it('a short first orchard trip is topped up to exactly one set (a known seed)', () => {
      const seed = SEEDS.find((s) => sets(rollExpedition(s, 'ORCHARD', 'STEADY', 1).loot, 'MANGO_STICKY_RICE') === 0)!;
      expect(seed).toBeDefined();
      const normal = rollExpedition(seed, 'ORCHARD', 'STEADY', 1);
      const first = rollExpedition(seed, 'ORCHARD', 'STEADY', 1, { firstVisit: true });
      expect(bagTotal(first.firstVisitBonus)).toBeGreaterThan(0);
      expect(sets(first.loot, 'MANGO_STICKY_RICE')).toBe(1);
      expect(bagTotal(first.loot)).toBe(bagTotal(normal.loot) + bagTotal(first.firstVisitBonus));
    });

    it('later trips never get a bonus', () => {
      for (const seed of SEEDS.slice(0, 300)) expect(rollExpedition(seed, 'ORCHARD', 'BOLD', 1).firstVisitBonus).toEqual({});
    });

    it('is a small one-time gift: on average well under one material per site', () => {
      for (const site of EXPEDITION_SITE_CODES) {
        const level = EXPEDITION_SITES[site].explorerLevel;
        const extra = SEEDS.reduce((a, s) => a + bagTotal(rollExpedition(s, site, 'STEADY', level, { firstVisit: true }).firstVisitBonus), 0) / SEEDS.length;
        expect(extra).toBeLessThan(0.5);
      }
    });

    it('every site names a recipe made from what that site gives', () => {
      for (const site of EXPEDITION_SITE_CODES) {
        const def = EXPEDITION_SITES[site];
        const found = new Set([...Object.keys(def.gather), ...Object.values(def.events).flatMap((e) => Object.keys(e.reward))]);
        for (const m of Object.keys(RECIPES[def.firstVisitRecipe].inputs)) expect(found.has(m)).toBe(true);
        expect(RECIPES[def.firstVisitRecipe].crafterLevel).toBeLessThanOrEqual(2);
      }
    });
  });
});
