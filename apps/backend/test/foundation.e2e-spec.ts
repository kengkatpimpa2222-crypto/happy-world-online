import { execSync } from 'node:child_process';
import { join } from 'node:path';
import { INestApplication } from '@nestjs/common';
import { PrismaService } from '../src/prisma/prisma.service';
import { createTestApp } from './helpers';

describe('Phase 1 foundation: health + database migration', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];

  beforeAll(async () => ({ app, prisma, http } = await createTestApp()));
  afterAll(async () => app.close());

  it('GET /api/v1/health reports the database as up', async () => {
    const res = await http().get('/api/v1/health').expect(200);
    expect(res.body).toMatchObject({ status: 'ok', checks: { database: 'up', redis: process.env.REDIS_URL ? 'up' : 'disabled' } });
  });

  it('sets security headers and a request id, hides x-powered-by', async () => {
    const res = await http().get('/api/v1/health/live').expect(200);
    expect(res.headers['x-frame-options']).toBeDefined();
    expect(res.headers['x-content-type-options']).toBe('nosniff');
    expect(res.headers['x-request-id']).toBeDefined();
    expect(res.headers['x-powered-by']).toBeUndefined();
  });

  it('unknown routes return the standard error shape without stack traces', async () => {
    const res = await http().get('/api/v1/does-not-exist').expect(404);
    expect(res.body.error.code).toBe('NOT_FOUND');
    expect(JSON.stringify(res.body)).not.toMatch(/at .*\.ts:\d+/);
  });

  it('swagger docs are not exposed when disabled', async () => {
    await http().get('/api/docs').expect(404);
  });

  it('all migrations are applied and re-running deploy is a no-op', async () => {
    const out = execSync('npx prisma migrate deploy', { cwd: join(__dirname, '..'), env: process.env }).toString();
    expect(out).toMatch(/No pending migrations to apply/);
    const status = execSync('npx prisma migrate status', { cwd: join(__dirname, '..'), env: process.env }).toString();
    expect(status).toMatch(/Database schema is up to date/);
  });

  it('creates every required table', async () => {
    const rows = await prisma.$queryRaw<{ tablename: string }[]>`SELECT tablename FROM pg_tables WHERE schemaname='public'`;
    const names = rows.map((r) => r.tablename);
    for (const t of [
      'users', 'player_profiles', 'player_wallets', 'wallet_transactions', 'items', 'player_inventory',
      'player_homes', 'home_furniture', 'businesses', 'business_inventory', 'quests', 'quest_progress',
      'friends', 'friend_requests', 'game_sessions', 'purchases', 'purchase_events', 'subscriptions',
      'moderation_reports', 'blocked_players', 'audit_logs',
    ]) {
      expect(names).toContain(t);
    }
  });

  it('database CHECK constraint rejects a negative wallet balance even if app code tried', async () => {
    const user = await prisma.user.create({ data: { ageBand: 'ADULT_18_PLUS' } });
    await prisma.playerWallet.create({ data: { userId: user.id, currency: 'GOLD', balance: 10 } });
    await expect(
      prisma.playerWallet.update({ where: { userId_currency: { userId: user.id, currency: 'GOLD' } }, data: { balance: -1 } }),
    ).rejects.toThrow(/player_wallets_balance_non_negative/);
  });

  it('wallet ledger rows cannot be edited or deleted', async () => {
    const user = await prisma.user.create({ data: { ageBand: 'ADULT_18_PLUS' } });
    const w = await prisma.playerWallet.create({ data: { userId: user.id, currency: 'GOLD', balance: 5 } });
    const tx = await prisma.walletTransaction.create({
      data: { userId: user.id, walletId: w.id, currency: 'GOLD', type: 'ADMIN_ADJUSTMENT', amount: 5, balanceBefore: 0, balanceAfter: 5, idempotencyKey: 'ledger-test' },
    });
    await expect(prisma.walletTransaction.update({ where: { id: tx.id }, data: { amount: 500 } })).rejects.toThrow(/append-only/);
    await expect(prisma.walletTransaction.delete({ where: { id: tx.id } })).rejects.toThrow(/append-only/);
  });
});
