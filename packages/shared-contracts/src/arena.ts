/**
 * Phase 13 — Splash Arena (ศึกลูกโป่งน้ำ): an opt-in team mode with a ranked ladder.
 *
 * Two teams (RED / BLUE) throw water balloons at each other for ARENA_MATCH_SECONDS. A player hit
 * ARENA_HITS_TO_SOAK times is "soaked" and walks back from their own base after a short wait; the team that
 * soaked the other side more often wins. Nobody in the open city can be attacked: the arena is a separate place
 * you only reach from the queue, matched with players of your own age band who have not blocked you.
 *
 * Everything that decides a match is checked on the server (positions, range, cooldowns, hits) and every player
 * has the same stats: nothing bought with money makes you stronger. Few players online is fine: after
 * ARENA_BOT_FILL_SECONDS in the queue, friendly bots fill the empty places (and the match moves rank half as far).
 *
 * Ranks: an Elo rating per monthly season (Bangkok calendar), shown as tiers Bronze → Star. A new season keeps
 * half of the distance from the starting rating so good players start near the top again without a grind.
 */
import { z } from 'zod';
import { BANGKOK_UTC_OFFSET_MS } from './city-time';

export const ARENA_TEAMS = ['RED', 'BLUE'] as const;
export type ArenaTeam = (typeof ARENA_TEAMS)[number];

/** Players per team (filled with bots when the queue is quiet). */
export const ARENA_TEAM_SIZE = 3;
/** Field size and bases (same units as the city: MAX_MOVE_SPEED per second). */
export const ARENA_WIDTH = 32;
export const ARENA_HEIGHT = 20;
export const ARENA_BASES: Record<ArenaTeam, { x: number; y: number }> = {
  RED: { x: 3, y: 10 },
  BLUE: { x: 29, y: 10 },
};
/** A balloon flies this far (server distance between thrower and target). */
export const ARENA_THROW_RANGE = 6;
/** Time between two throws of one player. */
export const ARENA_THROW_COOLDOWN_MS = 800;
/** Bots are a little slower to throw and sometimes miss, so a real player can always beat them. */
export const ARENA_BOT_COOLDOWN_MS = 1400;
export const ARENA_BOT_MISS_CHANCE = 0.35;
export const ARENA_BOT_SPEED = 5;
/** Hits until a player is soaked. */
export const ARENA_HITS_TO_SOAK = 4;
/** A soaked player sits out this long, then reappears at their base... */
export const ARENA_RESPAWN_MS = 3000;
/** ...and can't be hit for this long after reappearing (no spawn camping). */
export const ARENA_SPAWN_SHIELD_MS = 1500;

/** Default length of a match and queue wait before bots join (the server may shorten these in dev/test). */
export const ARENA_MATCH_SECONDS = 120;
export const ARENA_BOT_FILL_SECONDS = 15;
export const ARENA_COUNTDOWN_SECONDS = 3;

// ---------- ranks ----------

export const ARENA_START_RATING = 1000;
export const ARENA_ELO_K = 32;
/** Matches with any bot move rating by this share of the normal amount. */
export const ARENA_BOT_MATCH_FACTOR = 0.5;

export const ARENA_TIERS = ['BRONZE', 'SILVER', 'GOLD', 'PLATINUM', 'DIAMOND', 'STAR'] as const;
export type ArenaTier = (typeof ARENA_TIERS)[number];
/** Lowest rating of each tier. */
export const ARENA_TIER_FLOORS: Record<ArenaTier, number> = {
  BRONZE: 0,
  SILVER: 1100,
  GOLD: 1250,
  PLATINUM: 1400,
  DIAMOND: 1550,
  STAR: 1700,
};
export const ARENA_TIER_NAMES_TH: Record<ArenaTier, string> = {
  BRONZE: 'บรอนซ์',
  SILVER: 'ซิลเวอร์',
  GOLD: 'โกลด์',
  PLATINUM: 'แพลทินัม',
  DIAMOND: 'ไดมอนด์',
  STAR: 'ดาวเด่น',
};

