import { INestApplication } from '@nestjs/common';
import { RedisService } from '../src/redis/redis.service';
import { createTestApp } from './helpers';

describe('Rate limiting', () => {
  let app: INestApplication;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];

  // With Redis the counters are shared by every test file (same IP): start and leave them clean.
  const clearSharedCounters = async () => {
    const redis = app.get(RedisService).client;
    const keys = redis ? await redis.keys('hwo:throttle:*') : [];
    if (keys.length) await redis!.del(...keys);
  };

  beforeAll(async () => {
    process.env.RATE_LIMIT_GUEST_PER_HOUR = '2'; // read lazily per request by the guest route
    ({ app, http } = await createTestApp());
    await clearSharedCounters();
  });
  afterAll(async () => {
    await clearSharedCounters();
    await app.close();
  });

  it('limits guest account creation per IP', async () => {
    const body = { deviceId: 'rate-limit-device', ageBand: 'ADULT_18_PLUS' };
    await http().post('/api/v1/auth/guest').send(body).expect(201);
    await http().post('/api/v1/auth/guest').send(body).expect(201);
    const res = await http().post('/api/v1/auth/guest').send(body).expect(429);
    expect(res.body.error.code).toBe('RATE_LIMITED');
    expect(res.headers['retry-after']).toBeDefined();
  });
});
