import { INestApplication } from '@nestjs/common';
import { PrismaService } from '../src/prisma/prisma.service';
import { WalletService } from '../src/wallet/wallet.service';
import { createTestApp, grantGold, key, newGuest, TestPlayer } from './helpers';

describe('Wallet and store economy', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let wallet: WalletService;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];

  beforeAll(async () => {
    ({ app, prisma, http } = await createTestApp());
    wallet = app.get(WalletService);
  });
  afterAll(async () => app.close());

  const gold = async (p: TestPlayer) => (await http().get('/api/v1/player/wallet').set(p.auth).expect(200)).body.balances.GOLD as number;

  describe('WalletService (credit / debit)', () => {
    it('credits and debits with a full ledger record', async () => {
      const p = await newGuest(app);
      const credit = await prisma.$transaction((tx) =>
        wallet.applyChange(tx, { userId: p.userId, currency: 'GOLD', amount: 250, type: 'MINIGAME_REWARD', idempotencyKey: 'mg-1', referenceId: 'run-1' }),
      );
      expect(credit.transaction).toMatchObject({ amount: 250, balanceBefore: 500, balanceAfter: 750, type: 'MINIGAME_REWARD' });
      const debit = await prisma.$transaction((tx) =>
        wallet.applyChange(tx, { userId: p.userId, currency: 'GOLD', amount: -700, type: 'STORE_PURCHASE', idempotencyKey: 'buy-1' }),
      );
      expect(debit.transaction).toMatchObject({ amount: -700, balanceBefore: 750, balanceAfter: 50 });
      expect(await gold(p)).toBe(50);
    });

    it('never lets a balance go negative', async () => {
      const p = await newGuest(app);
      await expect(
        prisma.$transaction((tx) => wallet.applyChange(tx, { userId: p.userId, currency: 'GOLD', amount: -501, type: 'STORE_PURCHASE', idempotencyKey: 'over' })),
      ).rejects.toMatchObject({ code: 'INSUFFICIENT_FUNDS' });
      expect(await gold(p)).toBe(500);
    });

    it('same idempotency key twice moves money once', async () => {
      const p = await newGuest(app);
      const change = { userId: p.userId, currency: 'GOLD' as const, amount: 100, type: 'QUEST_REWARD' as const, idempotencyKey: 'same', referenceId: 'q' };
      const a = await prisma.$transaction((tx) => wallet.applyChange(tx, change));
      const b = await prisma.$transaction((tx) => wallet.applyChange(tx, change));
      expect(a.replayed).toBe(false);
      expect(b.replayed).toBe(true);
      expect(b.transaction.id).toBe(a.transaction.id);
      expect(await gold(p)).toBe(600);
    });

    it('20 parallel debits of 100 from 500 Gold: exactly 5 succeed', async () => {
      const p = await newGuest(app);
      const results = await Promise.allSettled(
        Array.from({ length: 20 }, (_, i) =>
          prisma.tx((tx) => wallet.applyChange(tx, { userId: p.userId, currency: 'GOLD', amount: -100, type: 'STORE_PURCHASE', idempotencyKey: `par-${i}` })),
        ),
      );
      expect(results.filter((r) => r.status === 'fulfilled')).toHaveLength(5);
      expect(await gold(p)).toBe(0);
      const ledgerSum = await prisma.walletTransaction.aggregate({ where: { userId: p.userId, currency: 'GOLD' }, _sum: { amount: true } });
      expect(ledgerSum._sum.amount).toBe(0); // ledger always reconciles with the balance
    });
  });

  describe('Store', () => {
    it('lists the catalog with server prices', async () => {
      const p = await newGuest(app);
      const res = await http().get('/api/v1/store/catalog').set(p.auth).expect(200);
      const table = res.body.find((i: { itemId: string }) => i.itemId === 'furn_wood_table');
      expect(table).toMatchObject({ priceGold: 120, priceGems: null, stackable: true });
      expect(res.body.find((i: { itemId: string }) => i.itemId === 'outfit_basic_tee')).toBeUndefined(); // not for sale
    });

    it('buys an item with Gold: debit + inventory in one step', async () => {
      const p = await newGuest(app);
      const res = await http().post('/api/v1/store/purchases').set(p.auth).set('Idempotency-Key', key()).send({ itemId: 'furn_wood_table', quantity: 2, currency: 'GOLD' }).expect(201);
      expect(res.body).toMatchObject({ replayed: false, amount: -240, balanceAfter: 260, item: { itemId: 'furn_wood_table', quantity: 2 } });
      expect(await gold(p)).toBe(260);
    });

    it('ignores any price the client sends', async () => {
      const p = await newGuest(app);
      await http().post('/api/v1/store/purchases').set(p.auth).set('Idempotency-Key', key()).send({ itemId: 'furn_wood_table', quantity: 1, currency: 'GOLD', price: 1 }).expect(400);
    });

    it('fails cleanly without enough Gold and changes nothing', async () => {
      const p = await newGuest(app);
      const res = await http().post('/api/v1/store/purchases').set(p.auth).set('Idempotency-Key', key()).send({ itemId: 'furn_single_bed', quantity: 3, currency: 'GOLD' }).expect(409);
      expect(res.body.error.code).toBe('INSUFFICIENT_FUNDS');
      expect(await gold(p)).toBe(500);
      const inv = await http().get('/api/v1/player/inventory').set(p.auth);
      expect(inv.body.find((i: { itemId: string }) => i.itemId === 'furn_single_bed')).toBeUndefined();
    });

    it('cannot buy gem-only items with Gold, or without enough Gems', async () => {
      const p = await newGuest(app);
      const a = await http().post('/api/v1/store/purchases').set(p.auth).set('Idempotency-Key', key()).send({ itemId: 'outfit_star_idol', currency: 'GOLD' }).expect(409);
      expect(a.body.error.code).toBe('ITEM_NOT_AVAILABLE');
      const b = await http().post('/api/v1/store/purchases').set(p.auth).set('Idempotency-Key', key()).send({ itemId: 'outfit_star_idol', currency: 'GEMS' }).expect(409);
      expect(b.body.error.code).toBe('INSUFFICIENT_FUNDS');
    });

    it('cosmetics can only be owned once (no accidental double buy)', async () => {
      const p = await newGuest(app);
      await http().post('/api/v1/store/purchases').set(p.auth).set('Idempotency-Key', key()).send({ itemId: 'acc_straw_hat', currency: 'GOLD' }).expect(201);
      const res = await http().post('/api/v1/store/purchases').set(p.auth).set('Idempotency-Key', key()).send({ itemId: 'acc_straw_hat', currency: 'GOLD' }).expect(409);
      expect(res.body.error.code).toBe('ITEM_NOT_AVAILABLE');
      expect(await gold(p)).toBe(420);
    });

    it('requires an Idempotency-Key header', async () => {
      const p = await newGuest(app);
      const res = await http().post('/api/v1/store/purchases').set(p.auth).send({ itemId: 'furn_plant_pot', currency: 'GOLD' }).expect(400);
      expect(res.body.error.code).toBe('IDEMPOTENCY_KEY_REQUIRED');
    });

    it('a retried request (same key) charges once and returns the same result', async () => {
      const p = await newGuest(app);
      const k = key();
      const body = { itemId: 'furn_plant_pot', quantity: 1, currency: 'GOLD' };
      const first = await http().post('/api/v1/store/purchases').set(p.auth).set('Idempotency-Key', k).send(body).expect(201);
      const second = await http().post('/api/v1/store/purchases').set(p.auth).set('Idempotency-Key', k).send(body).expect(201);
      expect(second.body.replayed).toBe(true);
      expect(second.body.transactionId).toBe(first.body.transactionId);
      expect(second.body.item.quantity).toBe(1);
      expect(await gold(p)).toBe(460);
    });

    it('10 parallel requests with the same key charge once', async () => {
      const p = await newGuest(app);
      const k = key();
      const res = await Promise.all(
        Array.from({ length: 10 }, () =>
          http().post('/api/v1/store/purchases').set(p.auth).set('Idempotency-Key', k).send({ itemId: 'furn_plant_pot', quantity: 1, currency: 'GOLD' }),
        ),
      );
      expect(res.every((r) => r.status === 201)).toBe(true);
      expect(new Set(res.map((r) => r.body.transactionId)).size).toBe(1);
      expect(await gold(p)).toBe(460);
      const inv = await prisma.playerInventory.findUnique({ where: { userId_itemId: { userId: p.userId, itemId: 'furn_plant_pot' } } });
      expect(inv?.quantity).toBe(1);
    });

    it('reusing a key for a different purchase is rejected', async () => {
      const p = await newGuest(app);
      const k = key();
      await http().post('/api/v1/store/purchases').set(p.auth).set('Idempotency-Key', k).send({ itemId: 'furn_plant_pot', currency: 'GOLD' }).expect(201);
      const res = await http().post('/api/v1/store/purchases').set(p.auth).set('Idempotency-Key', k).send({ itemId: 'furn_wood_table', currency: 'GOLD' }).expect(409);
      expect(res.body.error.code).toBe('IDEMPOTENCY_KEY_REUSED');
    });

    it('idempotency keys are scoped per player', async () => {
      const a = await newGuest(app);
      const b = await newGuest(app);
      await grantGold(prisma, b.userId, 0 + 1);
      const k = key();
      await http().post('/api/v1/store/purchases').set(a.auth).set('Idempotency-Key', k).send({ itemId: 'furn_plant_pot', currency: 'GOLD' }).expect(201);
      const res = await http().post('/api/v1/store/purchases').set(b.auth).set('Idempotency-Key', k).send({ itemId: 'furn_plant_pot', currency: 'GOLD' }).expect(201);
      expect(res.body.replayed).toBe(false);
    });
  });
});
