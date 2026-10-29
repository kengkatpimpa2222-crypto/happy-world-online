import {
  ARENA_BASES,
  ARENA_BOT_COOLDOWN_MS,
  ARENA_BOT_MISS_CHANCE,
  ARENA_BOT_SPEED,
  ARENA_HEIGHT,
  ARENA_HITS_TO_SOAK,
  ARENA_RESPAWN_MS,
  ARENA_SPAWN_SHIELD_MS,
  ARENA_THROW_COOLDOWN_MS,
  ARENA_THROW_RANGE,
  ARENA_WIDTH,
  ArenaOutcome,
  ArenaTeam,
  MAX_MOVE_SPEED,
} from '@hwo/shared-contracts';

export interface ArenaFighter {
  id: string;
  team: ArenaTeam;
  isBot: boolean;
  x: number;
  y: number;
  /** hits taken since the last time this player was soaked */
  hits: number;
  /** while soaked: when the player comes back at their base */
  respawnAt: number | null;
  shieldUntil: number;
  lastThrowAt: number;
  lastMoveAt: number;
  /** left the match (disconnect / gave up): off the field for good */
  left: boolean;
  soaks: number;
  soakedTimes: number;
  throws: number;
  balloonsHit: number;
}

export type ThrowRefusal = 'NOT_STARTED' | 'OVER' | 'SOAKED' | 'COOLDOWN' | 'BAD_TARGET' | 'OUT_OF_RANGE';

export interface ThrowResult {
  by: string;
  target: string;
  hit: boolean;
  /** hits on the target after this throw (0 again once soaked) */
  targetHits: number;
  soaked: boolean;
  respawnAt: number | null;
  score: Record<ArenaTeam, number>;
}

export interface SimEvents {
  moved: { id: string; x: number; y: number; seq?: number }[];
  thrown: ThrowResult[];
  respawned: { id: string; x: number; y: number; shieldUntil: number }[];
}

/** Small deterministic PRNG (mulberry32) so a match with bots replays the same way in tests. */
export function seededRandom(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const dist = (a: { x: number; y: number }, b: { x: number; y: number }) => Math.hypot(a.x - b.x, a.y - b.y);
const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));

/**
 * One Splash Arena match, without any I/O: the rules the server enforces. Time is always passed in
 * (`now`, epoch ms) so tests can play a whole match instantly.
 */
export class ArenaSim {
  readonly fighters = new Map<string, ArenaFighter>();
  readonly score: Record<ArenaTeam, number> = { RED: 0, BLUE: 0 };

  constructor(
    readonly startsAt: number,
    readonly endsAt: number,
    roster: { id: string; team: ArenaTeam; isBot: boolean }[],
    private readonly random: () => number = Math.random,
  ) {
    const slot: Record<ArenaTeam, number> = { RED: 0, BLUE: 0 };
    for (const r of roster) {
      const base = ARENA_BASES[r.team];
      // stand side by side around the base: 0, +2, -2, +4 ...
      const i = slot[r.team]++;
      const offset = i === 0 ? 0 : Math.ceil(i / 2) * 2 * (i % 2 ? 1 : -1);
      this.fighters.set(r.id, {
        id: r.id,
        team: r.team,
        isBot: r.isBot,
        x: base.x,
        y: clamp(base.y + offset, 0, ARENA_HEIGHT),
        hits: 0,
        respawnAt: null,
        shieldUntil: startsAt + ARENA_SPAWN_SHIELD_MS,
        lastThrowAt: 0,
        lastMoveAt: startsAt,
        left: false,
        soaks: 0,
        soakedTimes: 0,
        throws: 0,
        balloonsHit: 0,
      });
    }
  }

  isOver(now: number): boolean {
    if (now >= this.endsAt) return true;
    const stay = [...this.fighters.values()].filter((f) => !f.left);
    // a team with nobody left forfeits; bots don't play on alone
    return (['RED', 'BLUE'] as const).some((t) => !stay.some((f) => f.team === t)) || !stay.some((f) => !f.isBot);
  }

  /** Server check of a player's move: inside the field, not soaked, not faster than walking (with jitter room). */
  move(id: string, x: number, y: number, now: number): boolean {
    const f = this.fighters.get(id);
    if (!f || f.left || f.respawnAt !== null || now < this.startsAt || this.isOver(now)) return false;
    if (x < 0 || y < 0 || x > ARENA_WIDTH || y > ARENA_HEIGHT) return false;
    const dt = Math.max(50, now - f.lastMoveAt) / 1000;
    if (dist(f, { x, y }) > MAX_MOVE_SPEED * dt * 1.25 + 0.5) return false;
    f.x = x;
    f.y = y;
    f.lastMoveAt = now;
    return true;
  }

