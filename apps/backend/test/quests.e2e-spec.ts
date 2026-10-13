import { INestApplication } from '@nestjs/common';
import { PrismaService } from '../src/prisma/prisma.service';
import { createTestApp, key, newGuest, TestPlayer } from './helpers';

describe('Quests', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];

  beforeAll(async () => ({ app, prisma, http } = await createTestApp()));
  afterAll(async () => app.close());

  const wallet = async (p: TestPlayer) => (await http().get('/api/v1/player/wallet').set(p.auth)).body.balances;

  async function completeProfileQuest(p: TestPlayer) {
    await http().post('/api/v1/quests/newbie_set_profile/start').set(p.auth).expect(201);
    await http().patch('/api/v1/player/profile').set(p.auth).send({ displayName: 'Quester' }).expect(200);
  }

  it('lists quests with availability', async () => {
    const p = await newGuest(app);
    const res = await http().get('/api/v1/quests').set(p.auth).expect(200);
    const first = res.body.find((q: { id: string }) => q.id === 'newbie_set_profile');
    const second = res.body.find((q: { id: string }) => q.id === 'newbie_first_purchase');
    expect(first).toMatchObject({ canStart: true, progress: null, rewards: { gold: 100, xp: 20 } });
    expect(second.canStart).toBe(false); // prerequisite not done
  });

  it('progress is driven by server-side actions only, then reward can be claimed', async () => {
    const p = await newGuest(app);
    await completeProfileQuest(p);
    const res = await http().post('/api/v1/quests/newbie_set_profile/claim').set(p.auth).expect(200);
    expect(res.body).toMatchObject({ rewards: { gold: 100, xp: 20 }, wallet: { GOLD: 600 } });
    const profile = await http().get('/api/v1/player/profile').set(p.auth);
    expect(profile.body.xp).toBe(20);
  });

  it('cannot claim a quest that is not completed', async () => {
    const p = await newGuest(app);
    await http().post('/api/v1/quests/newbie_set_profile/start').set(p.auth).expect(201);
    const res = await http().post('/api/v1/quests/newbie_set_profile/claim').set(p.auth).expect(409);
    expect(res.body.error.code).toBe('QUEST_NOT_COMPLETED');
  });

  it('claiming twice pays once', async () => {
    const p = await newGuest(app);
    await completeProfileQuest(p);
    await http().post('/api/v1/quests/newbie_set_profile/claim').set(p.auth).expect(200);
    const again = await http().post('/api/v1/quests/newbie_set_profile/claim').set(p.auth).expect(409);
    expect(again.body.error.code).toBe('QUEST_ALREADY_CLAIMED');
    expect((await wallet(p)).GOLD).toBe(600);
  });

  it('10 parallel claims pay exactly once', async () => {
    const p = await newGuest(app);
    await completeProfileQuest(p);
    const res = await Promise.all(Array.from({ length: 10 }, () => http().post('/api/v1/quests/newbie_set_profile/claim').set(p.auth)));
    expect(res.filter((r) => r.status === 200)).toHaveLength(1);
    expect(res.filter((r) => r.status === 409)).toHaveLength(9);
    expect((await wallet(p)).GOLD).toBe(600);
    const ledger = await prisma.walletTransaction.count({ where: { userId: p.userId, type: 'QUEST_REWARD' } });
    expect(ledger).toBe(1);
  });

  it('cannot start the same quest twice or skip prerequisites', async () => {
    const p = await newGuest(app);
    await http().post('/api/v1/quests/newbie_set_profile/start').set(p.auth).expect(201);
    const dup = await http().post('/api/v1/quests/newbie_set_profile/start').set(p.auth).expect(409);
    expect(dup.body.error.code).toBe('QUEST_ALREADY_STARTED');
    const skip = await http().post('/api/v1/quests/newbie_first_purchase/start').set(p.auth).expect(409);
    expect(skip.body.error.code).toBe('QUEST_NOT_AVAILABLE');
  });

  it('quest chain: buying an item completes the next quest', async () => {
    const p = await newGuest(app);
    await completeProfileQuest(p);
    await http().post('/api/v1/quests/newbie_set_profile/claim').set(p.auth).expect(200);
    await http().post('/api/v1/quests/newbie_first_purchase/start').set(p.auth).expect(201);
    await http().post('/api/v1/store/purchases').set(p.auth).set('Idempotency-Key', key()).send({ itemId: 'furn_plant_pot', currency: 'GOLD' }).expect(201);
    await http().post('/api/v1/quests/newbie_first_purchase/claim').set(p.auth).expect(200);
    expect((await wallet(p)).GOLD).toBe(500 + 100 - 40 + 100);
  });

  it('unknown or malformed quest ids are rejected', async () => {
    const p = await newGuest(app);
    await http().post('/api/v1/quests/nope_quest/start').set(p.auth).expect(404);
    await http().post('/api/v1/quests/DROP%20TABLE/start').set(p.auth).expect(400);
  });
});
