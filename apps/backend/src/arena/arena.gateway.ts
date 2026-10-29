import { Logger, OnModuleDestroy } from '@nestjs/common';
import {
  ConnectedSocket,
  MessageBody,
  OnGatewayDisconnect,
  OnGatewayInit,
  SubscribeMessage,
  WebSocketGateway,
  WebSocketServer,
} from '@nestjs/websockets';
import {
  ARENA_HEIGHT,
  ARENA_HITS_TO_SOAK,
  ARENA_TEAMS,
  ARENA_THROW_COOLDOWN_MS,
  ARENA_THROW_RANGE,
  ARENA_WIDTH,
  ArenaEvent,
  ArenaTeam,
  ErrorCode,
  WS_NAMESPACE,
  WsEvent,
  wsArenaMoveSchema,
  wsArenaThrowSchema,
} from '@hwo/shared-contracts';
import { randomInt, randomUUID } from 'node:crypto';
import type { Namespace, Socket } from 'socket.io';
import { z } from 'zod';
import { AppException } from '../common/app-exception';
import { AuthUser } from '../common/decorators';
import { AppConfig } from '../config/app-config.service';
import { PresenceService } from '../presence/presence.service';
import { userRoom } from '../realtime/realtime.service';
import { planMatches, PlannedMatch, QueuedPlayer } from './arena-matchmaking';
import { ArenaSim, seededRandom } from './arena-sim';
import { ArenaService, MatchSheet } from './arena.service';

/** What CityGateway's handshake puts on every socket of the namespace. */
interface SocketData {
  user: AuthUser;
  profile: { displayName: string; level: number; avatar: unknown };
  blocked: Set<string>;
  arenaBuckets?: Record<string, { tokens: number; at: number }>;
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type AnyEvents = Record<string, (...args: any[]) => void>;
type AppSocket = Socket<AnyEvents, AnyEvents, AnyEvents, SocketData>;
type Ack = { ok: true; [k: string]: unknown } | { ok: false; error: { code: string; message: string; [k: string]: unknown } };

interface Waiting extends QueuedPlayer {
  socketId: string;
}

interface LiveMatch {
  id: string;
  room: string;
  ageBand: AuthUser['ageBand'];
  sim: ArenaSim;
  withBots: boolean;
  teamAvg: Record<ArenaTeam, number>;
  humans: Map<string, { rating: number; socketId: string; team: ArenaTeam }>;
  createdAt: number;
}

const TICK_MS = 200;
/** Per-socket token buckets: [refill per second, burst]. */
const LIMITS = { queue: [1, 3], move: [20, 30], throw: [4, 4], leave: [1, 3] } as const;
const BOT_NAMES = ['น้องเป็ดน้ำ', 'น้องปลาทอง', 'น้องเต่าทะเล', 'น้องแมวน้ำ', 'น้องนากน้อย', 'น้องกบเขียว'];
const arenaRoom = (id: string) => `arena:${id}`;

/**
 * Splash Arena (Phase 13), on the same Socket.IO namespace as the city (CityGateway authenticates the socket).
 * Queue and matches live in this process; each match is an ArenaSim stepped every TICK_MS, and the result is
 * written once by ArenaService.record. Players only ever reach the arena from the queue, so nobody in the open
 * city can be attacked.
 *
 * Known limit: with several backend instances, players are matched with others connected to the same instance.
 */
@WebSocketGateway({ namespace: WS_NAMESPACE })
export class ArenaGateway implements OnGatewayInit, OnGatewayDisconnect, OnModuleDestroy {
  private readonly logger = new Logger('ArenaGateway');
  @WebSocketServer() private ns!: Namespace;
  private readonly queue = new Map<string, Waiting>();
  private readonly matches = new Map<string, LiveMatch>();
  private readonly playerMatch = new Map<string, string>();
  private timer: NodeJS.Timeout | null = null;
  private ticking = false;

  constructor(
    private readonly arena: ArenaService,
    private readonly presence: PresenceService,
    private readonly config: AppConfig,
  ) {}

  afterInit(): void {
    this.timer = setInterval(() => void this.safely('tick', () => this.tick()), TICK_MS);
    this.timer.unref();
  }

  onModuleDestroy(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
  }

  handleDisconnect(socket: AppSocket): Promise<void> {
    return this.safely('disconnect', async () => {
      const userId = socket.data?.user?.userId;
      if (!userId) return;
      const w = this.queue.get(userId);
      if (w?.socketId === socket.id) this.queue.delete(userId);
      const m = this.matchOf(userId);
      if (m && m.humans.get(userId)?.socketId === socket.id) this.leaveMatch(m, userId);
    });
  }