  /** A throw from `id` at `targetId`. Players' throws in range always hit; bots sometimes miss. */
  throw(id: string, targetId: string, now: number): ThrowResult | ThrowRefusal {
    if (now < this.startsAt) return 'NOT_STARTED';
    if (this.isOver(now)) return 'OVER';
    const f = this.fighters.get(id);
    const t = this.fighters.get(targetId);
    if (!f || f.left) return 'BAD_TARGET';
    if (f.respawnAt !== null) return 'SOAKED';
    if (now - f.lastThrowAt < (f.isBot ? ARENA_BOT_COOLDOWN_MS : ARENA_THROW_COOLDOWN_MS)) return 'COOLDOWN';
    if (!t || t.left || t.team === f.team || t.respawnAt !== null) return 'BAD_TARGET';
    if (dist(f, t) > ARENA_THROW_RANGE) return 'OUT_OF_RANGE';

    f.lastThrowAt = now;
    f.throws++;
    const shielded = now < t.shieldUntil;
    const hit = !shielded && !(f.isBot && this.random() < ARENA_BOT_MISS_CHANCE);
    let soaked = false;
    if (hit) {
      f.balloonsHit++;
      t.hits++;
      if (t.hits >= ARENA_HITS_TO_SOAK) {
        soaked = true;
        t.hits = 0;
        t.respawnAt = now + ARENA_RESPAWN_MS;
        t.soakedTimes++;
        f.soaks++;
        this.score[f.team]++;
      }
    }
    return { by: id, target: targetId, hit, targetHits: t.hits, soaked, respawnAt: t.respawnAt, score: { ...this.score } };
  }

  leave(id: string): boolean {
    const f = this.fighters.get(id);
    if (!f || f.left) return false;
    f.left = true;
    return true;
  }

  /** Advances time: soaked players come back, bots walk and throw. */
  tick(now: number): SimEvents {
    const ev: SimEvents = { moved: [], thrown: [], respawned: [] };
    if (now < this.startsAt || this.isOver(now)) return ev;
    for (const f of this.fighters.values()) {
      if (f.left || f.respawnAt === null || now < f.respawnAt) continue;
      const base = ARENA_BASES[f.team];
      f.respawnAt = null;
      f.x = base.x;
      f.y = base.y;
      f.lastMoveAt = now;
      f.shieldUntil = now + ARENA_SPAWN_SHIELD_MS;
      ev.respawned.push({ id: f.id, x: f.x, y: f.y, shieldUntil: f.shieldUntil });
    }
    for (const bot of this.fighters.values()) {
      if (!bot.isBot || bot.left || bot.respawnAt !== null) continue;
      const target = this.nearestEnemy(bot, now);
      if (!target) continue;
      const d = dist(bot, target);
      if (d > ARENA_THROW_RANGE - 1) {
        // walk towards the target, stopping just inside throwing range
        const step = Math.min(d - (ARENA_THROW_RANGE - 1.5), (ARENA_BOT_SPEED * Math.max(0, now - bot.lastMoveAt)) / 1000);
        if (step > 0.05) {
          bot.x = clamp(bot.x + ((target.x - bot.x) / d) * step, 0, ARENA_WIDTH);
          bot.y = clamp(bot.y + ((target.y - bot.y) / d) * step, 0, ARENA_HEIGHT);
          ev.moved.push({ id: bot.id, x: round2(bot.x), y: round2(bot.y) });
        }
      }
      bot.lastMoveAt = now;
      const r = this.throw(bot.id, target.id, now);
      if (typeof r !== 'string') ev.thrown.push(r);
    }
    return ev;
  }

  /** Who the bot goes for: the closest enemy still on the field and not shielded (or the closest at all). */
  private nearestEnemy(bot: ArenaFighter, now: number): ArenaFighter | null {
    let best: ArenaFighter | null = null;
    let bestD = Infinity;
    for (const f of this.fighters.values()) {
      if (f.team === bot.team || f.left || f.respawnAt !== null) continue;
      const d = dist(bot, f) + (now < f.shieldUntil ? 100 : 0);
      if (d < bestD) {
        best = f;
        bestD = d;
      }
    }
    return best;
  }

  /** Final result of each team; a team that walked out entirely loses whatever the score. */
  outcome(): Record<ArenaTeam, ArenaOutcome> {
    const present = (t: ArenaTeam) => [...this.fighters.values()].some((f) => f.team === t && !f.left);
    const red = present('RED');
    const blue = present('BLUE');
    if (red !== blue) return red ? { RED: 'WIN', BLUE: 'LOSS' } : { RED: 'LOSS', BLUE: 'WIN' };
    if (this.score.RED === this.score.BLUE) return { RED: 'DRAW', BLUE: 'DRAW' };
    return this.score.RED > this.score.BLUE ? { RED: 'WIN', BLUE: 'LOSS' } : { RED: 'LOSS', BLUE: 'WIN' };
  }

  publicFighter(f: ArenaFighter) {
    return { id: f.id, team: f.team, isBot: f.isBot, x: round2(f.x), y: round2(f.y), hits: f.hits, soaked: f.respawnAt !== null };
  }
}

function round2(v: number): number {
  return Math.round(v * 100) / 100;
}
