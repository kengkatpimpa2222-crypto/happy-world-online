import { INestApplication } from '@nestjs/common';
import { PrismaService } from '../src/prisma/prisma.service';
import { createTestApp, friendCodeOf, newGuest, TestPlayer } from './helpers';

describe('Friends, blocking and reports', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];

  beforeAll(async () => ({ app, prisma, http } = await createTestApp()));
  afterAll(async () => app.close());

  const send = (from: TestPlayer, body: object) => http().post('/api/v1/friends/requests').set(from.auth).send(body);
  const friendsOf = async (p: TestPlayer) => (await http().get('/api/v1/friends').set(p.auth).expect(200)).body as { userId: string }[];
  const pairRows = (a: TestPlayer, b: TestPlayer) =>
    prisma.friend.count({ where: { OR: [{ userId: a.userId, friendId: b.userId }, { userId: b.userId, friendId: a.userId }] } });

  describe('friend requests', () => {
    it('send by friend code -> listed incoming/outgoing -> accept -> friends on both sides', async () => {
      const a = await newGuest(app);
      const b = await newGuest(app);
      const res = await send(a, { friendCode: await friendCodeOf(app, b) }).expect(201);
      expect(res.body).toMatchObject({ status: 'PENDING', created: true, autoAccepted: false });

      const inc = await http().get('/api/v1/friends/requests/incoming').set(b.auth).expect(200);
      expect(inc.body).toHaveLength(1);
      expect(inc.body[0].from.userId).toBe(a.userId);
      expect(inc.body[0].from.displayName).toBeDefined();
      const out = await http().get('/api/v1/friends/requests/outgoing').set(a.auth).expect(200);
      expect(out.body[0].to.userId).toBe(b.userId);

      await http().post(`/api/v1/friends/requests/${res.body.requestId}/accept`).set(b.auth).expect(200);
      expect((await friendsOf(a)).map((f) => f.userId)).toEqual([b.userId]);
      expect((await friendsOf(b)).map((f) => f.userId)).toEqual([a.userId]);
      expect((await http().get('/api/v1/friends/requests/incoming').set(b.auth)).body).toHaveLength(0);
    });

    it('cannot add yourself, a missing player, or an existing friend', async () => {
      const a = await newGuest(app);
      const b = await newGuest(app);
      expect((await send(a, { playerId: a.userId }).expect(400)).body.error.code).toBe('CANNOT_TARGET_SELF');
      await send(a, { playerId: '00000000-0000-4000-8000-000000000000' }).expect(404);
      await send(a, { friendCode: 'ZZZZZZZZ' }).expect(404);
      await send(a, {}).expect(400);
      const r = await send(a, { playerId: b.userId }).expect(201);
      await http().post(`/api/v1/friends/requests/${r.body.requestId}/accept`).set(b.auth).expect(200);
      expect((await send(a, { playerId: b.userId }).expect(409)).body.error.code).toBe('ALREADY_FRIENDS');
      expect((await send(b, { playerId: a.userId }).expect(409)).body.error.code).toBe('ALREADY_FRIENDS');
    });

    it('retrying a request returns the same pending request (no duplicates)', async () => {
      const a = await newGuest(app);
      const b = await newGuest(app);
      const first = await send(a, { playerId: b.userId }).expect(201);
      const again = await send(a, { playerId: b.userId }).expect(201);
      expect(again.body).toMatchObject({ requestId: first.body.requestId, created: false });
    });

    it('10 parallel sends create exactly one pending request', async () => {
      const a = await newGuest(app);
      const b = await newGuest(app);
      const res = await Promise.all(Array.from({ length: 10 }, () => send(a, { playerId: b.userId })));
      expect(res.every((r) => r.status === 201)).toBe(true);
      expect(new Set(res.map((r) => r.body.requestId)).size).toBe(1);
      expect(await prisma.friendRequest.count({ where: { fromUserId: a.userId, toUserId: b.userId } })).toBe(1);
    });

    it('crossed requests (A->B, then B->A) become a friendship, never two pending requests', async () => {
      const a = await newGuest(app);
      const b = await newGuest(app);
      await send(a, { playerId: b.userId }).expect(201);
      const res = await send(b, { playerId: a.userId }).expect(201);
      expect(res.body).toMatchObject({ status: 'ACCEPTED', autoAccepted: true });
      expect(await pairRows(a, b)).toBe(2);
    });

    it('simultaneous crossed requests still end as exactly one friendship', async () => {
      for (let i = 0; i < 5; i++) {
        const a = await newGuest(app);
        const b = await newGuest(app);
        const res = await Promise.all([send(a, { playerId: b.userId }), send(b, { playerId: a.userId })]);
        expect(res.map((r) => r.status)).toEqual([201, 201]);
        expect(await pairRows(a, b)).toBe(2);
        expect(await prisma.friendRequest.count({ where: { pairKey: { contains: a.userId }, status: 'PENDING' } })).toBe(0);
      }
    });

    it('only the receiver can accept or decline; accepting twice is harmless', async () => {
      const a = await newGuest(app);
      const b = await newGuest(app);
      const stranger = await newGuest(app);
      const { body } = await send(a, { playerId: b.userId }).expect(201);
      await http().post(`/api/v1/friends/requests/${body.requestId}/accept`).set(a.auth).expect(404);
      await http().post(`/api/v1/friends/requests/${body.requestId}/accept`).set(stranger.auth).expect(404);
      await http().post(`/api/v1/friends/requests/${body.requestId}/decline`).set(stranger.auth).expect(404);

      const results = await Promise.all([1, 2, 3].map(() => http().post(`/api/v1/friends/requests/${body.requestId}/accept`).set(b.auth)));
      expect(results.every((r) => r.status === 200)).toBe(true);
      expect(results.filter((r) => r.body.alreadyAccepted === false)).toHaveLength(1);
      expect(await pairRows(a, b)).toBe(2);
    });

    it('decline, then the sender may ask again later', async () => {
      const a = await newGuest(app);
      const b = await newGuest(app);
      const r1 = await send(a, { playerId: b.userId }).expect(201);
      await http().post(`/api/v1/friends/requests/${r1.body.requestId}/decline`).set(b.auth).expect(200);
      await http().post(`/api/v1/friends/requests/${r1.body.requestId}/decline`).set(b.auth).expect(200); // idempotent
      const accepted = await http().post(`/api/v1/friends/requests/${r1.body.requestId}/accept`).set(b.auth).expect(409);
      expect(accepted.body.error.code).toBe('FRIEND_REQUEST_CLOSED');
      const r2 = await send(a, { playerId: b.userId }).expect(201);
      expect(r2.body.requestId).not.toBe(r1.body.requestId);
    });

    it('sender can cancel; nobody else can', async () => {
      const a = await newGuest(app);
      const b = await newGuest(app);
      const { body } = await send(a, { playerId: b.userId }).expect(201);
      await http().delete(`/api/v1/friends/requests/${body.requestId}`).set(b.auth).expect(404);
      await http().delete(`/api/v1/friends/requests/${body.requestId}`).set(a.auth).expect(204);
      await http().delete(`/api/v1/friends/requests/${body.requestId}`).set(a.auth).expect(204);
      expect((await http().get('/api/v1/friends/requests/incoming').set(b.auth)).body).toHaveLength(0);
    });

    it('players of different age bands cannot befriend each other (child safety)', async () => {
      const adult = await newGuest(app, 'ADULT_18_PLUS');
      const kid = await newGuest(app, 'UNDER_13');
      const res = await send(adult, { playerId: kid.userId }).expect(403);
      expect(res.body.error.code).toBe('FRIEND_REQUEST_NOT_ALLOWED');
      await send(kid, { friendCode: await friendCodeOf(app, adult) }).expect(403);
    });

    it('accepting a friend advances the co-op quest for both players', async () => {
      const a = await newGuest(app);
      const b = await newGuest(app);
      await http().post('/api/v1/quests/coop_make_friend/start').set(a.auth).expect(201);
      const { body } = await send(b, { playerId: a.userId }).expect(201);
      await http().post(`/api/v1/friends/requests/${body.requestId}/accept`).set(a.auth).expect(200);
      await http().post('/api/v1/quests/coop_make_friend/claim').set(a.auth).expect(200);
    });

    it('removing a friend removes only the friendship, both directions, idempotently', async () => {
      const a = await newGuest(app);
      const b = await newGuest(app);
      const { body } = await send(a, { playerId: b.userId }).expect(201);
      await http().post(`/api/v1/friends/requests/${body.requestId}/accept`).set(b.auth).expect(200);
      await http().delete(`/api/v1/friends/${b.userId}`).set(a.auth).expect(204);
      await http().delete(`/api/v1/friends/${b.userId}`).set(a.auth).expect(204);
      expect(await friendsOf(a)).toHaveLength(0);
      expect(await friendsOf(b)).toHaveLength(0);
      await http().get('/api/v1/player/profile').set(b.auth).expect(200);
      expect((await http().get('/api/v1/player/wallet').set(b.auth)).body.balances.GOLD).toBe(500);
      await send(a, { playerId: b.userId }).expect(201); // can become friends again
    });
  });

  describe('blocking', () => {
    it('cannot block yourself; blocking twice keeps one row', async () => {
      const a = await newGuest(app);
      const b = await newGuest(app);
      expect((await http().post(`/api/v1/players/${a.userId}/block`).set(a.auth).expect(400)).body.error.code).toBe('CANNOT_TARGET_SELF');
      const first = await http().post(`/api/v1/players/${b.userId}/block`).set(a.auth).expect(200);
      const second = await http().post(`/api/v1/players/${b.userId}/block`).set(a.auth).expect(200);
      expect(first.body.alreadyBlocked).toBe(false);
      expect(second.body.alreadyBlocked).toBe(true);
      await Promise.all([1, 2, 3].map(() => http().post(`/api/v1/players/${b.userId}/block`).set(a.auth).expect(200)));
      expect(await prisma.blockedPlayer.count({ where: { blockerId: a.userId, blockedId: b.userId } })).toBe(1);
      await http().post('/api/v1/players/00000000-0000-4000-8000-000000000000/block').set(a.auth).expect(404);
    });

    it('blocking ends the friendship and pending requests, and stops new requests both ways', async () => {
      const a = await newGuest(app);
      const b = await newGuest(app);
      const c = await newGuest(app);
      const r = await send(a, { playerId: b.userId }).expect(201);
      await http().post(`/api/v1/friends/requests/${r.body.requestId}/accept`).set(b.auth).expect(200);
      await send(c, { playerId: a.userId }).expect(201);

      await http().post(`/api/v1/players/${b.userId}/block`).set(a.auth).expect(200);
      await http().post(`/api/v1/players/${c.userId}/block`).set(a.auth).expect(200);
      expect(await friendsOf(a)).toHaveLength(0);
      expect(await friendsOf(b)).toHaveLength(0);
      expect((await http().get('/api/v1/friends/requests/incoming').set(a.auth)).body).toHaveLength(0);

      // the blocked player cannot reach the blocker, and the error does not reveal the block
      expect((await send(b, { playerId: a.userId }).expect(403)).body.error.code).toBe('FRIEND_REQUEST_NOT_ALLOWED');
      expect((await send(a, { playerId: b.userId }).expect(403)).body.error.code).toBe('FRIEND_REQUEST_NOT_ALLOWED');
    });

    it('lists blocked players and unblocks idempotently', async () => {
      const a = await newGuest(app);
      const b = await newGuest(app);
      await http().post(`/api/v1/players/${b.userId}/block`).set(a.auth).expect(200);
      const list = await http().get('/api/v1/players/blocked').set(a.auth).expect(200);
      expect(list.body).toEqual([expect.objectContaining({ playerId: b.userId })]);
      expect((await http().get('/api/v1/players/blocked').set(b.auth)).body).toHaveLength(0); // b cannot see who blocked them
      await http().delete(`/api/v1/players/${b.userId}/block`).set(a.auth).expect(204);
      await http().delete(`/api/v1/players/${b.userId}/block`).set(a.auth).expect(204);
      expect((await http().get('/api/v1/players/blocked').set(a.auth)).body).toHaveLength(0);
      await send(b, { playerId: a.userId }).expect(201);
    });
  });

  describe('moderation reports', () => {
    it('files a report, returns the same open report on retry, lists my reports', async () => {
      const a = await newGuest(app);
      const b = await newGuest(app);
      const body = { reportedPlayerId: b.userId, reason: 'HARASSMENT', contextCode: 'PRESET_CHAT' };
      const res = await Promise.all([1, 2, 3].map(() => http().post('/api/v1/moderation/reports').set(a.auth).send(body)));
      expect(res.every((r) => r.status === 201)).toBe(true);
      expect(new Set(res.map((r) => r.body.reportId)).size).toBe(1);
      expect(await prisma.moderationReport.count({ where: { reporterId: a.userId } })).toBe(1);

      await http().post('/api/v1/moderation/reports').set(a.auth).send({ ...body, reason: 'SPAM' }).expect(201);
      const mine = await http().get('/api/v1/moderation/reports/me').set(a.auth).expect(200);
      expect(mine.body).toHaveLength(2);
      expect(mine.body[0]).toMatchObject({ reportedPlayerId: b.userId, status: 'OPEN' });
      expect((await http().get('/api/v1/moderation/reports/me').set(b.auth)).body).toHaveLength(0);
      expect(await prisma.auditLog.count({ where: { actorUserId: a.userId, action: 'moderation.report_created' } })).toBe(2);
    });

    it('can report and block in one step; rejects self, unknown players and free text', async () => {
      const a = await newGuest(app);
      const b = await newGuest(app);
      const res = await http().post('/api/v1/moderation/reports').set(a.auth).send({ reportedPlayerId: b.userId, reason: 'ASKING_PERSONAL_INFO', alsoBlock: true }).expect(201);
      expect(res.body.blocked).toBe(true);
      expect((await http().get('/api/v1/players/blocked').set(a.auth)).body).toHaveLength(1);
      await http().post('/api/v1/moderation/reports').set(a.auth).send({ reportedPlayerId: a.userId, reason: 'SPAM' }).expect(400);
      await http().post('/api/v1/moderation/reports').set(a.auth).send({ reportedPlayerId: '00000000-0000-4000-8000-000000000000', reason: 'SPAM' }).expect(404);
      await http().post('/api/v1/moderation/reports').set(a.auth).send({ reportedPlayerId: b.userId, reason: 'SPAM', details: 'free text' }).expect(400);
    });
  });
});
