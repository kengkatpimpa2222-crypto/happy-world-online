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

// ---------- Phase 3 helpers ----------
import { io, Socket } from 'socket.io-client';

export async function friendCodeOf(app: INestApplication, p: TestPlayer): Promise<string> {
  const res = await request(await app.getUrl()).get('/api/v1/player/profile').set(p.auth).expect(200);
  return res.body.friendCode;
}

/** Connects to the realtime namespace and resolves after the server says `ready`. */
export async function connectWs(app: INestApplication, token: string | undefined): Promise<Socket> {
  const base = (await app.getUrl()).replace('[::1]', '127.0.0.1');
  const socket = io(`${base}/realtime`, { path: '/ws', auth: token ? { token } : {}, transports: ['websocket'], reconnection: false, forceNew: true });
  await new Promise<void>((resolve, reject) => {
    const t = setTimeout(() => reject(new Error('ws connect timeout')), 5000);
    socket.once('ready', () => {
      clearTimeout(t);
      resolve();
    });
    socket.once('connect_error', (e) => {
      clearTimeout(t);
      reject(e);
    });
  });
  return socket;
}

/** Waits for the next `event` matching `match`; rejects after `ms`. */
export function waitFor<T = Record<string, unknown>>(socket: Socket, event: string, match: (p: T) => boolean = () => true, ms = 3000): Promise<T> {
  return new Promise((resolve, reject) => {
    const t = setTimeout(() => {
      socket.off(event, handler);
      reject(new Error(`timeout waiting for ${event}`));
    }, ms);
    const handler = (p: T) => {
      if (!match(p)) return;
      clearTimeout(t);
      socket.off(event, handler);
      resolve(p);
    };
    socket.on(event, handler);
  });
}

/** Resolves true if `event` matching `match` arrives within `ms`, else false. */
export function receivedWithin<T = Record<string, unknown>>(socket: Socket, event: string, match: (p: T) => boolean = () => true, ms = 400): Promise<boolean> {
  return waitFor(socket, event, match, ms).then(
    () => true,
    () => false,
  );
}
