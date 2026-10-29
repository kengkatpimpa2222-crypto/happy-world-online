import {
  ARENA_BASES,
  ARENA_BOT_COOLDOWN_MS,
  ARENA_HITS_TO_SOAK,
  ARENA_RESPAWN_MS,
  ARENA_SPAWN_SHIELD_MS,
  ARENA_START_RATING,
  ARENA_THROW_COOLDOWN_MS,
  arenaCarryOver,
  arenaNextTierAt,
  arenaRatingDelta,
  arenaSeason,
  arenaSeasonEndsAt,
  arenaTier,
} from '@hwo/shared-contracts';
import { planMatches, QueuedPlayer } from './arena-matchmaking';
import { ArenaSim, seededRandom } from './arena-sim';

const T0 = 1_000_000;
const START = T0 + 3000;
const END = START + 120_000;

/** Walks a player to (x, y) in legal steps (8 units/s), returning the time after the walk. */
function walk(sim: ArenaSim, id: string, x: number, y: number, now: number): number {
  const f = sim.fighters.get(id)!;
  while (Math.hypot(x - f.x, y - f.y) > 0.01) {
    now += 200;
    const d = Math.hypot(x - f.x, y - f.y);
    const step = Math.min(d, 1.5);
    expect(sim.move(id, f.x + ((x - f.x) / d) * step, f.y + ((y - f.y) / d) * step, now)).toBe(true);
  }
  return now;
}

function duel() {
  return new ArenaSim(START, END, [
    { id: 'a', team: 'RED', isBot: false },
    { id: 'b', team: 'BLUE', isBot: false },
  ]);
}

