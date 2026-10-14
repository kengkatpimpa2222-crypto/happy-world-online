import { z } from 'zod';

const bool = z
  .enum(['true', 'false', '1', '0'])
  .transform((v) => v === 'true' || v === '1');

export const envSchema = z.object({
  NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
  PORT: z.coerce.number().int().positive().default(3000),
  DATABASE_URL: z.string().min(1),
  REDIS_URL: z.string().optional(),
  JWT_ACCESS_SECRET: z.string().min(32, 'JWT_ACCESS_SECRET must be at least 32 characters'),
  JWT_ACCESS_TTL_SECONDS: z.coerce.number().int().positive().default(900),
  REFRESH_TOKEN_TTL_DAYS: z.coerce.number().int().positive().default(30),
  CORS_ORIGINS: z.string().default(''),
  TRUST_PROXY: bool.default(false),
  SWAGGER_ENABLED: bool.default(false),
  BILLING_MODE: z.enum(['disabled', 'mock', 'google_play']).default('disabled'),
  GOOGLE_PLAY_PACKAGE_NAME: z.string().optional(),
  GOOGLE_PLAY_SERVICE_ACCOUNT_JSON: z.string().optional(),
  RATE_LIMIT_DEFAULT_PER_MINUTE: z.coerce.number().int().positive().default(120),
  RATE_LIMIT_GUEST_PER_HOUR: z.coerce.number().int().positive().default(10),
  RATE_LIMIT_AUTH_PER_15_MIN: z.coerce.number().int().positive().default(20),
  RATE_LIMIT_FRIEND_REQUESTS_PER_HOUR: z.coerce.number().int().positive().default(30),
  RATE_LIMIT_REPORTS_PER_HOUR: z.coerce.number().int().positive().default(20),
  BUSINESS_SALE_COOLDOWN_SECONDS: z.coerce.number().int().min(0).default(30),
  /** How often open market stalls are settled in the background (NPC customers). 0 = only on demand. */
  LIVING_SHOP_SETTLE_INTERVAL_SECONDS: z.coerce.number().int().min(0).default(20),
});

export type Env = z.infer<typeof envSchema>;

export function validateEnv(raw: Record<string, unknown>): Env {
  const parsed = envSchema.safeParse(raw);
  if (!parsed.success) {
    const issues = parsed.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`).join('; ');
    throw new Error(`Invalid environment configuration: ${issues}`);
  }
  const env = parsed.data;
  if (env.NODE_ENV === 'production') {
    if (env.BILLING_MODE === 'mock') throw new Error('BILLING_MODE=mock is not allowed in production');
    if (/dev-only|test-only|change-me/i.test(env.JWT_ACCESS_SECRET)) {
      throw new Error('JWT_ACCESS_SECRET looks like a development placeholder');
    }
  }
  return env;
}
