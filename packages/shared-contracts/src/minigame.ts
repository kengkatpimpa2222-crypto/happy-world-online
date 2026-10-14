/**
 * Mini-games: make goods by hand for your own stall (30-60 s rounds).
 *
 * Instead of buying stock with Gold, a player can make it: catch lemons for lemonade, grill meatballs,
 * pour Thai tea. Stars from the round decide how many units go into the shop's storage, free of charge
 * (cost 0, so the shop's average cost per unit drops: the lesson is that your own time is also a resource).
 *
 * The server owns the outcome: it hands out the round's seed, checks the round really lasted its length,
 * caps the score, computes stars and adds the goods. The client only plays the round (game logic in the
 * Unity client's Core/Minigames, tuned by bots in MinigameTests; keep the numbers below in step with it).
 */
import { BANGKOK_UTC_OFFSET_MS } from './city-time';
import type { BusinessProductCode } from './game-config';

export const MINIGAME_CODES = ['CATCH', 'GRILL', 'POUR'] as const;
export type MinigameCode = (typeof MINIGAME_CODES)[number];

export interface MinigameDef {
  durationSeconds: number;
  /** score needed for 1, 2, 3 stars */
  starScores: readonly [number, number, number];
  /** no honest round can score more (theoretical bound of the rules) */
  maxScore: number;
  products: readonly BusinessProductCode[];
}

export const MINIGAMES: Record<MinigameCode, MinigameDef> = {
  CATCH: { durationSeconds: 45, starScores: [250, 500, 750], maxScore: 2800, products: ['LEMONADE', 'FLOWER'] },
  GRILL: { durationSeconds: 60, starScores: [500, 1100, 2000], maxScore: 6000, products: ['MEATBALL', 'BREAD'] },
  POUR: { durationSeconds: 40, starScores: [450, 1100, 1700], maxScore: 3000, products: ['THAI_TEA'] },
};

/** Units of the product made, by stars (0-3). Even a 0-star try makes one. */
export const MINIGAME_UNITS_BY_STARS = [1, 4, 7, 10] as const;
export const MINIGAME_XP_BY_STARS = [1, 3, 5, 8] as const;
/**
 * Rounds per Bangkok day that make goods. Later rounds still count for stars and best scores (play for fun),
 * so there is never a reason to grind and nothing is lost by skipping a day.
 */
export const MINIGAME_DAILY_REWARDED_PLAYS = 15;
/** A round may be reported this many seconds before its nominal end (frame timing, network). */
export const MINIGAME_EARLY_TOLERANCE_SECONDS = 3;
/** A round left open longer than its length plus this is void (the app was closed mid-round). */
export const MINIGAME_SESSION_TTL_SECONDS = 600;

export function minigameForProduct(code: BusinessProductCode): MinigameCode {
  for (const g of MINIGAME_CODES) if (MINIGAMES[g].products.includes(code)) return g;
  throw new Error(`No mini-game makes ${code}`);
}

export function minigameStars(game: MinigameCode, score: number): 0 | 1 | 2 | 3 {
  const [a, b, c] = MINIGAMES[game].starScores;
  return score >= c ? 3 : score >= b ? 2 : score >= a ? 1 : 0;
}

/** Bangkok calendar day "YYYY-MM-DD" (the daily limit resets at midnight Bangkok time). */
export function bangkokDay(atMs: number): string {
  return new Date(atMs + BANGKOK_UTC_OFFSET_MS).toISOString().slice(0, 10);
}

/** Next Bangkok midnight after `atMs`, as epoch ms. */
export function nextBangkokMidnight(atMs: number): number {
  const day = 24 * 3600_000;
  return Math.floor((atMs + BANGKOK_UTC_OFFSET_MS) / day) * day + day - BANGKOK_UTC_OFFSET_MS;
}