describe('Splash Arena rules (ArenaSim)', () => {
  it('starts each team at its base, side by side', () => {
    const sim = new ArenaSim(START, END, [
      { id: 'r1', team: 'RED', isBot: false },
      { id: 'r2', team: 'RED', isBot: false },
      { id: 'r3', team: 'RED', isBot: false },
      { id: 'b1', team: 'BLUE', isBot: true },
    ]);
    const ys = ['r1', 'r2', 'r3'].map((id) => sim.fighters.get(id)!.y);
    expect(new Set(ys).size).toBe(3);
    expect(sim.fighters.get('r1')).toMatchObject(ARENA_BASES.RED);
    expect(sim.fighters.get('b1')).toMatchObject(ARENA_BASES.BLUE);
  });

  it('nothing moves or flies before the whistle', () => {
    const sim = duel();
    expect(sim.move('a', 4, 10, START - 1)).toBe(false);
    expect(sim.throw('a', 'b', START - 1)).toBe('NOT_STARTED');
  });

  it('refuses teleports and moves off the field', () => {
    const sim = duel();
    expect(sim.move('a', 20, 10, START + 200)).toBe(false);
    expect(sim.move('a', -1, 10, START + 2000)).toBe(false);
    expect(sim.move('a', 4.5, 10, START + 200)).toBe(true);
  });

  it('a balloon only reaches an enemy in range, and only after the cooldown', () => {
    const sim = duel();
    let now = START + ARENA_SPAWN_SHIELD_MS;
    expect(sim.throw('a', 'b', now)).toBe('OUT_OF_RANGE');
    now = walk(sim, 'a', 24, 10, now);
    const hit = sim.throw('a', 'b', now);
    expect(hit).toMatchObject({ hit: true, targetHits: 1, soaked: false });
    expect(sim.throw('a', 'b', now + ARENA_THROW_COOLDOWN_MS - 1)).toBe('COOLDOWN');
    expect(sim.throw('a', 'a', now + ARENA_THROW_COOLDOWN_MS)).toBe('BAD_TARGET'); // not at yourself / your team
  });

  it(`${ARENA_HITS_TO_SOAK} hits soak a player: a point for the thrower's team, back at base after the wait, shielded`, () => {
    const sim = duel();
    let now = walk(sim, 'a', 24, 10, START + ARENA_SPAWN_SHIELD_MS);
    let last;
    for (let i = 0; i < ARENA_HITS_TO_SOAK; i++) {
      last = sim.throw('a', 'b', now);
      now += ARENA_THROW_COOLDOWN_MS;
    }
    expect(last).toMatchObject({ soaked: true, targetHits: 0, score: { RED: 1, BLUE: 0 } });
    expect(sim.move('b', 28, 10, now)).toBe(false); // soaked players wait
    expect(sim.throw('b', 'a', now)).toBe('SOAKED');
    expect(sim.throw('a', 'b', now)).toBe('BAD_TARGET');

    const back = sim.tick(now + ARENA_RESPAWN_MS);
    expect(back.respawned).toEqual([expect.objectContaining({ id: 'b', ...ARENA_BASES.BLUE })]);
    // spawn shield: a hit right away does nothing (walk back into range first)
    const f = sim.fighters.get('a')!;
    expect(Math.hypot(f.x - ARENA_BASES.BLUE.x, f.y - ARENA_BASES.BLUE.y)).toBeLessThanOrEqual(6);
    expect(sim.throw('a', 'b', now + ARENA_RESPAWN_MS + 100)).toMatchObject({ hit: false });
  });

  it('the team with more soaks wins; equal is a draw; a team that walks out loses', () => {
    const sim = duel();
    expect(sim.outcome()).toEqual({ RED: 'DRAW', BLUE: 'DRAW' });
    sim.score.BLUE = 2;
    expect(sim.outcome()).toEqual({ RED: 'LOSS', BLUE: 'WIN' });
    expect(sim.isOver(START + 1)).toBe(false);
    sim.leave('b');
    expect(sim.isOver(START + 1)).toBe(true);
    expect(sim.outcome()).toEqual({ RED: 'WIN', BLUE: 'LOSS' });
    expect(sim.isOver(END)).toBe(true);
  });

  it('bots stop playing when every real player has left', () => {
    const sim = new ArenaSim(START, END, [
      { id: 'me', team: 'RED', isBot: false },
      { id: 'b1', team: 'RED', isBot: true },
      { id: 'b2', team: 'BLUE', isBot: true },
    ]);
    expect(sim.isOver(START)).toBe(false);
    sim.leave('me');
    expect(sim.isOver(START)).toBe(true);
  });

  it('bots walk into range and throw, slower than players and sometimes missing', () => {
    const sim = new ArenaSim(
      START,
      END,
      [
        { id: 'me', team: 'RED', isBot: false },
        { id: 'bot', team: 'BLUE', isBot: true },
      ],
      seededRandom(7),
    );
    let thrown = 0;
    let hits = 0;
    let firstThrowAt = 0;
    for (let now = START; now < START + 30_000; now += 200) {
      const ev = sim.tick(now);
      for (const t of ev.thrown) {
        thrown++;
        if (t.hit) hits++;
        firstThrowAt ||= now;
      }
    }
    expect(firstThrowAt).toBeGreaterThan(START);
    expect(thrown).toBeLessThanOrEqual(Math.ceil(30_000 / ARENA_BOT_COOLDOWN_MS));
    expect(hits).toBeGreaterThan(0);
    expect(hits).toBeLessThan(thrown);
    expect(sim.score.BLUE).toBeGreaterThan(0);
  });

  it('a seeded match replays exactly', () => {
    const run = () => {
      const sim = new ArenaSim(START, END, [
        { id: 'me', team: 'RED', isBot: false },
        { id: 'b1', team: 'RED', isBot: true },
        { id: 'b2', team: 'BLUE', isBot: true },
      ], seededRandom(42));
      for (let now = START; now < END; now += 200) sim.tick(now);
      return { ...sim.score };
    };
    const first = run();
    expect(first.RED + first.BLUE).toBeGreaterThan(0);
    expect(run()).toEqual(first);
  });
});

