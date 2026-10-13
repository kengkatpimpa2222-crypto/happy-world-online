import { execSync } from 'node:child_process';
import { join } from 'node:path';
import { PrismaClient } from '@prisma/client';
import Redis from 'ioredis';
import { loadTestEnv } from './load-test-env';
import { seed } from '../src/database/seed';

/**
 * Prepares the ISOLATED test database: apply migrations, wipe test data, seed reference data.
 * Refuses to run against any database whose name does not end in `_test`.
 */
export default async function globalSetup(): Promise<void> {
  loadTestEnv();
  const url = new URL(process.env.DATABASE_URL!);
  const dbName = url.pathname.replace(/^\//, '');
  if (!dbName.endsWith('_test')) {
    throw new Error(`Refusing to run integration tests against non-test database "${dbName}"`);
  }

  execSync('npx prisma migrate deploy', { cwd: join(__dirname, '..'), env: process.env, stdio: 'pipe' });

  const prisma = new PrismaClient();
  try {
    const tables = await prisma.$queryRaw<{ tablename: string }[]>`
      SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename <> '_prisma_migrations'`;
    const list = tables.map((t) => `"public"."${t.tablename}"`).join(', ');
    // TRUNCATE does not fire the ledger's row-level append-only trigger.
    if (list) await prisma.$executeRawUnsafe(`TRUNCATE ${list} RESTART IDENTITY CASCADE`);
    await seed(prisma);
  } finally {
    await prisma.$disconnect();
  }

  if (process.env.REDIS_URL) {
    const db = new URL(process.env.REDIS_URL).pathname.replace('/', '');
    if (db !== '15') throw new Error('HWO_TEST_REDIS_URL must use Redis database 15 (it is flushed before tests)');
    const redis = new Redis(process.env.REDIS_URL);
    await redis.flushdb();
    await redis.quit();
  }
}