export function arenaTier(rating: number): ArenaTier {
  let tier: ArenaTier = 'BRONZE';
  for (const t of ARENA_TIERS) if (rating >= ARENA_TIER_FLOORS[t]) tier = t;
  return tier;
}

/** Rating needed for the next tier, or null at the top. */
export function arenaNextTierAt(rating: number): number | null {
  const i = ARENA_TIERS.indexOf(arenaTier(rating));
  const next = ARENA_TIERS[i + 1];
  return next ? ARENA_TIER_FLOORS[next] : null;
}

export type ArenaOutcome = 'WIN' | 'DRAW' | 'LOSS';

/**
 * Team Elo: the rating change for every player of a team, from both teams' average ratings.
 * Bot matches move half as far; the result is never 0 for a win or a loss (a win always counts).
 */
export function arenaRatingDelta(teamAvg: number, opponentAvg: number, outcome: ArenaOutcome, withBots: boolean): number {
  const expected = 1 / (1 + 10 ** ((opponentAvg - teamAvg) / 400));
  const score = outcome === 'WIN' ? 1 : outcome === 'DRAW' ? 0.5 : 0;
  const raw = ARENA_ELO_K * (score - expected) * (withBots ? ARENA_BOT_MATCH_FACTOR : 1);
  const d = Math.round(raw);
  if (outcome === 'WIN') return Math.max(1, d);
  if (outcome === 'LOSS') return Math.min(-1, d);
  return d;
}

/** Season key = Bangkok calendar month, e.g. "2026-11". */
export function arenaSeason(atMs: number): string {
  return new Date(atMs + BANGKOK_UTC_OFFSET_MS).toISOString().slice(0, 7);
}

/** When the current season ends (start of next Bangkok month), as epoch ms. */
export function arenaSeasonEndsAt(atMs: number): number {
  const d = new Date(atMs + BANGKOK_UTC_OFFSET_MS);
  return Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + 1, 1) - BANGKOK_UTC_OFFSET_MS;
}

/** Soft reset: keep half the distance from the starting rating. */
export function arenaCarryOver(lastSeasonRating: number): number {
  return Math.round(ARENA_START_RATING + (lastSeasonRating - ARENA_START_RATING) / 2);
}

// ---------- rewards ----------

/** Gold for taking part, only for the first ARENA_DAILY_REWARDED_MATCHES matches of a Bangkok day. */
export const ARENA_REWARD_GOLD: Record<ArenaOutcome, number> = { WIN: 20, DRAW: 12, LOSS: 6 };
export const ARENA_DAILY_REWARDED_MATCHES = 10;

// ---------- realtime ----------

export const ArenaEvent = {
  // client -> server
  QUEUE: 'arena:queue',
  CANCEL: 'arena:cancel',
  MOVE: 'arena:move',
  THROW: 'arena:throw',
  /** give up and walk out (counts as a loss) */
  LEAVE: 'arena:leave',
  // server -> client
  /** you are in the queue: how many of your age band are waiting and when bots will join */
  QUEUED: 'arena:queued',
  /** the match is ready: teams, positions, when it starts and ends */
  START: 'arena:start',
  MOVED: 'arena:moved',
  /** a balloon was thrown: who at whom, whether it hit, the target's state and the score */
  THROWN: 'arena:thrown',
  RESPAWN: 'arena:respawn',
  /** a player left the match early */
  PLAYER_LEFT: 'arena:left',
  /** final score and everyone's new rating */
  END: 'arena:end',
} as const;

export const wsArenaMoveSchema = z.object({
  x: z.number().finite(),
  y: z.number().finite(),
  seq: z.number().int().min(0).optional(),
});
export const wsArenaThrowSchema = z.object({ targetId: z.string().min(1).max(64) });
export type WsArenaMove = z.infer<typeof wsArenaMoveSchema>;
export type WsArenaThrow = z.infer<typeof wsArenaThrowSchema>;

export const arenaLeaderboardQuerySchema = z.object({
  limit: z.coerce.number().int().min(1).max(100).default(50),
});