describe('Arena matchmaking', () => {
  const p = (userId: string, joinedAt: number, rating = 1000, ageBand = 'TEEN_13_17', blocked: string[] = []): QueuedPlayer => ({
    userId,
    ageBand,
    rating,
    joinedAt,
    blocked: new Set(blocked),
  });

  it('six players of one age band play at once, teams dealt in snake order by rating', () => {
    const q = [p('a', 1, 1600), p('b', 2, 1500), p('c', 3, 1400), p('d', 4, 1300), p('e', 5, 1200), p('f', 6, 1100)];
    const [m, ...rest] = planMatches(q, 10, 15_000);
    expect(rest).toEqual([]);
    expect(m!.teams.RED.map((x) => x.userId)).toEqual(['a', 'd', 'e']);
    expect(m!.teams.BLUE.map((x) => x.userId)).toEqual(['b', 'c', 'f']);
    expect(m!.bots).toEqual({ RED: 0, BLUE: 0 });
  });

  it('a small group waits, then plays with bots; two lone players face each other', () => {
    const q = [p('a', 0), p('b', 1000)];
    expect(planMatches(q, 5000, 15_000)).toEqual([]);
    const [m] = planMatches(q, 15_000, 15_000);
    expect(m!.teams.RED).toHaveLength(1);
    expect(m!.teams.BLUE).toHaveLength(1);
    expect(m!.bots).toEqual({ RED: 2, BLUE: 2 });
  });

  it('never mixes age bands or players who blocked each other', () => {
    const q = [p('kid', 0, 1000, 'UNDER_13'), p('adult', 0, 1000, 'ADULT_18_PLUS'), p('t1', 0, 1000, 'TEEN_13_17', ['t2']), p('t2', 0)];
    const matches = planMatches(q, 20_000, 15_000);
    expect(matches).toHaveLength(4);
    for (const m of matches) expect(m.teams.RED.length + m.teams.BLUE.length).toBe(1);
  });
});

describe('Arena ranks', () => {
  it('tiers by rating', () => {
    expect(arenaTier(0)).toBe('BRONZE');
    expect(arenaTier(ARENA_START_RATING)).toBe('BRONZE');
    expect(arenaTier(1100)).toBe('SILVER');
    expect(arenaTier(1699)).toBe('DIAMOND');
    expect(arenaTier(2400)).toBe('STAR');
    expect(arenaNextTierAt(1000)).toBe(1100);
    expect(arenaNextTierAt(1800)).toBeNull();
  });

  it('team Elo: beating a stronger team pays more; a win always counts; bots halve it', () => {
    expect(arenaRatingDelta(1000, 1000, 'WIN', false)).toBe(16);
    expect(arenaRatingDelta(1000, 1000, 'LOSS', false)).toBe(-16);
    expect(arenaRatingDelta(1000, 1000, 'DRAW', false)).toBe(0);
    expect(arenaRatingDelta(1000, 1400, 'WIN', false)).toBeGreaterThan(25);
    expect(arenaRatingDelta(2000, 1000, 'WIN', false)).toBe(1);
    expect(arenaRatingDelta(1000, 1000, 'WIN', true)).toBe(8);
  });

  it('seasons follow the Bangkok calendar month and reset softly', () => {
    // 2026-10-31 17:30 UTC is already 1 Nov 00:30 in Bangkok
    expect(arenaSeason(Date.UTC(2026, 9, 31, 17, 30))).toBe('2026-11');
    expect(arenaSeason(Date.UTC(2026, 9, 31, 16, 30))).toBe('2026-10');
    expect(new Date(arenaSeasonEndsAt(Date.UTC(2026, 9, 15))).toISOString()).toBe('2026-10-31T17:00:00.000Z');
    expect(arenaCarryOver(1600)).toBe(1300);
    expect(arenaCarryOver(800)).toBe(900);
  });
});
