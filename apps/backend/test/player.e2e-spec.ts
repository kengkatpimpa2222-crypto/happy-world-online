import { INestApplication } from '@nestjs/common';
import { createTestApp, newGuest, TestPlayer } from './helpers';

describe('Player profile, wallet, inventory', () => {
  let app: INestApplication;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];
  let p: TestPlayer;

  beforeAll(async () => {
    ({ app, http } = await createTestApp());
    p = await newGuest(app);
  });
  afterAll(async () => app.close());

  it('reads the profile of the token owner', async () => {
    const res = await http().get('/api/v1/player/profile').set(p.auth).expect(200);
    expect(res.body).toMatchObject({ userId: p.userId, level: 1, xp: 0, isGuest: true, ageBand: 'TEEN_13_17' });
    expect(res.body.friendCode).toMatch(/^[A-Z2-9]{8}$/);
  });

  it('updates name and avatar with owned items', async () => {
    const res = await http()
      .patch('/api/v1/player/profile')
      .set(p.auth)
      .send({ displayName: 'น้องแมว_01', avatar: { bodyColor: '#AABBCC', hairItemId: 'hair_short_brown' } })
      .expect(200);
    expect(res.body.displayName).toBe('น้องแมว_01');
    expect(res.body.avatar).toMatchObject({ bodyColor: '#AABBCC', hairItemId: 'hair_short_brown', outfitItemId: 'outfit_basic_tee' });
  });

  it('refuses to wear an item the player does not own', async () => {
    const res = await http().patch('/api/v1/player/profile').set(p.auth).send({ avatar: { outfitItemId: 'outfit_star_idol' } }).expect(409);
    expect(res.body.error.code).toBe('ITEM_NOT_OWNED');
  });

  it('rejects names that leak personal info or are inappropriate', async () => {
    await http().patch('/api/v1/player/profile').set(p.auth).send({ displayName: 'call 0812345678' }).expect(400);
    await http().patch('/api/v1/player/profile').set(p.auth).send({ displayName: 'me@mail.com' }).expect(400);
    const res = await http().patch('/api/v1/player/profile').set(p.auth).send({ displayName: 'Admin Tom' }).expect(422);
    expect(res.body.error.code).toBe('DISPLAY_NAME_REJECTED');
  });

  it('ignores attempts to change other fields such as level or another userId', async () => {
    await http().patch('/api/v1/player/profile').set(p.auth).send({ level: 99 }).expect(400);
    await http().patch('/api/v1/player/profile').set(p.auth).send({ userId: '00000000-0000-0000-0000-000000000000', displayName: 'Hacker' }).expect(400);
  });

  it('reads wallet balances and the signup ledger entry', async () => {
    const res = await http().get('/api/v1/player/wallet').set(p.auth).expect(200);
    expect(res.body.balances).toEqual({ GOLD: 500, GEMS: 0 });
    expect(res.body.recentTransactions[0]).toMatchObject({ type: 'SIGNUP_BONUS', amount: 500, balanceBefore: 0, balanceAfter: 500 });
  });

  it('lists inventory', async () => {
    const res = await http().get('/api/v1/player/inventory').set(p.auth).expect(200);
    expect(res.body.map((i: { itemId: string }) => i.itemId)).toEqual(expect.arrayContaining(['outfit_basic_tee', 'furn_wood_chair']));
  });
});
