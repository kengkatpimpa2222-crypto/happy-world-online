import { INestApplication } from '@nestjs/common';
import { PrismaService } from '../src/prisma/prisma.service';
import { createTestApp, newGuest } from './helpers';

describe('Auth', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];

  beforeAll(async () => ({ app, prisma, http } = await createTestApp()));
  afterAll(async () => app.close());

  it('creates a guest with profile, 500 Gold, starter items, home and a shop', async () => {
    const p = await newGuest(app);
    expect(p.accessToken).toBeTruthy();
    expect(p.refreshToken.length).toBeGreaterThan(20);
    const user = await prisma.user.findUniqueOrThrow({
      where: { id: p.userId },
      include: { profile: true, wallets: true, inventory: true, home: true, businesses: true },
    });
    expect(user.profile).not.toBeNull();
    expect(user.wallets.find((w) => w.currency === 'GOLD')?.balance).toBe(500);
    expect(user.wallets.find((w) => w.currency === 'GEMS')?.balance).toBe(0);
    expect(user.inventory.map((i) => i.itemId).sort()).toEqual(['furn_wood_chair', 'hair_short_brown', 'outfit_basic_tee']);
    expect(user.home).not.toBeNull();
    expect(user.businesses).toHaveLength(1);
    // only a hash of the device id is stored
    expect(user.deviceIdHash).toMatch(/^[0-9a-f]{64}$/);
  });

  it('rejects invalid guest payloads', async () => {
    const res = await http().post('/api/v1/auth/guest').send({ deviceId: 'short', ageBand: 'KID' }).expect(400);
    expect(res.body.error.code).toBe('VALIDATION_FAILED');
    await http().post('/api/v1/auth/guest').send({ deviceId: 'device-12345', ageBand: 'ADULT_18_PLUS', isAdmin: true }).expect(400);
  });

  it('protected routes need a valid token', async () => {
    await http().get('/api/v1/player/profile').expect(401);
    await http().get('/api/v1/player/profile').set('Authorization', 'Bearer not-a-jwt').expect(401);
  });

  it('refresh rotates tokens; reusing an old refresh token revokes all sessions', async () => {
    const p = await newGuest(app);
    const r1 = await http().post('/api/v1/auth/refresh').send({ refreshToken: p.refreshToken }).expect(200);
    expect(r1.body.refreshToken).not.toBe(p.refreshToken);
    await http().get('/api/v1/player/profile').set('Authorization', `Bearer ${r1.body.accessToken}`).expect(200);

    // old token presented again = theft signal
    const reuse = await http().post('/api/v1/auth/refresh').send({ refreshToken: p.refreshToken }).expect(401);
    expect(reuse.body.error.code).toBe('REFRESH_TOKEN_INVALID');
    await http().get('/api/v1/player/profile').set('Authorization', `Bearer ${r1.body.accessToken}`).expect(401);
    await http().post('/api/v1/auth/refresh').send({ refreshToken: r1.body.refreshToken }).expect(401);
  });

  it('logout invalidates the access token immediately', async () => {
    const p = await newGuest(app);
    await http().post('/api/v1/auth/logout').set(p.auth).expect(204);
    await http().get('/api/v1/player/profile').set(p.auth).expect(401);
    await http().post('/api/v1/auth/refresh').send({ refreshToken: p.refreshToken }).expect(401);
  });

  it('links an account and recovers it with login on a new device', async () => {
    const p = await newGuest(app, 'ADULT_18_PLUS');
    const email = `player-${Date.now()}@example.com`;
    await http().post('/api/v1/auth/link-account').set(p.auth).send({ email, password: 'correct horse battery' }).expect(200);
    const again = await http().post('/api/v1/auth/link-account').set(p.auth).send({ email: 'x' + email, password: 'correct horse battery' }).expect(409);
    expect(again.body.error.code).toBe('ACCOUNT_ALREADY_LINKED');

    const other = await newGuest(app, 'ADULT_18_PLUS');
    const dup = await http().post('/api/v1/auth/link-account').set(other.auth).send({ email: email.toUpperCase(), password: 'another password' }).expect(409);
    expect(dup.body.error.code).toBe('EMAIL_IN_USE');

    const bad = await http().post('/api/v1/auth/login').send({ email, password: 'wrong password' }).expect(401);
    expect(bad.body.error.code).toBe('INVALID_CREDENTIALS');
    const ok = await http().post('/api/v1/auth/login').send({ email, password: 'correct horse battery' }).expect(200);
    expect(ok.body.userId).toBe(p.userId);
    expect(ok.body.isGuest).toBe(false);

    const stored = await prisma.user.findUniqueOrThrow({ where: { id: p.userId } });
    expect(stored.passwordHash).toMatch(/^scrypt\$/);
  });

  it('players under 13 need parental consent before linking an e-mail', async () => {
    const kid = await newGuest(app, 'UNDER_13');
    const res = await http().post('/api/v1/auth/link-account').set(kid.auth).send({ email: `kid-${Date.now()}@example.com`, password: 'password123' }).expect(403);
    expect(res.body.error.code).toBe('PARENTAL_CONSENT_REQUIRED');
  });
});
