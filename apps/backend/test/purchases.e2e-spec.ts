import { INestApplication } from '@nestjs/common';
import { randomUUID } from 'node:crypto';
import { PrismaService } from '../src/prisma/prisma.service';
import { createTestApp, newGuest, TestPlayer } from './helpers';

describe('Real-money purchase verification (DEV_MOCK adapter)', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];

  beforeAll(async () => ({ app, prisma, http } = await createTestApp()));
  afterAll(async () => app.close());

  const gems = async (p: TestPlayer) => (await http().get('/api/v1/player/wallet').set(p.auth)).body.balances.GEMS as number;
  const verify = (p: TestPlayer, purchaseToken: string, productId = 'gems_small', provider = 'DEV_MOCK') =>
    http().post('/api/v1/purchases/verify').set(p.auth).send({ provider, productId, purchaseToken });

  it('grants gems once for a valid token, even when retried', async () => {
    const p = await newGuest(app);
    const token = `mock_ok_${randomUUID()}`;
    const a = await verify(p, token).expect(200);
    expect(a.body).toMatchObject({ status: 'GRANTED', gemsGranted: 60, alreadyProcessed: false });
    const b = await verify(p, token).expect(200);
    expect(b.body).toMatchObject({ status: 'GRANTED', alreadyProcessed: true, purchaseId: a.body.purchaseId });
    expect(await gems(p)).toBe(60);
    const events = await prisma.purchaseEvent.findMany({ where: { purchaseId: a.body.purchaseId } });
    expect(events.map((e) => e.type).sort()).toEqual(['GRANTED', 'VERIFICATION']);
  });

  it('parallel verification of one token grants once', async () => {
    const p = await newGuest(app);
    const token = `mock_ok_${randomUUID()}`;
    const res = await Promise.all(Array.from({ length: 5 }, () => verify(p, token)));
    expect(res.every((r) => r.status === 200)).toBe(true);
    expect(res.filter((r) => r.body.alreadyProcessed === false)).toHaveLength(1);
    expect(await gems(p)).toBe(60);
  });

  it('rejects an invalid token and grants nothing', async () => {
    const p = await newGuest(app);
    const res = await verify(p, `forged_${randomUUID()}`).expect(400);
    expect(res.body.error.code).toBe('PURCHASE_INVALID');
    expect(await gems(p)).toBe(0);
  });

  it('pending purchases are recorded but not granted yet', async () => {
    const p = await newGuest(app);
    const res = await verify(p, `mock_pending_${randomUUID()}`).expect(200);
    expect(res.body).toMatchObject({ status: 'PENDING', gemsGranted: 0 });
    expect(await gems(p)).toBe(0);
  });

  it('one player cannot redeem another player\'s purchase token', async () => {
    const a = await newGuest(app);
    const b = await newGuest(app);
    const token = `mock_ok_${randomUUID()}`;
    await verify(a, token).expect(200);
    const res = await verify(b, token).expect(409);
    expect(res.body.error.code).toBe('PURCHASE_TOKEN_IN_USE');
    expect(await gems(b)).toBe(0);
  });

  it('unknown products are rejected; Google Play is honestly reported as not configured', async () => {
    const p = await newGuest(app);
    expect((await verify(p, `mock_ok_${randomUUID()}`, 'free_money').expect(400)).body.error.code).toBe('UNKNOWN_PRODUCT');
    expect((await verify(p, `real-token-${randomUUID()}`, 'gems_small', 'GOOGLE_PLAY').expect(503)).body.error.code).toBe('BILLING_NOT_CONFIGURED');
  });

  it('gems bought can be spent on premium items', async () => {
    const p = await newGuest(app);
    await verify(p, `mock_ok_${randomUUID()}`).expect(200);
    const res = await http().post('/api/v1/store/purchases').set(p.auth).set('Idempotency-Key', randomUUID()).send({ itemId: 'outfit_star_idol', currency: 'GEMS' }).expect(201);
    expect(res.body.balanceAfter).toBe(0);
  });
});
