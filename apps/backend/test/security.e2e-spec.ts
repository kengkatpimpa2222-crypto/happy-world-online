import { INestApplication } from '@nestjs/common';
import { createTestApp, key, newGuest } from './helpers';

describe('Ownership: a player can never touch another player\'s data', () => {
  let app: INestApplication;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];

  beforeAll(async () => ({ app, http } = await createTestApp()));
  afterAll(async () => app.close());

  it('cannot read, price, restock, sell or upgrade someone else\'s shop', async () => {
    const owner = await newGuest(app);
    const attacker = await newGuest(app);
    const shop = (await http().get('/api/v1/businesses').set(owner.auth)).body[0];

    await http().get(`/api/v1/businesses/${shop.id}`).set(attacker.auth).expect(404);
    await http().patch(`/api/v1/businesses/${shop.id}/pricing`).set(attacker.auth).send({ productCode: 'LEMONADE', price: 6 }).expect(404);
    await http().post(`/api/v1/businesses/${shop.id}/restock`).set(attacker.auth).set('Idempotency-Key', key()).send({ productCode: 'LEMONADE', quantity: 5 }).expect(404);
    await http().post(`/api/v1/businesses/${shop.id}/sell`).set(attacker.auth).expect(404);
    await http().post(`/api/v1/businesses/${shop.id}/upgrade`).set(attacker.auth).send({ expectedLevel: 1 }).expect(404);

    const after = (await http().get(`/api/v1/businesses/${shop.id}`).set(owner.auth)).body;
    expect(after.level).toBe(1);
    expect(after.products[0].sellPrice).toBe(10);
    const attackerGold = (await http().get('/api/v1/player/wallet').set(attacker.auth)).body.balances.GOLD;
    expect(attackerGold).toBe(500);
  });

  it('a forged token signed with another secret is rejected', async () => {
    const victim = await newGuest(app);
    const [h, , ] = victim.accessToken.split('.');
    const payload = Buffer.from(JSON.stringify({ sub: victim.userId, sid: '00000000-0000-0000-0000-000000000000' })).toString('base64url');
    await http().get('/api/v1/player/profile').set('Authorization', `Bearer ${h}.${payload}.invalidsig`).expect(401);
  });

  it('path ids are validated before reaching the database', async () => {
    const p = await newGuest(app);
    const res = await http().get("/api/v1/businesses/1' OR '1'='1").set(p.auth).expect(400);
    expect(res.body.error.code).toBe('VALIDATION_FAILED');
  });
});
