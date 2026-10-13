import { Injectable } from '@nestjs/common';
import { PRESENCE_TTL_SECONDS } from '@hwo/shared-contracts';
import { RedisService } from '../redis/redis.service';
import { MemoryPresenceStore, PresenceEntry, PresenceStore, RedisPresenceStore } from './presence.store';

/** Redis-backed when REDIS_URL is set (required for more than one backend instance), else in-memory. */
@Injectable()
export class PresenceService {
  private readonly store: PresenceStore;

  constructor(redis: RedisService) {
    this.store = redis.client ? new RedisPresenceStore(redis.client) : new MemoryPresenceStore();
  }

  upsert(entry: PresenceEntry) {
    return this.store.upsert({ ...entry, lastSeen: Date.now() }, PRESENCE_TTL_SECONDS);
  }
  get(userId: string) {
    return this.store.get(userId);
  }
  getMany(userIds: string[]) {
    return this.store.getMany(userIds);
  }
  remove(userId: string, socketId: string) {
    return this.store.remove(userId, socketId);
  }
  listRoom(room: string) {
    return this.store.listRoom(room);
  }
}
