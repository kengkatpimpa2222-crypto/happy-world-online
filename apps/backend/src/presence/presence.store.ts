import type Redis from 'ioredis';

export interface PresenceEntry {
  userId: string;
  socketId: string;
  ageBand: string;
  displayName: string;
  level: number;
  avatar: unknown;
  /** null = connected but not in a city zone */
  zone: string | null;
  room: string | null;
  x: number;
  y: number;
  dir: number;
  lastMoveAt: number;
  lastSeen: number;
}

/**
 * Ephemeral "who is online and where". Never the source of truth for anything valuable,
 * so losing it (Redis restart) only means players re-join their zone.
 */
export interface PresenceStore {
  upsert(entry: PresenceEntry, ttlSeconds: number): Promise<void>;
  get(userId: string): Promise<PresenceEntry | null>;
  getMany(userIds: string[]): Promise<Map<string, PresenceEntry>>;
  /** Removes only if the entry still belongs to `socketId` (a newer connection wins). */
  remove(userId: string, socketId: string): Promise<PresenceEntry | null>;
  listRoom(room: string): Promise<PresenceEntry[]>;
}

export class MemoryPresenceStore implements PresenceStore {
  private readonly users = new Map<string, { entry: PresenceEntry; expiresAt: number }>();

  async upsert(entry: PresenceEntry, ttlSeconds: number): Promise<void> {
    this.users.set(entry.userId, { entry: { ...entry }, expiresAt: Date.now() + ttlSeconds * 1000 });
  }

  async get(userId: string): Promise<PresenceEntry | null> {
    const hit = this.users.get(userId);
    if (!hit) return null;
    if (hit.expiresAt < Date.now()) {
      this.users.delete(userId);
      return null;
    }
    return { ...hit.entry };
  }

  async getMany(userIds: string[]): Promise<Map<string, PresenceEntry>> {
    const out = new Map<string, PresenceEntry>();
    for (const id of userIds) {
      const e = await this.get(id);
      if (e) out.set(id, e);
    }
    return out;
  }

  async remove(userId: string, socketId: string): Promise<PresenceEntry | null> {
    const e = await this.get(userId);
    if (!e || e.socketId !== socketId) return null;
    this.users.delete(userId);
    return e;
  }

  async listRoom(room: string): Promise<PresenceEntry[]> {
    const out: PresenceEntry[] = [];
    for (const id of [...this.users.keys()]) {
      const e = await this.get(id);
      if (e && e.room === room) out.push(e);
    }
    return out;
  }
}

/** Redis layout: hwo:presence:u:<userId> = JSON (with TTL); hwo:presence:room:<room> = SET of userIds. */
export class RedisPresenceStore implements PresenceStore {
  constructor(private readonly redis: Redis) {}

  private userKey(id: string) {
    return `hwo:presence:u:${id}`;
  }
  private roomKey(room: string) {
    return `hwo:presence:room:${room}`;
  }

  async upsert(entry: PresenceEntry, ttlSeconds: number): Promise<void> {
    const prev = await this.get(entry.userId);
    const multi = this.redis.multi().set(this.userKey(entry.userId), JSON.stringify(entry), 'EX', ttlSeconds);
    if (prev?.room && prev.room !== entry.room) multi.srem(this.roomKey(prev.room), entry.userId);
    if (entry.room) multi.sadd(this.roomKey(entry.room), entry.userId).expire(this.roomKey(entry.room), ttlSeconds * 10);
    await multi.exec();
  }

  async get(userId: string): Promise<PresenceEntry | null> {
    const raw = await this.redis.get(this.userKey(userId));
    return raw ? (JSON.parse(raw) as PresenceEntry) : null;
  }

  async getMany(userIds: string[]): Promise<Map<string, PresenceEntry>> {
    const out = new Map<string, PresenceEntry>();
    if (userIds.length === 0) return out;
    const raws = await this.redis.mget(userIds.map((id) => this.userKey(id)));
    raws.forEach((raw, i) => {
      if (raw) out.set(userIds[i]!, JSON.parse(raw) as PresenceEntry);
    });
    return out;
  }

  async remove(userId: string, socketId: string): Promise<PresenceEntry | null> {
    const e = await this.get(userId);
    if (!e || e.socketId !== socketId) return null;
    const multi = this.redis.multi().del(this.userKey(userId));
    if (e.room) multi.srem(this.roomKey(e.room), userId);
    await multi.exec();
    return e;
  }

  async listRoom(room: string): Promise<PresenceEntry[]> {
    const ids = await this.redis.smembers(this.roomKey(room));
    const found = await this.getMany(ids);
    const stale = ids.filter((id) => found.get(id)?.room !== room);
    if (stale.length) await this.redis.srem(this.roomKey(room), ...stale);
    return [...found.values()].filter((e) => e.room === room);
  }
}
