import { validateEnv } from './env';

const base = { DATABASE_URL: 'postgresql://x', JWT_ACCESS_SECRET: 'a'.repeat(40) };

describe('validateEnv', () => {
  it('applies safe defaults', () => {
    const env = validateEnv(base);
    expect(env).toMatchObject({ NODE_ENV: 'development', PORT: 3000, SWAGGER_ENABLED: false, BILLING_MODE: 'disabled' });
  });

  it('rejects short JWT secrets', () => {
    expect(() => validateEnv({ ...base, JWT_ACCESS_SECRET: 'short' })).toThrow(/JWT_ACCESS_SECRET/);
  });

  it('refuses mock billing and placeholder secrets in production', () => {
    expect(() => validateEnv({ ...base, NODE_ENV: 'production', BILLING_MODE: 'mock' })).toThrow(/mock/);
    expect(() => validateEnv({ ...base, NODE_ENV: 'production', JWT_ACCESS_SECRET: 'dev-only-' + 'x'.repeat(30) })).toThrow(/placeholder/);
  });

  it('refuses a shifted daily-reward clock in production (Phase 11)', () => {
    expect(validateEnv({ ...base, DAILY_CLOCK_OFFSET_HOURS: '48' }).DAILY_CLOCK_OFFSET_HOURS).toBe(48);
    expect(() => validateEnv({ ...base, NODE_ENV: 'production', DAILY_CLOCK_OFFSET_HOURS: '1' })).toThrow(/DAILY_CLOCK_OFFSET_HOURS/);
  });
});
