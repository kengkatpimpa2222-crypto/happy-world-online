import { INestApplication } from '@nestjs/common';
import type { Socket } from 'socket.io-client';
import { ARENA_HITS_TO_SOAK, ARENA_THROW_COOLDOWN_MS } from '@hwo/shared-contracts';
import { PrismaService } from '../src/prisma/prisma.service';
import { connectWs, createTestApp, newGuest, TestPlayer, waitFor } from './helpers';

type Ack = { ok: boolean; error?: { code: string; reason?: string }; [k: string]: unknown };
interface StartMsg {
  matchId: string;
  you: { userId: string; team: 'RED' | 'BLUE' };
  withBots: boolean;
  players: { id: string; team: string; isBot: boolean; x: number; y: number; displayName: string }[];
}
interface EndMsg {
  matchId: string;
  score: { RED: number; BLUE: number };
  outcome: { RED: string; BLUE: string };
  saved: boolean;
  results: { userId: string; outcome: string; leftEarly: boolean; ratingBefore: number; ratingAfter: number; delta: number; rewardGold: number; tier: string }[];
}

// .env.test: matches last 8 s, bots join after 1 s, no countdown
describe('Splash Arena (opt-in team mode and ranks)', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];
  const sockets: Socket[] = [];

  beforeAll(async () => ({ app, prisma, http } = await createTestApp()));
  afterEach(() => {
    sockets.splice(0).forEach((s) => s.disconnect());
  });
  afterAll(async () => {
    await new Promise((r) => setTimeout(r, 300));
    await app.close();
  });

  async function online(p: TestPlayer): Promise<Socket> {
    const s = await connectWs(app, p.accessToken);
    sockets.push(s);
    return s;
  }
  const emit = (s: Socket, event: string, body?: unknown) => s.timeout(3000).emitWithAck(event, body) as Promise<Ack>;
  const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

  /** Walks towards (x, y) at a legal pace (7.5 units/s). */
  async function walk(s: Socket, from: { x: number; y: number }, to: { x: number; y: number }): Promise<void> {
    let { x, y } = from;
    while (Math.hypot(to.x - x, to.y - y) > 0.01) {
      const d = Math.hypot(to.x - x, to.y - y);
      const step = Math.min(d, 1.5);
      x += ((to.x - x) / d) * step;
      y += ((to.y - y) / d) * step;
      await sleep(200);
      expect(await emit(s, 'arena:move', { x, y })).toMatchObject({ ok: true });
    }
  }

  it('a fresh player starts the season at 1000 in Bronze and appears on the age-band leaderboard', async () => {
    const p = await newGuest(app);
    const me = await http().get('/api/v1/arena/me').set(p.auth).expect(200);
    expect(me.body).toMatchObject({ rating: 1000, peak: 1000, tier: 'BRONZE', nextTierAt: 1100, wins: 0, losses: 0, rewardedMatchesLeft: 10, recent: [] });
    expect(me.body.season).toMatch(/^\d{4}-\d{2}$/);
    const board = await http().get('/api/v1/arena/leaderboard?limit=100').set(p.auth).expect(200);
    expect(board.body.entries.find((e: { isMe: boolean }) => e.isMe)).toBeTruthy();

    const kid = await newGuest(app, 'UNDER_13');
    const kidBoard = await http().get('/api/v1/arena/leaderboard?limit=100').set(kid.auth).expect(200);
    expect(kidBoard.body.entries.map((e: { userId: string }) => e.userId)).not.toContain(p.userId);
  });

  it('nobody can throw outside a match: the city is never a battlefield', async () => {
    const p = await newGuest(app);
    const s = await online(p);
    await emit(s, 'zone:join', { zone: 'PLAZA' });
    expect((await emit(s, 'arena:throw', { targetId: p.userId })).error?.code).toBe('ARENA_NOT_IN_MATCH');
    expect((await emit(s, 'arena:move', { x: 1, y: 1 })).error?.code).toBe('ARENA_NOT_IN_MATCH');
  });

  it('six players: red soaks a blue player, blue walks out, red wins rank and Gold, leavers lose and earn nothing', async () => {
    const players = await Promise.all(Array.from({ length: 6 }, () => newGuest(app, 'ADULT_18_PLUS')));
    const socks = await Promise.all(players.map(online));
    const starts = socks.map((s) => waitFor<StartMsg>(s, 'arena:start', () => true, 4000));
    const queued = await Promise.all(socks.map((s) => emit(s, 'arena:queue')));
    queued.forEach((q) => expect(q.ok).toBe(true));
    const msgs = await Promise.all(starts);

    expect(new Set(msgs.map((m) => m.matchId)).size).toBe(1);
    expect(msgs[0]!.withBots).toBe(false);
    expect(msgs[0]!.players.filter((p) => p.team === 'RED')).toHaveLength(3);
    const red = msgs.findIndex((m) => m.you.team === 'RED');
    const blue = msgs.findIndex((m) => m.you.team === 'BLUE');
    const redId = msgs[red]!.you.userId;
    const blueId = msgs[blue]!.you.userId;
    const redPos = msgs[red]!.players.find((p) => p.id === redId)!;
    const bluePos = msgs[blue]!.players.find((p) => p.id === blueId)!;

    // others see the walk
    const seen = waitFor<{ id: string }>(socks[blue]!, 'arena:moved', (m) => m.id === redId);
    await walk(socks[red]!, redPos, { x: bluePos.x - 5, y: bluePos.y });
    await seen;

    const soaked = waitFor<{ target: string; soaked: boolean; score: { RED: number } }>(socks[blue]!, 'arena:thrown', (t) => t.soaked);
    for (let i = 0; i < ARENA_HITS_TO_SOAK; i++) {
      const r = await emit(socks[red]!, 'arena:throw', { targetId: blueId });
      expect(r).toMatchObject({ ok: true, hit: true });
      if (i === 0) expect((await emit(socks[red]!, 'arena:throw', { targetId: blueId })).error).toMatchObject({ code: 'ARENA_THROW_REFUSED', reason: 'COOLDOWN' });
      await sleep(ARENA_THROW_COOLDOWN_MS + 30);
    }
    expect(await soaked).toMatchObject({ target: blueId, soaked: true, score: { RED: 1 } });
    // a soaked player sits out
    expect((await emit(socks[blue]!, 'arena:throw', { targetId: redId })).error?.reason).toBe('SOAKED');

    // the whole blue team walks out: the match ends at once
    const ended = waitFor<EndMsg>(socks[red]!, 'arena:end', () => true, 4000);
    for (let i = 0; i < 6; i++) if (msgs[i]!.you.team === 'BLUE') expect(await emit(socks[i]!, 'arena:leave')).toMatchObject({ ok: true, left: true });
    const end = await ended;
    expect(end).toMatchObject({ saved: true, outcome: { RED: 'WIN', BLUE: 'LOSS' }, score: { RED: 1, BLUE: 0 } });
    const winner = end.results.find((r) => r.userId === redId)!;
    const loser = end.results.find((r) => r.userId === blueId)!;
    expect(winner).toMatchObject({ outcome: 'WIN', ratingBefore: 1000, ratingAfter: 1016, rewardGold: 20 });
    expect(loser).toMatchObject({ outcome: 'LOSS', leftEarly: true, ratingAfter: 984, rewardGold: 0 });

    const me = await http().get('/api/v1/arena/me').set(players[red]!.auth).expect(200);
    expect(me.body).toMatchObject({ rating: 1016, wins: 1, rewardedMatchesLeft: 9 });
    expect(me.body.recent[0]).toMatchObject({ outcome: 'WIN', delta: 16, rewardGold: 20, soaks: 1 });
    const ledger = await prisma.walletTransaction.findMany({ where: { userId: redId, type: 'ARENA_REWARD' } });
    expect(ledger.map((l) => l.amount)).toEqual([20]);
    expect(await prisma.walletTransaction.count({ where: { userId: blueId, type: 'ARENA_REWARD' } })).toBe(0);

    // after the match everyone can queue again
    expect(await emit(socks[red]!, 'arena:queue')).toMatchObject({ ok: true });
    expect(await emit(socks[red]!, 'arena:cancel')).toMatchObject({ ok: true, removed: true });
  }, 30_000);

  it('a lone player gets friendly bots and disappears from the city while playing; bot matches move rank half as far', async () => {
    const me = await newGuest(app, 'UNDER_13');
    const friend = await newGuest(app, 'UNDER_13');
    const s = await online(me);
    const watcher = await online(friend);
    await emit(s, 'zone:join', { zone: 'PLAZA' });
    await emit(watcher, 'zone:join', { zone: 'PLAZA' });

    const vanished = waitFor(watcher, 'player:left', (p: { userId: string }) => p.userId === me.userId, 4000);
    const start = waitFor<StartMsg>(s, 'arena:start', () => true, 4000);
    const q = await emit(s, 'arena:queue');
    expect(q).toMatchObject({ ok: true, waiting: 1 });
    const m = await start;
    await vanished;
    expect(m.withBots).toBe(true);
    expect(m.players).toHaveLength(6);
    expect(m.players.filter((p) => p.isBot)).toHaveLength(5);
    expect((await emit(s, 'arena:queue')).error?.code).toBe('ARENA_IN_MATCH');

    // bots play on their own: within a few seconds someone throws
    await waitFor(s, 'arena:thrown', () => true, 5000);

    const end = waitFor<EndMsg>(s, 'arena:end', () => true, 4000);
    await emit(s, 'arena:leave');
    const result = (await end).results[0]!;
    expect(result).toMatchObject({ userId: me.userId, outcome: 'LOSS', leftEarly: true, delta: -8, rewardGold: 0 });
  }, 20_000);

  it('players who blocked each other are never put in the same match', async () => {
    const a = await newGuest(app);
    const b = await newGuest(app);
    await http().post(`/api/v1/players/${b.userId}/block`).set(a.auth).expect(200);
    const sa = await online(a);
    const sb = await online(b);
    const starts = [waitFor<StartMsg>(sa, 'arena:start', () => true, 4000), waitFor<StartMsg>(sb, 'arena:start', () => true, 4000)];
    await Promise.all([emit(sa, 'arena:queue'), emit(sb, 'arena:queue')]);
    const [ma, mb] = await Promise.all(starts);
    expect(ma!.matchId).not.toBe(mb!.matchId);
    expect(ma!.players.map((p) => p.id)).not.toContain(b.userId);
    await Promise.all([emit(sa, 'arena:leave'), emit(sb, 'arena:leave')]);
  }, 15_000);

  it('a full match runs to the final whistle and pays every player who stayed', async () => {
    const p = await newGuest(app);
    const s = await online(p);
    const start = waitFor<StartMsg>(s, 'arena:start', () => true, 4000);
    await emit(s, 'arena:queue');
    const m = await start;
    const end = await waitFor<EndMsg>(s, 'arena:end', () => true, 12_000);
    expect(end.matchId).toBe(m.matchId);
    const r = end.results[0]!;
    expect(r.leftEarly).toBe(false);
    expect(r.rewardGold).toBe(({ WIN: 20, DRAW: 12, LOSS: 6 } as Record<string, number>)[r.outcome]);
    expect(Math.abs(r.delta)).toBeLessThanOrEqual(8);
  }, 20_000);
});
