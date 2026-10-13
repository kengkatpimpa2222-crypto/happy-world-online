import { ThrottlerStorage } from '@nestjs/throttler';
import type Redis from 'ioredis';

// KEYS[1] hit counter, KEYS[2] block marker; ARGV ttlMs, limit, blockMs
const SCRIPT = `
local hits = redis.call('INCR', KEYS[1])
if hits == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end
local ttl = redis.call('PTTL', KEYS[1])
local blocked = redis.call('PTTL', KEYS[2])
if blocked <= 0 and hits > tonumber(ARGV[2]) then
  redis.call('SET', KEYS[2], '1', 'PX', ARGV[3])
  blocked = tonumber(ARGV[3])
end
return {hits, ttl, blocked}
`;

/** Shared rate-limit counters so limits hold across multiple backend instances. */
export class RedisThrottlerStorage implements ThrottlerStorage {
  constructor(private readonly redis: Redis) {}

  async increment(key: string, ttl: number, limit: number, blockDuration: number, throttlerName: string) {
    const base = `hwo:throttle:${throttlerName}:${key}`;
    const [hits, ttlMs, blockedMs] = (await this.redis.eval(SCRIPT, 2, base, `${base}:blocked`, ttl, limit, blockDuration || ttl)) as [
      number,
      number,
      number,
    ];
    return {
      totalHits: hits,
      timeToExpire: Math.max(0, Math.ceil(ttlMs / 1000)),
      isBlocked: blockedMs > 0,
      timeToBlockExpire: Math.max(0, Math.ceil(blockedMs / 1000)),
    };
  }
}
