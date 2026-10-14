import { INestApplication } from '@nestjs/common';
import { MINIGAME_DAILY_REWARDED_PLAYS } from '@hwo/shared-contracts';
import { PrismaService } from '../src/prisma/prisma.service';
import { createTestApp, newGuest, TestPlayer } from './helpers';

describe('Mini-games (make goods by hand)', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];

  beforeAll(async () => ({ app, prisma, http } = await createTestApp()));
  afterAll(async () => app.close());

  const shopOf = async (p: TestPlayer) => (await http().get('/api/v1/businesses').set(p.auth).expect(200)).body[0];
  const start = (p: TestPlayer, businessId: string, productCode = 'LEMONADE') =>
    http().post('/api/v1/minigames/start').set(p.auth).send({ businessId, productCode });
  /** The round "lasted" its full length: move its start back instead of waiting 45 s. */
  const timeTravel = (sessionId: string, seconds: number) =>
    prisma.$executeRaw`UPDATE "minigame_sessions" SET "started_at" = "started_at" - make_interval(secs => ${seconds}) WHERE "id" = ${sessionId}::uuid`;
  const finish = (p: TestPlayer, sessionId: string, score: number) => http().post(`/api/v1/minigames/${sessionId}/finish`).set(p.auth).send({ score });
  const xp = async (p: TestPlayer) => (await http().get('/api/v1/player/profile').set(p.auth).expect(200)).body.xp as number;

  it('a 3-star lemon round puts 10 free lemonades into storage and lowers the average cost', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    const s = await start(p, shop.id).expect(200);
    expect(s.body).toMatchObject({ game: 'CATCH', productCode: 'LEMONADE', durationSeconds: 45, starScores: [250, 500, 750], rewardedPlaysLeft: MINIGAME_DAILY_REWARDED_PLAYS });
    expect(Number.isInteger(s.body.seed)).toBe(true);
    const xpBefore = await xp(p);

    await timeTravel(s.body.sessionId, 45);
    const r = await finish(p, s.body.sessionId, 820).expect(200);
    expect(r.body).toMatchObject({ stars: 3, unitsAwarded: 10, xpAwarded: 8, rewarded: true, replayed: false, best: 820, rewardedPlaysLeft: MINIGAME_DAILY_REWARDED_PLAYS - 1 });
    const lemonade = r.body.business.products.find((x: { productCode: string }) => x.productCode === 'LEMONADE');
    expect(lemonade).toMatchObject({ quantity: 10, avgUnitCost: 0 });
    expect(await xp(p)).toBe(xpBefore + 8);
    // no Gold moved: homemade goods are paid with time, not money
    const wallet = await http().get('/api/v1/player/wallet').set(p.auth).expect(200);
    expect(wallet.body.balances.GOLD).toBe(500);
  });

  it('finishing twice returns the same result and adds the goods once', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    const s = await start(p, shop.id).expect(200);
    await timeTravel(s.body.sessionId, 45);
    const [a, b] = await Promise.all([finish(p, s.body.sessionId, 300), finish(p, s.body.sessionId, 300)]);
    expect([a.status, b.status]).toEqual([200, 200]);
    expect([a.body.replayed, b.body.replayed].sort()).toEqual([false, true]);
    const again = await finish(p, s.body.sessionId, 2000).expect(200); // a different score later changes nothing
    expect(again.body).toMatchObject({ stars: 1, unitsAwarded: 4, score: 300, replayed: true });
    expect((await shopOf(p)).products[0].quantity).toBe(4);
  });

  it('refuses a round reported before it could have ended, and impossible scores', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    const s = await start(p, shop.id).expect(200);
    const early = await finish(p, s.body.sessionId, 100).expect(409);
    expect(early.body.error.code).toBe('MINIGAME_TOO_EARLY');
    await timeTravel(s.body.sessionId, 44); // within the tolerance of a few seconds
    const cheat = await finish(p, s.body.sessionId, 1_000_000).expect(400);
    expect(cheat.body.error.code).toBe('VALIDATION_FAILED');
    const over = await finish(p, s.body.sessionId, 2801).expect(400);
    expect(over.body.error.code).toBe('MINIGAME_SCORE_INVALID');
    await finish(p, s.body.sessionId, 2800).expect(200);
  });

  it('a round left open too long is void', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    const s = await start(p, shop.id).expect(200);
    await timeTravel(s.body.sessionId, 45 + 601);
    const res = await finish(p, s.body.sessionId, 500).expect(409);
    expect(res.body.error.code).toBe('MINIGAME_EXPIRED');
  });

  it("other players can't play with my shop or finish my round", async () => {
    const owner = await newGuest(app);
    const other = await newGuest(app);
    const shop = await shopOf(owner);
    await start(other, shop.id).expect(404);
    const s = await start(owner, shop.id).expect(200);
    await timeTravel(s.body.sessionId, 45);
    await finish(other, s.body.sessionId, 500).expect(404);
  });

  it('locked products cannot be made yet; each product uses its own game', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    const locked = await start(p, shop.id, 'MEATBALL').expect(409);
    expect(locked.body.error.code).toBe('BUSINESS_PRODUCT_LOCKED');
    expect((await start(p, shop.id, 'BREAD').expect(200)).body.game).toBe('GRILL');
    expect((await start(p, shop.id, 'FLOWER').expect(200)).body.game).toBe('CATCH');
  });

  it('goods stop at the storage limit', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    await prisma.businessInventory.update({ where: { businessId_productCode: { businessId: shop.id, productCode: 'LEMONADE' } }, data: { quantity: 47, avgUnitCost: 5 } });
    const s = await start(p, shop.id).expect(200);
    await timeTravel(s.body.sessionId, 45);
    const r = await finish(p, s.body.sessionId, 900).expect(200);
    expect(r.body).toMatchObject({ unitsAwarded: 3, stockFull: true });
    expect(r.body.business.products[0]).toMatchObject({ quantity: 50, avgUnitCost: 5 }); // 47x5 + 3x0 over 50 = 4.7
  });

  it(`after ${MINIGAME_DAILY_REWARDED_PLAYS} rounds a day, rounds are just for fun`, async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    const s = await start(p, shop.id).expect(200);
    const day = (await prisma.minigameSession.findUniqueOrThrow({ where: { id: s.body.sessionId } })).bkkDay;
    await prisma.minigameSession.createMany({
      data: Array.from({ length: MINIGAME_DAILY_REWARDED_PLAYS }, () => ({
        userId: p.userId, businessId: shop.id, game: 'CATCH', productCode: 'LEMONADE', seed: 1, bkkDay: day, finishedAt: new Date(), score: 10, stars: 0, rewarded: true, unitsAwarded: 1,
      })),
    });
    await timeTravel(s.body.sessionId, 45);
    const r = await finish(p, s.body.sessionId, 900).expect(200);
    expect(r.body).toMatchObject({ stars: 3, rewarded: false, unitsAwarded: 0, xpAwarded: 0, rewardedPlaysLeft: 0, best: 900 });
    const overview = await http().get('/api/v1/minigames').set(p.auth).expect(200);
    expect(overview.body.rewardedPlaysLeft).toBe(0);
    expect(overview.body.games.find((g: { game: string }) => g.game === 'CATCH').best).toBe(900);
    expect(new Date(overview.body.resetsAt).getTime()).toBeGreaterThan(Date.now());
  });

  it('counts toward the "make goods yourself" quest', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    await http().post('/api/v1/quests/life_homemade_goods/start').set(p.auth).expect(201);
    for (let i = 0; i < 3; i++) {
      const s = await start(p, shop.id).expect(200);
      await timeTravel(s.body.sessionId, 45);
      await finish(p, s.body.sessionId, 100).expect(200);
    }
    const quests = await http().get('/api/v1/quests').set(p.auth).expect(200);
    const q = (quests.body as { id: string; progress: { status: string; value: number } }[]).find((x) => x.id === 'life_homemade_goods');
    expect(q?.progress).toMatchObject({ status: 'COMPLETED', value: 3 });
  });
});
