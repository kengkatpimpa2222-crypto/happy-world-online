import { INestApplication } from '@nestjs/common';
import type { Socket } from 'socket.io-client';
import { connectWs, createTestApp, newGuest, receivedWithin, TestPlayer, waitFor } from './helpers';

type Ack = { ok: boolean; error?: { code: string }; [k: string]: unknown };

describe('Realtime city (WebSocket)', () => {
  let app: INestApplication;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];
  const sockets: Socket[] = [];

  beforeAll(async () => ({ app, http } = await createTestApp()));
  afterEach(() => {
    sockets.splice(0).forEach((s) => s.disconnect());
  });
  afterAll(async () => {
    await new Promise((r) => setTimeout(r, 200)); // let disconnect handlers finish
    await app.close();
  });

  async function online(p: TestPlayer): Promise<Socket> {
    const s = await connectWs(app, p.accessToken);
    sockets.push(s);
    return s;
  }
  const emit = (s: Socket, event: string, body?: unknown) => s.timeout(3000).emitWithAck(event, body) as Promise<Ack>;
  const befriend = async (a: TestPlayer, b: TestPlayer) => {
    const r = await http().post('/api/v1/friends/requests').set(a.auth).send({ playerId: b.userId }).expect(201);
    await http().post(`/api/v1/friends/requests/${r.body.requestId}/accept`).set(b.auth).expect(200);
  };

  it('rejects connections without a valid token', async () => {
    await expect(connectWs(app, undefined)).rejects.toThrow();
    await expect(connectWs(app, 'not-a-token')).rejects.toThrow();
    const p = await newGuest(app);
    await http().post('/api/v1/auth/logout').set(p.auth).expect(204);
    await expect(connectWs(app, p.accessToken)).rejects.toThrow(); // revoked session
  });

  it('exposes the city layout over REST', async () => {
    const p = await newGuest(app);
    const res = await http().get('/api/v1/city').set(p.auth).expect(200);
    expect(res.body.zones.map((z: { id: string }) => z.id)).toEqual(['PLAZA', 'HOMES', 'MARKET', 'ACTIVITY', 'MINIGAME_GATE']);
    expect(res.body.realtime).toMatchObject({ namespace: '/realtime', path: '/ws' });
    expect(res.body.presetMessages.length).toBeGreaterThan(5);
  });

  it('players in the same zone see each other join, move and leave', async () => {
    const a = await newGuest(app);
    const b = await newGuest(app);
    const sa = await online(a);
    const sb = await online(b);

    const ja = await emit(sa, 'zone:join', { zone: 'PLAZA' });
    expect(ja).toMatchObject({ ok: true, zone: 'PLAZA', x: 30, y: 30 });

    const joined = waitFor(sa, 'player:joined', (p: { userId: string }) => p.userId === b.userId);
    const jb = await emit(sb, 'zone:join', { zone: 'PLAZA' });
    expect((jb.players as { userId: string }[]).map((p) => p.userId)).toContain(a.userId);
    await joined;

    const moved = waitFor(sb, 'player:moved', (p: { userId: string }) => p.userId === a.userId);
    await new Promise((r) => setTimeout(r, 200));
    expect(await emit(sa, 'move', { x: 31, y: 30.5, dir: 2, seq: 1 })).toMatchObject({ ok: true });
    expect(await moved).toMatchObject({ x: 31, y: 30.5, dir: 2 });

    const rest = await http().get('/api/v1/city/players?zone=PLAZA').set(b.auth).expect(200);
    expect(rest.body.players).toEqual([expect.objectContaining({ userId: a.userId, x: 31, y: 30.5, isFriend: false })]);

    const left = waitFor(sb, 'player:left', (p: { userId: string }) => p.userId === a.userId);
    sa.disconnect();
    await left;
  });

  it('the server rejects teleporting, out-of-bounds moves and moves outside a zone', async () => {
    const a = await newGuest(app);
    const sa = await online(a);
    expect((await emit(sa, 'move', { x: 1, y: 1 })).error?.code).toBe('NOT_IN_ZONE');
    await emit(sa, 'zone:join', { zone: 'PLAZA' });
    const corrected = waitFor(sa, 'move:corrected');
    expect((await emit(sa, 'move', { x: 59, y: 59, seq: 7 })).error?.code).toBe('MOVE_REJECTED');
    expect(await corrected).toMatchObject({ x: 30, y: 30, seq: 7 });
    await new Promise((r) => setTimeout(r, 500));
    expect((await emit(sa, 'move', { x: -1, y: 30 })).error?.code).toBe('MOVE_REJECTED');
    expect((await emit(sa, 'move', { x: 'a', y: 30 })).error?.code).toBe('VALIDATION_FAILED');
    expect((await emit(sa, 'zone:join', { zone: 'SECRET_ADMIN_ROOM' })).error?.code).toBe('VALIDATION_FAILED');
  });

  it('preset chat only: valid presets are delivered, anything else is rejected and rate limited', async () => {
    const a = await newGuest(app);
    const b = await newGuest(app);
    const sa = await online(a);
    const sb = await online(b);
    await emit(sa, 'zone:join', { zone: 'MARKET' });
    await emit(sb, 'zone:join', { zone: 'MARKET' });

    const got = waitFor(sb, 'chat:preset', (p: { userId: string }) => p.userId === a.userId);
    expect(await emit(sa, 'chat:preset', { presetId: 'HELLO' })).toMatchObject({ ok: true });
    expect(await got).toMatchObject({ presetId: 'HELLO' });
    expect((await emit(sa, 'chat:preset', { presetId: 'call me 0812345678' })).error?.code).toBe('VALIDATION_FAILED');
    expect((await emit(sa, 'chat:preset', { presetId: 'HELLO', text: 'free text' })).error?.code).toBe('VALIDATION_FAILED');
    // burst of 3 used up: the next one is refused even though it is valid
    expect((await emit(sa, 'chat:preset', { presetId: 'BYE' })).error?.code).toBe('RATE_LIMITED');
  });

  it('different age bands never share a room', async () => {
    const adult = await newGuest(app, 'ADULT_18_PLUS');
    const kid = await newGuest(app, 'UNDER_13');
    const sAdult = await online(adult);
    const sKid = await online(kid);
    await emit(sKid, 'zone:join', { zone: 'ACTIVITY' });
    const sawKid = receivedWithin(sAdult, 'player:joined');
    const res = await emit(sAdult, 'zone:join', { zone: 'ACTIVITY' });
    expect((res.players as { userId: string }[]).map((p) => p.userId)).not.toContain(kid.userId);
    expect(res.room).not.toBe((await emit(sKid, 'zone:join', { zone: 'ACTIVITY' })).room);
    expect(await sawKid).toBe(false);
    const rest = await http().get('/api/v1/city/players?zone=ACTIVITY').set(adult.auth).expect(200);
    expect(rest.body.players.map((p: { userId: string }) => p.userId)).not.toContain(kid.userId);
  });

  it('blocking hides players from each other immediately, both ways', async () => {
    const a = await newGuest(app);
    const b = await newGuest(app);
    const sa = await online(a);
    const sb = await online(b);
    await emit(sa, 'zone:join', { zone: 'HOMES' });
    await emit(sb, 'zone:join', { zone: 'HOMES' });

    const vanished = waitFor(sb, 'player:left', (p: { userId: string }) => p.userId === a.userId);
    await http().post(`/api/v1/players/${b.userId}/block`).set(a.auth).expect(200);
    await vanished;

    const bHearsA = receivedWithin(sb, 'chat:preset');
    const aHearsB = receivedWithin(sa, 'chat:preset');
    await emit(sa, 'chat:preset', { presetId: 'HELLO' });
    await emit(sb, 'chat:preset', { presetId: 'HELLO' });
    expect(await bHearsA).toBe(false);
    expect(await aHearsB).toBe(false);

    const rest = await http().get('/api/v1/city/players?zone=HOMES').set(b.auth).expect(200);
    expect(rest.body.players.map((p: { userId: string }) => p.userId)).not.toContain(a.userId);

    // re-joining also hides the blocker
    const rejoin = await emit(sb, 'zone:join', { zone: 'HOMES' });
    expect((rejoin.players as { userId: string }[]).map((p) => p.userId)).not.toContain(a.userId);
  });

  it('friends get online/offline notices, can be invited, and show as online', async () => {
    const a = await newGuest(app);
    const b = await newGuest(app);
    const stranger = await newGuest(app);
    await befriend(a, b);

    const sb = await online(b);
    const cameOnline = waitFor(sb, 'friend:online', (p: { userId: string }) => p.userId === a.userId);
    const sa = await online(a);
    await cameOnline;

    const list = await http().get('/api/v1/friends').set(b.auth).expect(200);
    expect(list.body[0]).toMatchObject({ userId: a.userId, online: true });

    await emit(sa, 'zone:join', { zone: 'PLAZA' });
    const invited = waitFor(sb, 'friend:invited');
    expect(await emit(sa, 'friend:invite', { friendId: b.userId, activity: 'MINIGAME_DELIVERY' })).toMatchObject({ ok: true });
    expect(await invited).toMatchObject({ fromUserId: a.userId, activity: 'MINIGAME_DELIVERY', zone: 'PLAZA' });

    expect((await emit(sa, 'friend:invite', { friendId: stranger.userId, activity: 'JOIN_ZONE' })).error?.code).toBe('NOT_FRIENDS');

    const wentOffline = waitFor(sb, 'friend:offline', (p: { userId: string }) => p.userId === a.userId);
    sa.disconnect();
    await wentOffline;
    expect((await http().get('/api/v1/friends').set(b.auth)).body[0].online).toBe(false);
  });

  it('friend requests arrive in realtime', async () => {
    const a = await newGuest(app);
    const b = await newGuest(app);
    const sb = await online(b);
    const incoming = waitFor(sb, 'friend:request');
    await http().post('/api/v1/friends/requests').set(a.auth).send({ playerId: b.userId }).expect(201);
    expect(await incoming).toMatchObject({ from: { userId: a.userId } });
  });

  it('a second connection replaces the first', async () => {
    const a = await newGuest(app);
    const first = await online(a);
    const replaced = waitFor(first, 'session:replaced');
    const second = await online(a);
    await replaced;
    expect(await emit(second, 'zone:join', { zone: 'PLAZA' })).toMatchObject({ ok: true });
    await new Promise((r) => setTimeout(r, 100));
    expect(first.connected).toBe(false);
  });

  it('flooding move messages is rate limited', async () => {
    const a = await newGuest(app);
    const sa = await online(a);
    await emit(sa, 'zone:join', { zone: 'PLAZA' });
    const results = await Promise.all(Array.from({ length: 60 }, () => emit(sa, 'move', { x: 30, y: 30 })));
    expect(results.some((r) => r.error?.code === 'RATE_LIMITED')).toBe(true);
  });
});