  @SubscribeMessage(ArenaEvent.QUEUE)
  queueUp(@ConnectedSocket() socket: AppSocket): Promise<Ack> {
    return this.handle(socket, 'queue', z.any(), undefined, async () => {
      const { user, blocked } = socket.data;
      if (this.matchOf(user.userId)) throw AppException.conflict(ErrorCode.ARENA_IN_MATCH, 'You are already in a match');
      const standing = await this.arena.current(user);
      const existing = this.queue.get(user.userId);
      this.queue.set(user.userId, {
        userId: user.userId,
        ageBand: user.ageBand,
        rating: standing.rating,
        // re-queueing (e.g. after a reconnect) keeps your place
        joinedAt: existing?.joinedAt ?? Date.now(),
        blocked,
        socketId: socket.id,
      });
      const status = this.queueStatus(user.userId);
      socket.emit(ArenaEvent.QUEUED, status);
      return status;
    });
  }

  @SubscribeMessage(ArenaEvent.CANCEL)
  cancel(@ConnectedSocket() socket: AppSocket): Promise<Ack> {
    return this.handle(socket, 'queue', z.any(), undefined, async () => {
      const removed = this.queue.delete(socket.data.user.userId);
      return { removed };
    });
  }

  @SubscribeMessage(ArenaEvent.MOVE)
  move(@ConnectedSocket() socket: AppSocket, @MessageBody() body: unknown): Promise<Ack> {
    return this.handle(socket, 'move', wsArenaMoveSchema, body, async (m) => {
      const { userId } = socket.data.user;
      const match = this.requireMatch(socket);
      if (!match.sim.move(userId, m.x, m.y, Date.now())) {
        const f = match.sim.fighters.get(userId)!;
        throw AppException.badRequest(ErrorCode.MOVE_REJECTED, 'Move rejected', { x: f.x, y: f.y, seq: m.seq });
      }
      socket.to(match.room).volatile.emit(ArenaEvent.MOVED, { id: userId, x: m.x, y: m.y, seq: m.seq });
      return { x: m.x, y: m.y };
    });
  }

  @SubscribeMessage(ArenaEvent.THROW)
  throwBalloon(@ConnectedSocket() socket: AppSocket, @MessageBody() body: unknown): Promise<Ack> {
    return this.handle(socket, 'throw', wsArenaThrowSchema, body, async ({ targetId }) => {
      const match = this.requireMatch(socket);
      const r = match.sim.throw(socket.data.user.userId, targetId, Date.now());
      if (typeof r === 'string') throw AppException.conflict(ErrorCode.ARENA_THROW_REFUSED, 'Throw refused', { reason: r });
      this.ns.to(match.room).emit(ArenaEvent.THROWN, r);
      return { ...r };
    });
  }

  @SubscribeMessage(ArenaEvent.LEAVE)
  leave(@ConnectedSocket() socket: AppSocket): Promise<Ack> {
    return this.handle(socket, 'leave', z.any(), undefined, async () => {
      const { userId } = socket.data.user;
      this.queue.delete(userId);
      const match = this.matchOf(userId);
      if (match) this.leaveMatch(match, userId);
      return { left: !!match };
    });
  }

  // ---------- match lifecycle ----------

  private async tick(): Promise<void> {
    // a slow database write must not let two ticks finish the same match
    if (this.ticking) return;
    this.ticking = true;
    try {
      await this.step(Date.now());
    } finally {
      this.ticking = false;
    }
  }

  private async step(now: number): Promise<void> {
    this.dropStaleQueue();
    // block lists are read live from the socket: a block made while waiting counts too
    const waiting = [...this.queue.values()].map((w) => ({ ...w, blocked: (this.ns.sockets.get(w.socketId) as AppSocket | undefined)?.data.blocked ?? w.blocked }));
    for (const plan of planMatches(waiting, now, this.config.get('ARENA_BOT_FILL_SECONDS') * 1000)) {
      await this.safely('start', () => this.startMatch(plan, now));
    }
    for (const m of [...this.matches.values()]) {
      const ev = m.sim.tick(now);
      for (const mv of ev.moved) this.ns.to(m.room).volatile.emit(ArenaEvent.MOVED, mv);
      for (const t of ev.thrown) this.ns.to(m.room).emit(ArenaEvent.THROWN, t);
      for (const r of ev.respawned) this.ns.to(m.room).emit(ArenaEvent.RESPAWN, r);
      if (m.sim.isOver(now)) await this.safely('finish', () => this.finishMatch(m, now));
    }
  }

