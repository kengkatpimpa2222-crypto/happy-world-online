import { INestApplication } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { AppModule } from '../src/app.module';
import { configureApp } from '../src/app-setup';
import { PrismaService } from '../src/prisma/prisma.service';

export async function createTestApp(): Promise<{ app: INestApplication; prisma: PrismaService; http: () => request.Agent }> {
  const moduleRef = await Test.createTestingModule({ imports: [AppModule] }).compile();
  const app = moduleRef.createNestApplication({ bodyParser: false, logger: ['error'] });
  configureApp(app);
  await app.listen(0, '127.0.0.1');
  const base = await app.getUrl();
  return { app, prisma: app.get(PrismaService), http: () => request.agent(base.replace('[::1]', '127.0.0.1')) };
}

export interface TestPlayer {
  userId: string;
  accessToken: string;
  refreshToken: string;
  auth: { Authorization: string };
}

export async function newGuest(app: INestApplication, ageBand = 'TEEN_13_17'): Promise<TestPlayer> {
  const res = await request(await app.getUrl())
    .post('/api/v1/auth/guest')
    .send({ deviceId: `test-device-${randomUUID()}`, ageBand })
    .expect(201);
  return {
    userId: res.body.userId,
    accessToken: res.body.accessToken,
    refreshToken: res.body.refreshToken,
    auth: { Authorization: `Bearer ${res.body.accessToken}` },
  };
}

export const key = () => randomUUID();

/** Test-only shortcut to give a player money, written through the real ledger. */
export async function grantGold(prisma: PrismaService, userId: string, amount: number): Promise<void> {
  await prisma.$transaction(async (tx) => {
    const w = await tx.playerWallet.update({ where: { userId_currency: { userId, currency: 'GOLD' } }, data: { balance: { increment: amount } } });
    await tx.walletTransaction.create({
      data: { userId, walletId: w.id, currency: 'GOLD', type: 'ADMIN_ADJUSTMENT', amount, balanceBefore: w.balance - amount, balanceAfter: w.balance, idempotencyKey: `test-grant:${randomUUID()}` },
    });
  });
}
