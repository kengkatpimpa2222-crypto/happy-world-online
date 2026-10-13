import { Injectable, Logger, OnModuleDestroy } from '@nestjs/common';
import Redis from 'ioredis';
import { AppConfig } from '../config/app-config.service';

/** Optional Redis connection. When REDIS_URL is not set, features fall back to in-memory. */
@Injectable()
export class RedisService implements OnModuleDestroy {
  private readonly logger = new Logger('Redis');
  readonly client: Redis | null;

  constructor(config: AppConfig) {
    const url = config.get('REDIS_URL');
    this.client = url ? new Redis(url, { maxRetriesPerRequest: 2, lazyConnect: false }) : null;
    this.client?.on('error', (e) => this.logger.warn(`Redis error: ${e.message}`));
  }

  get enabled(): boolean {
    return this.client !== null;
  }

  async ping(): Promise<boolean> {
    if (!this.client) return false;
    try {
      return (await this.client.ping()) === 'PONG';
    } catch {
      return false;
    }
  }

  async onModuleDestroy(): Promise<void> {
    await this.client?.quit().catch(() => undefined);
  }
}
