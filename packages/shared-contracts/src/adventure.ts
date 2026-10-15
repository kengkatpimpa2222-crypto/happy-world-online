/**
 * Phase 9 — the materials loop: go exploring, bring back materials, craft them into goods for your shop.
 *
 * Exploring is the cozy RPG of the game. A trip takes a few real minutes (it keeps going while the app is
 * closed). On the way the player meets a few events (monkeys after the mangoes, a buffalo on the bridge,
 * a lost puppy); each one succeeds or not depending on the Explorer skill and the plan chosen at the start
 * (careful = safe but less loot, bold = more loot but a failed event costs some of it). The server rolls
 * everything from a seed it never sends, so the result can't be previewed, rerolled or faked.
 *
 * Crafting turns materials into goods that can't be bought with Gold (craft-only products in
 * BUSINESS_PRODUCTS). They sell for more than restocked goods, which is the reason to go exploring.
 */
import type { BusinessProductCode } from './game-config';

// ---- Life skills ----
export const SKILL_CODES = ['EXPLORER', 'CRAFTER'] as const;
export type SkillCode = (typeof SKILL_CODES)[number];
export const SKILL_MAX_LEVEL = 10;

/** Level 2 at 40 XP, 3 at 160, 4 at 360 ... (40 * (level - 1)^2). */
export function skillLevelForXp(xp: number): number {
  return Math.min(SKILL_MAX_LEVEL, Math.floor(Math.sqrt(Math.max(0, xp) / 40)) + 1);
}
export function skillXpForLevel(level: number): number {
  return 40 * (level - 1) * (level - 1);
}

// ---- Materials ----
/** `value` = Gold a unit is roughly worth (tuning reference only; materials are never sold for Gold). */
export const MATERIALS = {
  MANGO: { value: 4 },
  COCONUT: { value: 3 },
  STICKY_RICE: { value: 3 },
  JASMINE: { value: 2 },
  MARIGOLD: { value: 3 },
  WILD_HONEY: { value: 8 },
  LEMONGRASS: { value: 3 },
  HERB: { value: 4 },
  BEESWAX: { value: 6 },
} as const;
export type MaterialCode = keyof typeof MATERIALS;
export const MATERIAL_CODES = Object.keys(MATERIALS) as MaterialCode[];
export type MaterialBag = Partial<Record<MaterialCode, number>>;

/** Materials live in the normal player inventory as items `mat_<code>` (category MATERIAL). */
export function materialItemId(code: MaterialCode): string {
  return `mat_${code.toLowerCase()}`;
}
export function materialFromItemId(itemId: string): MaterialCode | null {
  if (!itemId.startsWith('mat_')) return null;
  const code = itemId.slice(4).toUpperCase();
  return code in MATERIALS ? (code as MaterialCode) : null;
}

// ---- Expedition sites, plans and events ----
export const EXPEDITION_PLAN_CODES = ['CAREFUL', 'STEADY', 'BOLD'] as const;
export type ExpeditionPlan = (typeof EXPEDITION_PLAN_CODES)[number];

export interface ExpeditionPlanDef {
  /** added to every event's chance of success */
  successBonus: number;
  /** multiplies how many materials are gathered along the way */
  lootMultiplier: number;
  /** a failed event costs its `loss` in materials (only when true) */
  failureCosts: boolean;
}
export const EXPEDITION_PLANS: Record<ExpeditionPlan, ExpeditionPlanDef> = {
  CAREFUL: { successBonus: 0.2, lootMultiplier: 0.7, failureCosts: false },
  STEADY: { successBonus: 0, lootMultiplier: 1, failureCosts: false },
  BOLD: { successBonus: -0.15, lootMultiplier: 1.5, failureCosts: true },
};

export interface ExpeditionEventDef {
  /** chance of success for a level-1 explorer with the STEADY plan */
  chance: number;
  /** extra materials when it goes well */
  reward: MaterialBag;
  /** materials lost when it goes badly on a BOLD trip */
  loss: number;
}

export const EXPEDITION_SITE_CODES = ['ORCHARD', 'FLOWER_FOREST', 'HERB_VALLEY'] as const;
export type ExpeditionSite = (typeof EXPEDITION_SITE_CODES)[number];

export interface ExpeditionSiteDef {
  /** Explorer level needed to go */
  explorerLevel: number;
  minutes: number;
  /** materials found along the way (weighted draws) */
  gatherRolls: number;
  gather: MaterialBag;
  /** how many events happen on the way (drawn from `events` without repeats) */
  eventCount: number;
  events: Record<string, ExpeditionEventDef>;
  explorerXp: number;
  playerXp: number;
  /**
   * New-player safety net (Phase 9.1): the first trip a player brings home from this site always carries
   * at least one full set of this recipe's inputs. Only what is missing is topped up, nothing is added on
   * top, and later trips roll exactly as before.
   */
  firstVisitRecipe: RecipeCode;
}

