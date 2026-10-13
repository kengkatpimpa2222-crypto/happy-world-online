import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/** Loads apps/backend/.env.test into process.env (test values always win). */
export function loadTestEnv(): void {
  const text = readFileSync(join(__dirname, '..', '.env.test'), 'utf8');
  for (const line of text.split('\n')) {
    const m = /^\s*([A-Z0-9_]+)\s*=\s*(.*)\s*$/.exec(line);
    if (m) process.env[m[1]!] = m[2]!;
  }
  // Empty (not deleted) so Prisma's own .env loader cannot fill it in: by default tests use in-memory state.
  // Set HWO_TEST_REDIS_URL (e.g. redis://localhost:6379/15) to run the same suite against Redis.
  process.env.REDIS_URL = process.env.HWO_TEST_REDIS_URL ?? '';
}

loadTestEnv();
