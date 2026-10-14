import {
  EXPEDITION_PLANS,
  EXPEDITION_SITES,
  EXPEDITION_XP_PER_SUCCESS,
  ExpeditionPlan,
  ExpeditionSite,
  expeditionEventChance,
  MATERIAL_CODES,
  MaterialBag,
  MaterialCode,
} from '@hwo/shared-contracts';
import { pickWeighted, seededRandom } from '../business/living-shop-sim';

export interface ExpeditionEventOutcome {
  /** event code, e.g. MONKEYS (the client tells the story from it) */
  event: string;
  success: boolean;
  /** materials gained (success) */
  gained: MaterialBag;
  /** materials lost (a failed event on a BOLD trip) */
  lost: MaterialBag;
}

export interface ExpeditionOutcome {
  /** what was gathered along the way, before events */
  gathered: MaterialBag;
  events: ExpeditionEventOutcome[];
  /** final haul: gathered + gained - lost (never below zero, never empty) */
  loot: MaterialBag;
  explorerXp: number;
  playerXp: number;
}

const add = (bag: MaterialBag, code: MaterialCode, n: number) => {
  bag[code] = (bag[code] ?? 0) + n;
};
const entries = (bag: MaterialBag) => MATERIAL_CODES.filter((c) => (bag[c] ?? 0) > 0).map((c) => [c, bag[c]!] as const);

/**
 * Rolls a whole trip. Pure and deterministic: the same (seed, site, plan, explorerLevel) always gives the
 * same story, so the server can roll it at claim time and a repeated claim tells the same story.
 * A fixed number of random values is drawn per step so one roll never shifts the next one.
 */
export function rollExpedition(seed: number, site: ExpeditionSite, plan: ExpeditionPlan, explorerLevel: number): ExpeditionOutcome {
  const def = EXPEDITION_SITES[site];
  const planDef = EXPEDITION_PLANS[plan];
  const rng = seededRandom(`expedition:${seed}:${site}`);

  // gathering: the plan decides how many draws (a bold trip covers more ground), each draw is one unit
  const gathered: MaterialBag = {};
  const pool = entries(def.gather).map(([c]) => c);
  const rolls = Math.max(1, Math.round(def.gatherRolls * planDef.lootMultiplier));
  for (let i = 0; i < rolls; i++) add(gathered, pickWeighted(rng, pool, (c) => def.gather[c] ?? 0), 1);

  // events: drawn without repeats in a seeded order
  const codes = Object.keys(def.events);
  const order = codes.map((c) => ({ c, k: rng() })).sort((a, b) => a.k - b.k).map((x) => x.c);
  const loot: MaterialBag = { ...gathered };
  const events: ExpeditionEventOutcome[] = [];
  let explorerXp = def.explorerXp;
  for (const code of order.slice(0, def.eventCount)) {
    const ev = def.events[code]!;
    const roll = rng();
    const lossRoll = rng();
    const success = roll < expeditionEventChance(ev.chance, explorerLevel, plan);
    const outcome: ExpeditionEventOutcome = { event: code, success, gained: {}, lost: {} };
    if (success) {
      explorerXp += EXPEDITION_XP_PER_SUCCESS;
      for (const [c, n] of entries(ev.reward)) {
        add(outcome.gained, c, n);
        add(loot, c, n);
      }
    } else if (planDef.failureCosts && ev.loss > 0) {
      // lose `loss` units, taken from whatever is most plentiful (ties broken by the roll)
      for (let i = 0; i < ev.loss; i++) {
        const have = entries(loot);
        if (have.length === 0) break;
        const most = Math.max(...have.map(([, n]) => n));
        const top = have.filter(([, n]) => n === most);
        const [c] = top[Math.floor(lossRoll * top.length)]!;
        loot[c] = loot[c]! - 1;
        add(outcome.lost, c, 1);
      }
    }
    events.push(outcome);
  }

  // a trip always brings something home
  const final: MaterialBag = {};
  for (const [c, n] of entries(loot)) final[c] = n;
  if (entries(final).length === 0) final[pool[0]!] = 1;
  return { gathered, events, loot: final, explorerXp, playerXp: def.playerXp };
}

export function bagTotal(bag: MaterialBag): number {
  return entries(bag).reduce((a, [, n]) => a + n, 0);
}