  private async startMatch(plan: PlannedMatch, now: number): Promise<void> {
    const humans = [...plan.teams.RED, ...plan.teams.BLUE];
    humans.forEach((p) => this.queue.delete(p.userId));
    const sockets = new Map<string, AppSocket>();
    for (const p of humans) {
      const s = this.ns.sockets.get((p as Waiting).socketId) as AppSocket | undefined;
      if (s?.connected) sockets.set(p.userId, s);
    }
    // someone vanished between queueing and now: put the rest back in line, keeping their place
    if (sockets.size !== humans.length) {
      for (const p of humans) if (sockets.has(p.userId)) this.queue.set(p.userId, p as Waiting);
      return;
    }

    const id = randomUUID();
    const avg = Math.round(humans.reduce((s, p) => s + p.rating, 0) / humans.length);
    const roster: { id: string; team: ArenaTeam; isBot: boolean }[] = [];
    const teamAvg = { RED: 0, BLUE: 0 } as Record<ArenaTeam, number>;
    const names = new Map<string, string>();
    let botNo = 0;
    for (const team of ARENA_TEAMS) {
      const ratings = plan.teams[team].map((p) => p.rating);
      for (const p of plan.teams[team]) roster.push({ id: p.userId, team, isBot: false });
      for (let i = 0; i < plan.bots[team]; i++) {
        const botId = `bot:${id.slice(0, 8)}:${botNo}`;
        names.set(botId, BOT_NAMES[botNo++ % BOT_NAMES.length]!);
        roster.push({ id: botId, team, isBot: true });
        ratings.push(avg);
      }
      teamAvg[team] = ratings.reduce((s, r) => s + r, 0) / ratings.length;
    }
    const startsAt = now + this.config.get('ARENA_COUNTDOWN_SECONDS') * 1000;
    const endsAt = startsAt + this.config.get('ARENA_MATCH_SECONDS') * 1000;
    const sim = new ArenaSim(startsAt, endsAt, roster, seededRandom(randomInt(1, 2 ** 31)));
    const match: LiveMatch = {
      id,
      room: arenaRoom(id),
      ageBand: plan.ageBand as AuthUser['ageBand'],
      sim,
      withBots: plan.bots.RED + plan.bots.BLUE > 0,
      teamAvg,
      humans: new Map(),
      createdAt: now,
    };
    for (const team of ARENA_TEAMS) {
      for (const p of plan.teams[team]) match.humans.set(p.userId, { rating: p.rating, socketId: sockets.get(p.userId)!.id, team });
    }
    this.matches.set(id, match);

    const players = [...sim.fighters.values()].map((f) => {
      const s = sockets.get(f.id);
      return {
        ...sim.publicFighter(f),
        displayName: s ? s.data.profile.displayName : names.get(f.id)!,
        level: s ? s.data.profile.level : 1,
        avatar: s ? s.data.profile.avatar : null,
        rating: match.humans.get(f.id)?.rating ?? null,
      };
    });
    for (const [userId, s] of sockets) {
      this.playerMatch.set(userId, id);
      await this.leaveCityRoom(s);
      await s.join(match.room);
      s.emit(ArenaEvent.START, {
        matchId: id,
        you: { userId, team: match.humans.get(userId)!.team },
        startsAt: new Date(startsAt).toISOString(),
        endsAt: new Date(endsAt).toISOString(),
        serverTime: new Date(now).toISOString(),
        field: { width: ARENA_WIDTH, height: ARENA_HEIGHT },
        rules: { throwRange: ARENA_THROW_RANGE, throwCooldownMs: ARENA_THROW_COOLDOWN_MS, hitsToSoak: ARENA_HITS_TO_SOAK },
        withBots: match.withBots,
        players,
      });
    }
  }

  private leaveMatch(m: LiveMatch, userId: string): void {
    if (!m.sim.leave(userId)) return;
    this.playerMatch.delete(userId);
    const h = m.humans.get(userId);
    const s = h && (this.ns.sockets.get(h.socketId) as AppSocket | undefined);
    if (s) void s.leave(m.room);
    this.ns.to(m.room).emit(ArenaEvent.PLAYER_LEFT, { id: userId });
  }

