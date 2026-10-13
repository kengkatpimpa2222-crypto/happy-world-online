import { INestApplicationContext, Logger } from '@nestjs/common';
import { IoAdapter } from '@nestjs/platform-socket.io';
import { createAdapter } from '@socket.io/redis-adapter';
import type Redis from 'ioredis';
import { WS_PATH } from '@hwo/shared-contracts';
import type { Server, ServerOptions } from 'socket.io';
import { AppConfig } from '../config/app-config.service';
import { RedisService } from '../redis/redis.service';

/**
 * Socket.IO server settings in one place: path, CORS allow-list, small payload limit and,
 * when Redis is configured, the Redis adapter so events reach players on every backend instance.
 */
export class AppIoAdapter extends IoAdapter {
  private readonly log = new Logger('AppIoAdapter');
  private readonly redisClients: Redis[] = [];

  constructor(private readonly app: INestApplicationContext) {
    super(app);
  }

  override createIOServer(port: number, options?: ServerOptions): Server {
    const config = this.app.get(AppConfig);
    const origins = config
      .get('CORS_ORIGINS')
      .split(',')
      .map((o) => o.trim())
      .filter(Boolean);
    const server = super.createIOServer(port, {
      ...options,
      path: WS_PATH,
      serveClient: false,
      maxHttpBufferSize: 4 * 1024,
      pingInterval: 20_000,
      pingTimeout: 20_000,
      cors: { origin: origins, credentials: false },
    }) as Server;

    const redis = this.app.get(RedisService);
    if (redis.client) {
      const pub = redis.client.duplicate();
      const sub = redis.client.duplicate();
      this.redisClients.push(pub, sub);
      server.adapter(createAdapter(pub, sub, { key: 'hwo:socket.io' }));
      this.log.log('Socket.IO Redis adapter enabled');
    }
    return server;
  }

  /** Close the adapter's Redis connections on shutdown so the process can exit cleanly. */
  override async dispose(): Promise<void> {
    await super.dispose();
    await Promise.all(this.redisClients.splice(0).map((c) => c.quit().catch(() => undefined)));
  }
}
