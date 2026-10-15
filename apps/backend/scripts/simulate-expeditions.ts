/**
 * Phase 9.1 balance check: how soon can a new player make their first craft, with and without the
 * first-visit safety net? Uses the real rollExpedition, deterministic seeds, nothing random.
 *
 *   cd apps/backend && npx ts-node --transpile-only scripts/simulate-expeditions.ts [players]
 */
import {
  EXPEDITION_PLAN_CODES,
  EXPEDITION_SITE_CODES,
  EXPEDITION_SITES,
  ExpeditionPlan,
  ExpeditionSite,
  MATERIAL_CODES,
  MATERIALS,
  MaterialBag,
  RECIPES,
  RecipeCode,
  skillLevelForXp,
} from '@hwo/shared-contracts';
import { bagTotal, rollExpedition } from '../src/adventure/expedition-sim';

const PLAYERS = Number(process.argv[2] ?? 20000);
const sets = (bag: MaterialBag, r: RecipeCode) =>
  Math.min(...MATERIAL_CODES.filter((c) => RECIPES[r].inputs[c]).map((c) => Math.floor((bag[c] ?? 0) / RECIPES[r].inputs[c]!)));
const pct = (n: number) => ((n / PLAYERS) * 100).toFixed(1) + '%';
const value = (bag: MaterialBag) => MATERIAL_CODES.reduce((a, c) => a + (bag[c] ?? 0) * MATERIALS[c].value, 0);

console.log(`New players: ${PLAYERS}, orchard only (Explorer 1-2), the same plan every trip\n`);
console.log('plan     net  | first craft possible after trip 1 / 2 / 3 | worst case (trips) | avg loot trip 1');
for (const plan of EXPEDITION_PLAN_CODES) {
  for (const net of [false, true]) {
    const after = [0, 0, 0, 0];
    let worst = 0;
    let loot1 = 0;
    for (let p = 0; p < PLAYERS; p++) {
      const bag: MaterialBag = {};
      let xp = 0;
      let first = 0;
      for (let t = 1; t <= 12 && !first; t++) {
        const o = rollExpedition(p * 7919 + t, 'ORCHARD', plan, skillLevelForXp(xp), { firstVisit: net && t === 1 });
        for (const c of MATERIAL_CODES) bag[c] = (bag[c] ?? 0) + (o.loot[c] ?? 0);
        if (t === 1) loot1 += bagTotal(o.loot);
        xp += o.explorerXp;
        if (sets(bag, 'MANGO_STICKY_RICE') > 0) first = t;
      }
      for (let t = 1; t <= 3; t++) if (first && first <= t) after[t]++;
      worst = Math.max(worst, first || 99);
    }
    console.log(`${plan.padEnd(8)} ${net ? 'on ' : 'off'}  | ${pct(after[1]!).padStart(6)} / ${pct(after[2]!).padStart(6)} / ${pct(after[3]!).padStart(6)}            | ${String(worst).padStart(3)}                | ${(loot1 / PLAYERS).toFixed(2)}`);
  }
}

console.log('\nFirst trip to each site (at its unlock level): chance the site recipe can be made, and the one-time cost of the net');
console.log('site           plan     | recipe possible: off / on | avg bonus units | avg bonus value (Gold ref) | normal trip value');
for (const site of EXPEDITION_SITE_CODES) {
  const def = EXPEDITION_SITES[site as ExpeditionSite];
  for (const plan of EXPEDITION_PLAN_CODES as readonly ExpeditionPlan[]) {
    let off = 0;
    let units = 0;
    let bonusValue = 0;
    let tripValue = 0;
    for (let p = 0; p < PLAYERS; p++) {
      const o = rollExpedition(p * 104729 + 1, site, plan, def.explorerLevel, { firstVisit: true });
      const normal = rollExpedition(p * 104729 + 1, site, plan, def.explorerLevel);
      if (sets(normal.loot, def.firstVisitRecipe) > 0) off++;
      units += bagTotal(o.firstVisitBonus);
      bonusValue += value(o.firstVisitBonus);
      tripValue += value(normal.loot);
    }
    console.log(
      `${site.padEnd(14)} ${plan.padEnd(8)} | ${pct(off).padStart(6)} / 100.0%         | ${(units / PLAYERS).toFixed(3).padStart(6)}          | ${(bonusValue / PLAYERS).toFixed(2).padStart(6)}                     | ${(tripValue / PLAYERS).toFixed(1)}`,
    );
  }
}