  private async finishMatch(m: LiveMatch, now: number): Promise<void> {
    this.matches.delete(m.id);
    const outcome = m.sim.outcome();
    const sheet: MatchSheet = {
      id: m.id,
      ageBand: m.ageBand,
      withBots: m.withBots,
      startedAt: new Date(m.sim.startsAt),
      endedAt: new Date(Math.max(now, m.sim.startsAt)),
      score: { ...m.sim.score },
      outcome,
      teamAvg: m.teamAvg,
      players: [...m.humans].map(([userId, h]) => {
        const f = m.sim.fighters.get(userId)!;
        return { userId, team: h.team, rating: h.rating, leftEarly: f.left, soaks: f.soaks, soakedTimes: f.soakedTimes };
      }),
    };
    let results: Awaited<ReturnType<ArenaService['record']>> | null = null;
    try {
      results = await this.arena.record(sheet);
    } catch (e) {
      this.logger.error(`Arena match ${m.id} could not be saved`, e instanceof Error ? e.stack : String(e));
    }
    const payload = {
      matchId: m.id,
      score: sheet.score,
      outcome,
      saved: results !== null,
      players: [...m.sim.fighters.values()].map((f) => ({
        id: f.id,
        team: f.team,
        isBot: f.isBot,
        left: f.left,
        soaks: f.soaks,
        soakedTimes: f.soakedTimes,
        balloonsHit: f.balloonsHit,
      })),
      results: results ?? [],
    };
    this.ns.to(m.room).emit(ArenaEvent.END, payload);
    // players who left early still learn how it ended (on whatever connection they have now)
    for (const r of results ?? []) if (r.leftEarly) this.ns.to(userRoom(r.userId)).emit(ArenaEvent.END, payload);
    this.ns.in(m.room).socketsLeave(m.room);
    for (const userId of m.humans.keys()) if (this.playerMatch.get(userId) === m.id) this.playerMatch.delete(userId);
  }

  /** The arena is its own place: whoever stood in a city zone or home disappears from there while playing. */
  private async leaveCityRoom(s: AppSocket): Promise<void> {
    const e = await this.presence.get(s.data.user.userId);
    if (!e || e.socketId !== s.id || !e.room) return;
    await s.leave(e.room);
    s.to(e.room).emit(WsEvent.PLAYER_LEFT, { userId: e.userId });
    await this.presence.upsert({ ...e, zone: null, room: null, bounds: undefined });
  }

  private dropStaleQueue(): void {
    for (const [userId, w] of this.queue) {
      if (!this.ns.sockets.get(w.socketId)?.connected) this.queue.delete(userId);
    }
  }

  private queueStatus(userId: string) {
    const me = this.queue.get(userId)!;
    const sameBand = [...this.queue.values()].filter((w) => w.ageBand === me.ageBand);
    return {
      waiting: sameBand.length,
      botsJoinAt: new Date(me.joinedAt + this.config.get('ARENA_BOT_FILL_SECONDS') * 1000).toISOString(),
    };
  }

  private matchOf(userId: string): LiveMatch | undefined {
    const id = this.playerMatch.get(userId);
    return id ? this.matches.get(id) : undefined;
  }

  private requireMatch(socket: AppSocket): LiveMatch {
    const m = this.matchOf(socket.data.user.userId);
    if (!m || m.humans.get(socket.data.user.userId)?.socketId !== socket.id) {
      throw AppException.conflict(ErrorCode.ARENA_NOT_IN_MATCH, 'You are not in a match');
    }
    return m;
  }

  // ---------- helpers (same shape as CityGateway's) ----------

  private async handle<S extends z.ZodType, R extends Record<string, unknown>>(
    socket: AppSocket,
    limit: keyof typeof LIMITS,
    schema: S,
    body: unknown,
    fn: (input: z.infer<S>) => Promise<R>,
  ): Promise<Ack> {
    try {
      if (!socket.data?.user) throw AppException.unauthorized();
      if (!this.allow(socket, limit)) throw new AppException(ErrorCode.RATE_LIMITED, 'Too many messages', 429);
      const parsed = schema.safeParse(body);
      if (!parsed.success) {
        throw AppException.badRequest(ErrorCode.VALIDATION_FAILED, parsed.error.issues[0]?.message ?? 'Invalid message');
      }
      return { ok: true, ...(await fn(parsed.data)) };
    } catch (e) {
      if (e instanceof AppException) return { ok: false, error: { code: e.code, message: e.message, ...((e.details as Record<string, unknown> | undefined) ?? {}) } };
      this.logger.error('Arena handler failed', e instanceof Error ? e.stack : String(e));
      return { ok: false, error: { code: ErrorCode.INTERNAL_ERROR, message: 'Internal error' } };
    }
  }

  private allow(socket: AppSocket, key: keyof typeof LIMITS): boolean {
    const [rate, burst] = LIMITS[key];
    const now = Date.now();
    const buckets = (socket.data.arenaBuckets ??= {});
    const b = (buckets[key] ??= { tokens: burst, at: now });
    b.tokens = Math.min(burst, b.tokens + ((now - b.at) / 1000) * rate);
    b.at = now;
    if (b.tokens < 1) return false;
    b.tokens -= 1;
    return true;
  }

  private async safely(what: string, fn: () => Promise<void>): Promise<void> {
    try {
      await fn();
    } catch (e) {
      this.logger.warn(`Arena ${what} failed: ${e instanceof Error ? e.message : String(e)}`);
    }
  }
}
