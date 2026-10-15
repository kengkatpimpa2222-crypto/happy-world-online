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
  /**
   * Dev/QA only: shifts the festival calendar (hours) so a festival can be tried on any weekday.
   * Must be 0 in production.
   */
  FESTIVAL_CLOCK_OFFSET_HOURS: z.coerce.number().min(-24 * 7 * 52).max(24 * 7 * 52).default(0),
  /** Dev/QA only (Phase 7): force the city weather (SUNNY | HOT | CLOUDY | RAIN). Empty = real weather. */
  CITY_WEATHER_OVERRIDE: z.preprocess((v) => (v === '' ? undefined : v), z.enum(['SUNNY', 'HOT', 'CLOUDY', 'RAIN']).optional()),
  /** Dev/QA only (Phase 9): expedition length multiplier, e.g. 0.05 turns a 3-minute trip into 9 s. Must be 1 in production. */
  EXPEDITION_TIME_SCALE: z.coerce.number().min(0.001).max(1).default(1),
  /** Dev/QA only (Phase 11): shift the daily-reward clock (check-in day, mission day/week). Must be 0 in production. */
  DAILY_CLOCK_OFFSET_HOURS: z.coerce.number().min(-24 * 400).max(24 * 400).default(0),
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
    if (env.FESTIVAL_CLOCK_OFFSET_HOURS !== 0) throw new Error('FESTIVAL_CLOCK_OFFSET_HOURS must be 0 in production');
    if (env.CITY_WEATHER_OVERRIDE) throw new Error('CITY_WEATHER_OVERRIDE is not allowed in production');
    if (env.EXPEDITION_TIME_SCALE !== 1) throw new Error('EXPEDITION_TIME_SCALE must be 1 in production');
    if (env.DAILY_CLOCK_OFFSET_HOURS !== 0) throw new Error('DAILY_CLOCK_OFFSET_HOURS must be 0 in production');
  }
  return env;
}
