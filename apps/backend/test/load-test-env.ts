import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/** Loads apps/backend/.env.test into process.env (test values always win). */
export function loadTestEnv(): void {
  const text = readFileSync(join(__dirname, '..', '.env.test'), 'utf8');
  for (const line of text.split('\n')) {
    const m = /^\s*([A-Z0-9_]+)\s*=\s*(.*)\s*$/.exec(line);
    if (m) process.env[m[1]!] = m[2]!;
  }
  // Empty (not deleted) so Prisma's own .env loader cannot fill it in: tests use in-memory rate limiting.
  process.env.REDIS_URL = '';
}

loadTestEnv();
