import { INestApplication } from '@nestjs/common';
import { randomUUID } from 'node:crypto';
import { Socket } from 'socket.io-client';
import { STARTER_HOME_GIFT } from '@hwo/shared-contracts';
import { PrismaService } from '../src/prisma/prisma.service';
import { connectWs, createTestApp, friendCodeOf, key, newGuest, receivedWithin, TestPlayer, waitFor } from './helpers';

type Ack = { ok: boolean; error?: { code: string }; [k: string]: unknown };
interface Piece {
  id?: string;
  itemId: string;
  x: number;
  y: number;
  rotation: number;
}

describe('First home and the residential district (Phase 10)', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];
  const sockets: Socket[] = [];

  beforeAll(async () => {
    ({ app, prisma, http } = await createTestApp());
  });
  afterEach(() => {
    for (const s of sockets.splice(0)) s.disconnect();
  });
  afterAll(async () => app.close());

  const myHome = async (p: TestPlayer) => (await http().get('/api/v1/homes/me').set(p.auth).expect(200)).body;
  const claim = (p: TestPlayer) => http().post('/api/v1/homes/me/claim').set(p.auth);
  const bag = async (p: TestPlayer): Promise<Record<string, { inBag: number; placed: number }>> => {
    const rows = (await http().get('/api/v1/homes/me/furniture').set(p.auth).expect(200)).body as { itemId: string; inBag: number; placed: number }[];
    return Object.fromEntries(rows.map((r) => [r.itemId, { inBag: r.inBag, placed: r.placed }]));
  };
  const save = (p: TestPlayer, baseVersion: number, pieces: Piece[], k = key()) =>
    http().put('/api/v1/homes/me/layout').set(p.auth).set('Idempotency-Key', k).send({ baseVersion, pieces });
  const visit = (viewer: TestPlayer, owner: TestPlayer) => http().get(`/api/v1/homes/${owner.userId}`).set(viewer.auth);
  const setPolicy = (p: TestPlayer, visitPolicy: string) => http().patch('/api/v1/homes/me/settings').set(p.auth).send({ visitPolicy }).expect(200);
  const befriend = async (a: TestPlayer, b: TestPlayer) => {
    const r = await http().post('/api/v1/friends/requests').set(a.auth).send({ friendCode: await friendCodeOf(app, b) }).expect(201);
    await http().post(`/api/v1/friends/requests/${r.body.requestId}/accept`).set(b.auth).expect(200);
  };
  const homeOwner = async (ageBand?: string) => {
    const p = await newGuest(app, ageBand);
    await claim(p).expect(200);
    return p;
  };
  /** a saved piece as the client sends it back (the response also carries its footprint) */
  const asPiece = (f: Required<Piece>): Required<Piece> => ({ id: f.id, itemId: f.itemId, x: f.x, y: f.y, rotation: f.rotation });
  /** what the player owns of an item in total: in the bag + standing in the home */
  const owned = async (p: TestPlayer, itemId: string) => {
    const b = (await bag(p))[itemId];
    return (b?.inBag ?? 0) + (b?.placed ?? 0);
  };
  const ws = async (p: TestPlayer) => {
    const s = await connectWs(app, p.accessToken);
    sockets.push(s);
    return s;
  };
  const emit = (s: Socket, event: string, body?: unknown) => s.timeout(3000).emitWithAck(event, body) as Promise<Ack>;

  describe('starter home', () => {
    it('a new player has an unclaimed home and the chair every player starts with', async () => {
      const p = await newGuest(app);
      const h = await myHome(p);
      expect(h).toMatchObject({ claimed: false, plot: null, isMine: true, visitPolicy: 'FRIENDS', gridWidth: 8, gridHeight: 8, layoutVersion: 0, furniture: [] });
      expect(h.interior).toEqual({ width: 12, height: 12, spawn: { x: 6, y: 0.8 } });
      expect(await bag(p)).toEqual({ furn_wood_chair: { inBag: 1, placed: 0 } });
    });

    it('can be claimed once only, even with many taps at once, and the welcome furniture comes once', async () => {
      const p = await newGuest(app);
      const results = await Promise.all(Array.from({ length: 5 }, () => claim(p)));
      const ok = results.filter((r) => r.status === 200);
      expect(ok).toHaveLength(1);
      for (const r of results.filter((x) => x.status !== 200)) {
        expect(r.status).toBe(409);
        expect(r.body.error.code).toBe('HOME_ALREADY_CLAIMED');
      }
      expect(ok[0]!.body).toMatchObject({ claimed: true, gift: STARTER_HOME_GIFT, home: { claimed: true } });
      expect((await claim(p).expect(409)).body.error.code).toBe('HOME_ALREADY_CLAIMED');

      const b = await bag(p);
      expect(b).toMatchObject({ furn_wood_chair: { inBag: 1 }, furn_single_bed: { inBag: 1 }, furn_wood_table: { inBag: 1 }, furn_plant_pot: { inBag: 2 } });
      expect(await prisma.inventoryTransaction.count({ where: { userId: p.userId, type: 'HOME_STARTER_GIFT' } })).toBe(STARTER_HOME_GIFT.length);
      expect(await prisma.auditLog.count({ where: { actorUserId: p.userId, action: 'home.claim' } })).toBe(1);
      const h = await myHome(p);
      expect(h.plot).toMatchObject({ plotNumber: expect.any(Number), street: expect.any(Number), slot: expect.any(Number), door: { x: expect.any(Number), y: expect.any(Number) } });
    });

    it('players claiming at the same time never share a plot', async () => {
      const players = await Promise.all(Array.from({ length: 6 }, () => newGuest(app)));
      await Promise.all(players.map((p) => claim(p).expect(200)));
      const plots = await Promise.all(players.map(async (p) => (await myHome(p)).plot.plotNumber as number));
      expect(new Set(plots).size).toBe(players.length);
    });

    it('cannot save a layout before claiming', async () => {
      const p = await newGuest(app);
      expect((await save(p, 0, []).expect(409)).body.error.code).toBe('HOME_NOT_CLAIMED');
    });
  });

  describe('placing furniture', () => {
    it('placing takes pieces out of the bag, moving keeps them, removing puts them back', async () => {
      const p = await homeOwner();
      await http().post('/api/v1/quests/home_first_furniture/start').set(p.auth).expect(201);
      const placed = await save(p, 0, [
        { itemId: 'furn_single_bed', x: 0, y: 0, rotation: 0 },
        { itemId: 'furn_wood_table', x: 3, y: 3, rotation: 0 },
        { itemId: 'furn_wood_chair', x: 3, y: 4, rotation: 180 },
      ]).expect(200);
      expect(placed.body).toMatchObject({ replayed: false, placed: 3, moved: 0, removed: 0, version: 1, home: { layoutVersion: 1 } });
      expect(placed.body.home.furniture).toHaveLength(3);
      expect(await bag(p)).toMatchObject({ furn_single_bed: { inBag: 0, placed: 1 }, furn_wood_table: { inBag: 0, placed: 1 }, furn_wood_chair: { inBag: 0, placed: 1 } });
      expect(await prisma.inventoryTransaction.count({ where: { userId: p.userId, type: 'HOME_PLACE' } })).toBe(3);
      const quests = (await http().get('/api/v1/quests').set(p.auth).expect(200)).body as { id: string; progress: { status: string } | null }[];
      expect(quests.find((q) => q.id === 'home_first_furniture')?.progress?.status).toBe('COMPLETED');

      // move + turn the table, take the chair back
      const pieces = (placed.body.home.furniture as Required<Piece>[]).map(asPiece);
      const table = pieces.find((f) => f.itemId === 'furn_wood_table')!;
      const bed = pieces.find((f) => f.itemId === 'furn_single_bed')!;
      const moved = await save(p, 1, [bed, { ...table, x: 6, y: 5, rotation: 90 }]).expect(200);
      expect(moved.body).toMatchObject({ placed: 0, moved: 1, removed: 1, version: 2 });
      const after = moved.body.home.furniture as Required<Piece>[];
      expect(after.find((f) => f.id === table.id)).toMatchObject({ x: 6, y: 5, rotation: 90, footprint: [2, 1] });
      expect(await bag(p)).toMatchObject({ furn_wood_table: { inBag: 0, placed: 1 }, furn_wood_chair: { inBag: 1, placed: 0 } });
      expect(await prisma.inventoryTransaction.count({ where: { userId: p.userId, type: 'HOME_REMOVE' } })).toBe(1);
      expect(await prisma.auditLog.count({ where: { actorUserId: p.userId, action: 'home.layout_save' } })).toBe(2);

      // take everything back: every piece is in the bag again, nothing more, nothing less
      await save(p, 2, []).expect(200);
      expect(await bag(p)).toMatchObject({
        furn_single_bed: { inBag: 1, placed: 0 },
        furn_wood_table: { inBag: 1, placed: 0 },
        furn_wood_chair: { inBag: 1, placed: 0 },
        furn_plant_pot: { inBag: 2, placed: 0 },
      });
    });

    it('rejects pieces outside the room, on top of each other, non-furniture and furniture not in the bag', async () => {
      const p = await homeOwner();
      const reject = async (pieces: Piece[], status: number, code: string) => {
        const r = await save(p, 0, pieces).expect(status);
        expect(r.body.error.code).toBe(code);
        return r.body.error;
      };
      expect((await reject([{ itemId: 'furn_wood_table', x: 7, y: 0, rotation: 0 }], 400, 'HOME_PLACEMENT_INVALID')).details).toMatchObject({ problem: 'OUT_OF_BOUNDS' });
      expect((await reject([{ itemId: 'furn_single_bed', x: 2, y: 7, rotation: 0 }], 400, 'HOME_PLACEMENT_INVALID')).details).toMatchObject({ problem: 'OUT_OF_BOUNDS' });
      const overlap = await reject(
        [
          { itemId: 'furn_wood_table', x: 2, y: 2, rotation: 0 },
          { itemId: 'furn_plant_pot', x: 3, y: 2, rotation: 0 },
        ],
        400,
        'HOME_PLACEMENT_INVALID',
      );
      expect(overlap.details).toMatchObject({ problem: 'OVERLAP', index: 1, with: 0 });
      await reject([{ itemId: 'furn_wood_chair', x: 64, y: 0, rotation: 0 }], 400, 'VALIDATION_FAILED');
      await reject([{ itemId: 'furn_wood_chair', x: 1, y: 1, rotation: 45 }], 400, 'VALIDATION_FAILED');
      expect((await reject([{ itemId: 'outfit_basic_tee', x: 1, y: 1, rotation: 0 }], 400, 'HOME_PLACEMENT_INVALID')).details).toMatchObject({ problem: 'NOT_FURNITURE' });
      expect((await reject([{ itemId: 'no_such_item', x: 1, y: 1, rotation: 0 }], 400, 'HOME_PLACEMENT_INVALID')).details).toMatchObject({ problem: 'NOT_FURNITURE' });
      // one table in the bag, two asked for: nothing is placed at all
      await reject(
        [
          { itemId: 'furn_wood_table', x: 0, y: 0, rotation: 0 },
          { itemId: 'furn_wood_table', x: 0, y: 2, rotation: 0 },
        ],
        409,
        'ITEM_NOT_OWNED',
      );
      // a premium sofa the player never bought
      await reject([{ itemId: 'furn_sofa_cloud', x: 0, y: 0, rotation: 0 }], 409, 'ITEM_NOT_OWNED');
      // a made-up piece id
      expect((await reject([{ id: randomUUID(), itemId: 'furn_wood_chair', x: 0, y: 0, rotation: 0 }], 400, 'HOME_PLACEMENT_INVALID')).details).toMatchObject({ problem: 'UNKNOWN_PIECE' });

      const h = await myHome(p);
      expect(h).toMatchObject({ layoutVersion: 0, furniture: [] });
      expect(await bag(p)).toMatchObject({ furn_wood_table: { inBag: 1, placed: 0 }, furn_plant_pot: { inBag: 2, placed: 0 } });
    });

    it('needs an Idempotency-Key; a retried save is answered once, a stale version is refused', async () => {
      const p = await homeOwner();
      await http().put('/api/v1/homes/me/layout').set(p.auth).send({ baseVersion: 0, pieces: [] }).expect(400);
      const k = key();
      const first = await save(p, 0, [{ itemId: 'furn_plant_pot', x: 1, y: 1, rotation: 0 }], k).expect(200);
      const retry = await save(p, 0, [{ itemId: 'furn_plant_pot', x: 1, y: 1, rotation: 0 }], k).expect(200);
      expect(first.body.replayed).toBe(false);
      expect(retry.body).toMatchObject({ replayed: true, home: { layoutVersion: 1 } });
      expect(retry.body.home.furniture).toHaveLength(1);
      expect(await bag(p)).toMatchObject({ furn_plant_pot: { inBag: 1, placed: 1 } });

      const stale = await save(p, 0, [{ itemId: 'furn_plant_pot', x: 5, y: 5, rotation: 0 }]).expect(409);
      expect(stale.body.error).toMatchObject({ code: 'HOME_LAYOUT_CONFLICT', details: { layoutVersion: 1 } });

      // the old key used again for a different save (after another save) is refused, not replayed into a duplicate
      const pot = retry.body.home.furniture[0];
      await save(p, 1, []).expect(200);
      const reused = await save(p, 2, [{ itemId: 'furn_plant_pot', x: pot.x, y: pot.y, rotation: 0 }], k).expect(409);
      expect(reused.body.error.code).toBe('IDEMPOTENCY_KEY_REUSED');
      expect(await owned(p, 'furn_plant_pot')).toBe(2);
    });

    it('simultaneous edits from two devices never duplicate or lose a piece', async () => {
      const p = await homeOwner();
      // the only chair, placed at six spots by six devices at once
      const results = await Promise.all(Array.from({ length: 6 }, (_, i) => save(p, 0, [{ itemId: 'furn_wood_chair', x: i, y: 0, rotation: 0 }])));
      expect(results.filter((r) => r.status === 200)).toHaveLength(1);
      for (const r of results.filter((x) => x.status !== 200)) expect(r.body.error.code).toBe('HOME_LAYOUT_CONFLICT');
      expect(await bag(p)).toMatchObject({ furn_wood_chair: { inBag: 0, placed: 1 } });

      // the same tap retried in parallel: one save, the rest are replays
      const k = key();
      const h = await myHome(p);
      const retries = await Promise.all(Array.from({ length: 4 }, () => save(p, h.layoutVersion, [...h.furniture.map(asPiece), { itemId: 'furn_plant_pot', x: 4, y: 4, rotation: 0 }], k)));
      expect(retries.map((r) => r.status)).toEqual([200, 200, 200, 200]);
      expect(retries.filter((r) => !r.body.replayed)).toHaveLength(1);
      expect(await bag(p)).toMatchObject({ furn_plant_pot: { inBag: 1, placed: 1 }, furn_wood_chair: { inBag: 0, placed: 1 } });

      // the bag and the home move in one transaction: a store purchase racing a save still adds up
      await http().post('/api/v1/store/purchases').set(p.auth).set('Idempotency-Key', key()).send({ itemId: 'furn_plant_pot', quantity: 1, currency: 'GOLD' }).expect(201);
      const now = await myHome(p);
      await Promise.all([
        save(p, now.layoutVersion, [...now.furniture.map(asPiece), { itemId: 'furn_plant_pot', x: 6, y: 6, rotation: 0 }]).expect(200),
        http().post('/api/v1/store/purchases').set(p.auth).set('Idempotency-Key', key()).send({ itemId: 'furn_plant_pot', quantity: 1, currency: 'GOLD' }).expect(201),
      ]);
      expect(await owned(p, 'furn_plant_pot')).toBe(4);
      const ledger = await prisma.inventoryTransaction.aggregate({ where: { userId: p.userId, itemId: 'furn_wood_chair' }, _sum: { delta: true } });
      expect(ledger._sum.delta).toBe(-1); // the chair was placed once; the starter chair predates the ledger
    });

    it('the layout is still there after logging out and back in', async () => {
      const p = await homeOwner();
      const email = `home-${randomUUID()}@example.com`;
      await http().post('/api/v1/auth/link-account').set(p.auth).send({ email, password: 'correct horse battery' }).expect(200);
      const saved = await save(p, 0, [
        { itemId: 'furn_single_bed', x: 6, y: 0, rotation: 0 },
        { itemId: 'furn_wood_table', x: 2, y: 5, rotation: 90 },
      ]).expect(200);
      await http().post('/api/v1/auth/logout').set(p.auth).expect(204);
      await http().get('/api/v1/homes/me').set(p.auth).expect(401);

      const login = await http().post('/api/v1/auth/login').send({ email, password: 'correct horse battery' }).expect(200);
      const again = await myHome({ ...p, auth: { Authorization: `Bearer ${login.body.accessToken}` } });
      expect(again.layoutVersion).toBe(1);
      expect(again.furniture).toEqual(saved.body.home.furniture);
    });
  });

  describe('visiting', () => {
    it('friends-only by default: strangers are refused, friends may look around but not see the setting', async () => {
      const owner = await homeOwner();
      await save(owner, 0, [{ itemId: 'furn_single_bed', x: 0, y: 0, rotation: 0 }]).expect(200);
      const stranger = await newGuest(app);
      expect((await visit(stranger, owner).expect(403)).body.error.code).toBe('HOME_VISIT_NOT_ALLOWED');

      const friend = await newGuest(app);
      await befriend(friend, owner);
      const v = await visit(friend, owner).expect(200);
      expect(v.body).toMatchObject({ isMine: false, isFriend: true, claimed: true, owner: { userId: owner.userId }, layoutVersion: 1 });
      expect(v.body.furniture).toHaveLength(1);
      expect(v.body.visitPolicy).toBeUndefined();
      // my own home through the same route is just my home
      expect((await visit(owner, owner).expect(200)).body).toMatchObject({ isMine: true, visitPolicy: 'FRIENDS' });
    });

    it('private closes the home to friends; public opens it to everyone of the same age band', async () => {
      const owner = await homeOwner();
      const friend = await newGuest(app);
      await befriend(friend, owner);
      const stranger = await newGuest(app);
      const adult = await newGuest(app, 'ADULT_18_PLUS');

      await setPolicy(owner, 'PRIVATE');
      expect((await visit(friend, owner).expect(403)).body.error.code).toBe('HOME_VISIT_NOT_ALLOWED');
      await setPolicy(owner, 'PUBLIC');
      await visit(friend, owner).expect(200);
      await visit(stranger, owner).expect(200);
      await visit(adult, owner).expect(404); // other age bands never meet, not even through a home
      expect(await prisma.auditLog.count({ where: { actorUserId: owner.userId, action: 'home.visit_policy' } })).toBe(2);
      await http().patch('/api/v1/homes/me/settings').set(owner.auth).send({ visitPolicy: 'EVERYONE' }).expect(400);
    });

    it('blocked players and unclaimed homes cannot be visited', async () => {
      const owner = await homeOwner();
      await setPolicy(owner, 'PUBLIC');
      const pest = await newGuest(app);
      await visit(pest, owner).expect(200);
      await http().post(`/api/v1/players/${pest.userId}/block`).set(owner.auth).expect(200);
      await visit(pest, owner).expect(404);

      const nobody = await newGuest(app);
      await visit(pest, nobody).expect(404);
      await http().get('/api/v1/homes/not-a-uuid').set(pest.auth).expect(400);
    });

    it("nobody can move, take or place furniture in someone else's home", async () => {
      const owner = await homeOwner();
      await setPolicy(owner, 'PUBLIC');
      const saved = await save(owner, 0, [{ itemId: 'furn_wood_table', x: 1, y: 1, rotation: 0 }]).expect(200);
      const table = saved.body.home.furniture[0];
      const thief = await homeOwner();
      // the only write routes are /homes/me/...: the owner's piece id is unknown in the thief's own home
      const r = await save(thief, 0, [{ id: table.id, itemId: table.itemId, x: 0, y: 0, rotation: 0 }]).expect(400);
      expect(r.body.error.details).toMatchObject({ problem: 'UNKNOWN_PIECE' });
      // no route takes an owner id for writing
      await http().put(`/api/v1/homes/${owner.userId}/layout`).set(thief.auth).set('Idempotency-Key', key()).send({ baseVersion: 1, pieces: [] }).expect(404);
      await http().patch(`/api/v1/homes/${owner.userId}/settings`).set(thief.auth).send({ visitPolicy: 'PRIVATE' }).expect(404);
      const after = await myHome(owner);
      expect(after).toMatchObject({ layoutVersion: 1, visitPolicy: 'PUBLIC' });
      expect(after.furniture).toEqual(saved.body.home.furniture);
      expect(await owned(thief, 'furn_wood_table')).toBe(1);
    });

    it('the district shows my street: my plot, friends I can visit and nameless homes of blocked players', async () => {
      const me = await homeOwner();
      const friend = await homeOwner();
      await befriend(me, friend);
      const blocked = await homeOwner();
      await http().post(`/api/v1/players/${blocked.userId}/block`).set(me.auth).expect(200);

      const mine = await myHome(me);
      const street = mine.plot.street as number;
      const d = (await http().get(`/api/v1/homes/district?street=${street}`).set(me.auth).expect(200)).body;
      expect(d).toMatchObject({ street, myPlot: mine.plot.plotNumber });
      expect(d.plots).toHaveLength(12);
      const at = (p: TestPlayer) => d.plots.find((x: { owner: { userId: string } | null }) => x.owner?.userId === p.userId);
      expect(d.plots.find((x: { isMine: boolean }) => x.isMine)).toMatchObject({ plotNumber: mine.plot.plotNumber, canVisit: true });
      const f = await myHome(friend);
      if (f.plot.street === street) expect(at(friend)).toMatchObject({ isFriend: true, canVisit: true });
      expect(at(blocked)).toBeUndefined();
      // default street = my own
      expect((await http().get('/api/v1/homes/district').set(me.auth).expect(200)).body.street).toBe(street);
      await http().get('/api/v1/homes/district?street=-1').set(me.auth).expect(400);
    });
  });

  describe('being in a home together (realtime)', () => {
    it('a friend walks in, sees the owner, gets layout updates; strangers are turned away at the door', async () => {
      const owner = await homeOwner();
      const friend = await newGuest(app);
      await befriend(friend, owner);
      const stranger = await newGuest(app);
      const [so, sf, ss] = await Promise.all([ws(owner), ws(friend), ws(stranger)]);

      const mine = await emit(so, 'home:enter', { ownerId: owner.userId });
      expect(mine).toMatchObject({ ok: true, isMine: true, interior: { width: 12, height: 12 }, layoutVersion: 0 });
      const joined = waitFor(so, 'player:joined', (p: { userId: string }) => p.userId === friend.userId);
      const state = waitFor(sf, 'home:state');
      const enter = await emit(sf, 'home:enter', { ownerId: owner.userId });
      expect(enter).toMatchObject({ ok: true, isMine: false, ownerId: owner.userId });
      expect((enter.players as { userId: string }[]).map((p) => p.userId)).toEqual([owner.userId]);
      expect(await state).toMatchObject({ ownerId: owner.userId, you: { x: 6, y: 0.8 } });
      await joined;

      const denied = await emit(ss, 'home:enter', { ownerId: owner.userId });
      expect(denied).toMatchObject({ ok: false, error: { code: 'HOME_VISIT_NOT_ALLOWED' } });
      expect((await emit(ss, 'home:enter', { ownerId: 'nope' })).error?.code).toBe('VALIDATION_FAILED');

      // moving inside the room: within the walls only
      sf.emit('move', { x: 6.5, y: 1.5, dir: 0, seq: 1 });
      await waitFor(so, 'player:moved', (m: { userId: string }) => m.userId === friend.userId);
      expect((await emit(sf, 'move', { x: 30, y: 30 })).error?.code).toBe('MOVE_REJECTED');

      const layout = waitFor(sf, 'home:layout');
      await save(owner, 0, [{ itemId: 'furn_wood_chair', x: 0, y: 0, rotation: 0 }]).expect(200);
      expect(await layout).toEqual({ ownerId: owner.userId, layoutVersion: 1 });
      // nobody outside the home hears about it
      expect(await receivedWithin(ss, 'home:layout')).toBe(false);

      // leaving = joining a city zone again
      const left = waitFor(so, 'player:left', (p: { userId: string }) => p.userId === friend.userId);
      expect(await emit(sf, 'zone:join', { zone: 'HOMES' })).toMatchObject({ ok: true, zone: 'HOMES' });
      await left;
    });

    it('closing the home, unfriending or blocking walks the visitor out at once', async () => {
      const owner = await homeOwner();
      const friend = await newGuest(app);
      await befriend(friend, owner);
      const [so, sf] = await Promise.all([ws(owner), ws(friend)]);
      await emit(so, 'home:enter', { ownerId: owner.userId });

      expect((await emit(sf, 'home:enter', { ownerId: owner.userId })).ok).toBe(true);
      let kicked = waitFor(sf, 'home:kicked');
      let left = waitFor(so, 'player:left', (p: { userId: string }) => p.userId === friend.userId);
      await setPolicy(owner, 'PRIVATE');
      expect(await kicked).toEqual({ ownerId: owner.userId });
      await left;
      expect((await emit(sf, 'move', { x: 6, y: 1 })).error?.code).toBe('NOT_IN_ZONE');
      expect((await emit(sf, 'home:enter', { ownerId: owner.userId })).error?.code).toBe('HOME_VISIT_NOT_ALLOWED');

      await setPolicy(owner, 'FRIENDS');
      expect((await emit(sf, 'home:enter', { ownerId: owner.userId })).ok).toBe(true);
      kicked = waitFor(sf, 'home:kicked');
      await http().delete(`/api/v1/friends/${owner.userId}`).set(friend.auth).expect(204);
      await kicked;

      await setPolicy(owner, 'PUBLIC');
      expect((await emit(sf, 'home:enter', { ownerId: owner.userId })).ok).toBe(true);
      kicked = waitFor(sf, 'home:kicked');
      left = waitFor(so, 'player:left', (p: { userId: string }) => p.userId === friend.userId);
      await http().post(`/api/v1/players/${friend.userId}/block`).set(owner.auth).expect(200);
      await kicked;
      await left;
    });
  });
});