export const EXPEDITION_SITES: Record<ExpeditionSite, ExpeditionSiteDef> = {
  ORCHARD: {
    explorerLevel: 1,
    minutes: 3,
    gatherRolls: 5,
    gather: { MANGO: 3, COCONUT: 3, STICKY_RICE: 3 },
    eventCount: 2,
    events: {
      MONKEYS: { chance: 0.6, reward: { MANGO: 2 }, loss: 2 },
      RAIN_SHOWER: { chance: 0.7, reward: { COCONUT: 1 }, loss: 1 },
      LOST_PUPPY: { chance: 0.8, reward: { STICKY_RICE: 2 }, loss: 0 },
    },
    explorerXp: 10,
    playerXp: 5,
    firstVisitRecipe: 'MANGO_STICKY_RICE',
  },
  FLOWER_FOREST: {
    explorerLevel: 2,
    minutes: 6,
    gatherRolls: 7,
    gather: { JASMINE: 5, MARIGOLD: 3, WILD_HONEY: 1 },
    eventCount: 3,
    events: {
      WILD_BEES: { chance: 0.5, reward: { WILD_HONEY: 1 }, loss: 2 },
      FOG: { chance: 0.6, reward: { JASMINE: 3 }, loss: 2 },
      BUTTERFLIES: { chance: 0.75, reward: { MARIGOLD: 2 }, loss: 0 },
    },
    explorerXp: 18,
    playerXp: 9,
    firstVisitRecipe: 'JASMINE_GARLAND',
  },
  HERB_VALLEY: {
    explorerLevel: 4,
    minutes: 10,
    gatherRolls: 9,
    gather: { LEMONGRASS: 3, HERB: 3, BEESWAX: 2, WILD_HONEY: 1 },
    eventCount: 3,
    events: {
      BUFFALO_BRIDGE: { chance: 0.45, reward: { BEESWAX: 2, HERB: 2 }, loss: 3 },
      SLIPPERY_ROCKS: { chance: 0.6, reward: { LEMONGRASS: 2 }, loss: 2 },
      HERB_DOCTOR: { chance: 0.8, reward: { HERB: 2 }, loss: 0 },
      HIVE_IN_TREE: { chance: 0.5, reward: { BEESWAX: 1, WILD_HONEY: 1 }, loss: 2 },
    },
    explorerXp: 30,
    playerXp: 15,
    firstVisitRecipe: 'HERBAL_BALM',
  },
};

/** Each event a player handles well adds this to the trip's Explorer XP. */
export const EXPEDITION_XP_PER_SUCCESS = 3;
/** Each Explorer level above 1 adds this to every event's chance. */
export const EXPEDITION_CHANCE_PER_LEVEL = 0.06;
export const EXPEDITION_MIN_CHANCE = 0.1;
export const EXPEDITION_MAX_CHANCE = 0.95;

export function expeditionEventChance(base: number, explorerLevel: number, plan: ExpeditionPlan): number {
  const c = base + EXPEDITION_CHANCE_PER_LEVEL * (explorerLevel - 1) + EXPEDITION_PLANS[plan].successBonus;
  return Math.min(EXPEDITION_MAX_CHANCE, Math.max(EXPEDITION_MIN_CHANCE, c));
}

// ---- Crafting ----
export interface RecipeDef {
  productCode: BusinessProductCode;
  inputs: MaterialBag;
  /** units of the product one craft makes (before the Crafter bonus) */
  outputUnits: number;
  /** workshop fee in Gold per craft: the Gold sink of the loop and the goods' cost per unit */
  feeGold: number;
  crafterLevel: number;
  crafterXp: number;
}

export const RECIPE_CODES = ['MANGO_STICKY_RICE', 'JASMINE_GARLAND', 'HERBAL_BALM', 'HONEY_MANGO_JAM'] as const;
export type RecipeCode = (typeof RECIPE_CODES)[number];

export const RECIPES: Record<RecipeCode, RecipeDef> = {
  MANGO_STICKY_RICE: { productCode: 'MANGO_STICKY_RICE', inputs: { MANGO: 1, STICKY_RICE: 1, COCONUT: 1 }, outputUnits: 3, feeGold: 6, crafterLevel: 1, crafterXp: 5 },
  JASMINE_GARLAND: { productCode: 'JASMINE_GARLAND', inputs: { JASMINE: 3, MARIGOLD: 1 }, outputUnits: 2, feeGold: 4, crafterLevel: 1, crafterXp: 4 },
  HERBAL_BALM: { productCode: 'HERBAL_BALM', inputs: { HERB: 2, LEMONGRASS: 1, BEESWAX: 1 }, outputUnits: 2, feeGold: 8, crafterLevel: 2, crafterXp: 8 },
  // rare wild honey (forest, valley) + orchard mangoes: the priciest good, worth a trip to both places
  HONEY_MANGO_JAM: { productCode: 'HONEY_MANGO_JAM', inputs: { MANGO: 2, WILD_HONEY: 1 }, outputUnits: 2, feeGold: 6, crafterLevel: 2, crafterXp: 8 },
};
/** Most crafts of one recipe in a single request. */
export const CRAFT_MAX_TIMES = 10;

/** Crafter levels 4, 7 and 10 each add one unit per craft. */
export function craftOutputUnits(recipe: RecipeCode, crafterLevel: number): number {
  return RECIPES[recipe].outputUnits + Math.floor((crafterLevel - 1) / 3);
}
