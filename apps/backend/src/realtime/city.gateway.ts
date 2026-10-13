import { Logger } from '@nestjs/common';
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
  canInteract,
  ErrorCode,
  MAX_MOVE_SPEED,
  WS_NAMESPACE,
  WsEvent,
  wsInviteSchema,
  wsMoveSchema,
  wsPresetChatSchema,
  wsZoneJoinSchema,
  zoneById,
} from '@hwo/shared-contracts';
import type { Namespace, Socket } from 'socket.io';
import { z } from 'zod';
import { AppException } from '../common/app-exception';
import { AuthUser } from '../common/decorators';
import { SessionVerifier } from '../auth/session-verifier.service';
import { PrismaService } from '../prisma/prisma.service';
import { PresenceEntry } from '../presence/presence.store';
import { PresenceService } from '../presence/presence.service';
import { SocialGraphService } from '../social/social-graph.service';
import { CityService } from './city.service';
import { BLOCKS_CHANGED, RealtimeService, userRoom } from './realtime.service';

interface Bucket {
  tokens: number;
  at: number;
}

interface SocketData {
  user: AuthUser;
  profile: { displayName: string; level: number; avatar: unknown };
  blocked: Set<string>;
  buckets: Record<string, Bucket>;
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type AnyEvents = Record<string, (...args: any[]) => void>;
type AppSocket = Socket<AnyEvents, AnyEvents, AnyEvents, SocketData>;

type Ack = { ok: true; [k: string]: unknown } | { ok: false; error: { code: string; message: string } };

/** Per-socket token buckets: [refill per second, burst]. */
const LIMITS = {
  move: [20, 30],
  chat: [0.5, 3],
  invite: [0.34, 2],
  zone: [1, 5],
  heartbeat: [1, 5],
} as const satisfies Record<string, readonly [number, number]>;

/**
 * Realtime city: presence, movement and preset chat over Socket.IO.
 * The server is authoritative for position (bounds + max speed) and for who may see whom
 * (same age band, never across a block in either direction).
 */
@WebSocketGateway({ namespace: WS_NAMESPACE })
export class CityGateway implements OnGatewayInit, OnGatewayDisconnect {
  private readonly logger = new Logger('CityGateway');
  @WebSocketServer() private ns!: Namespace;

  constructor(
    private readonly sessions: SessionVerifier,
    private readonly prisma: PrismaService,
    private readonly presence: PresenceService,
    private readonly graph: SocialGraphService,
    private readonly city: CityService,
    private readonly realtime: RealtimeService,
  ) {}

  afterInit(ns: Namespace): void {
    // Authenticate during the handshake: unauthenticated sockets never reach a handler.
    ns.use((socket, next) => {
      this.authenticate(socket as AppSocket)
        .then(() => next())
        .catch((e: unknown) => {
          const err = new Error(e instanceof AppException ? e.message : 'Unauthorized') as Error & { data?: unknown };
          err.data = { code: e instanceof AppException ? e.code : ErrorCode.UNAUTHORIZED };
          next(err);
        });
    });
    ns.on('connection', (socket: AppSocket) => void this.safely('connect', () => this.onConnected(socket)));
    ns.on(BLOCKS_CHANGED, (ids: string[]) => void this.safely('blocks-changed', () => this.reloadBlocks(ids)));
    this.realtime.attach(ns, (ids) => this.reloadBlocks(ids));
  }

  private async authenticate(socket: AppSocket): Promise<void> {
    const auth = socket.handshake.auth as { token?: unknown } | undefined;
    const user = await this.sessions.verify(typeof auth?.token === 'string' ? auth.token : undefined);
    const profile = await this.prisma.playerProfile.findUniqueOrThrow({
      where: { userId: user.userId },
      select: { displayName: true, level: true, avatar: true },
    });
    socket.data = { user, profile, blocked: await this.graph.blockedEitherWay(user.userId), buckets: {} };
  }

  private async onConnected(socket: AppSocket): Promise<void> {
    const { user, profile } = socket.data;
    // One live connection per player: the newest wins (e.g. app reopened on another phone).
    const previous = await this.ns.in(userRoom(user.userId)).fetchSockets();
    for (const old of previous) {
      old.emit(WsEvent.SESSION_REPLACED, {});
      old.disconnect(true);
    }
    await socket.join(userRoom(user.userId));
    await this.presence.upsert(this.entry(socket, { zone: null, room: null, x: 0, y: 0, dir: 0 }));
    for (const friendId of await this.graph.friendIds(user.userId)) {
      this.realtime.notifyUser(friendId, WsEvent.FRIEND_ONLINE, { userId: user.userId, displayName: profile.displayName });
    }
    socket.emit(WsEvent.READY, { userId: user.userId });
  }

  handleDisconnect(socket: AppSocket): Promise<void> {
    return this.safely('disconnect', () => this.onDisconnected(socket));
  }

  /** Socket lifecycle callbacks must never throw (an unhandled rejection would crash the process). */
  private async safely(what: string, fn: () => Promise<void>): Promise<void> {
    try {
      await fn();
    } catch (e) {
      this.logger.warn(`Realtime ${what} handler failed: ${e instanceof Error ? e.message : String(e)}`);
    }
  }

  private async onDisconnected(socket: AppSocket): Promise<void> {
    if (!socket.data?.user) return;
    const { userId } = socket.data.user;
    const removed = await this.presence.remove(userId, socket.id);
    if (!removed) return; // a newer connection already took over
    if (removed.room) this.broadcast(socket, removed.room, WsEvent.PLAYER_LEFT, { userId });
    for (const friendId of await this.graph.friendIds(userId)) {
      this.realtime.notifyUser(friendId, WsEvent.FRIEND_OFFLINE, { userId });
    }
  }

  @SubscribeMessage(WsEvent.ZONE_JOIN)
  zoneJoin(@ConnectedSocket() socket: AppSocket, @MessageBody() body: unknown): Promise<Ack> {
    return this.handle(socket, 'zone', wsZoneJoinSchema, body, async ({ zone: zoneId }) => {
      const zone = this.city.requireZone(zoneId);
      const me = await this.current(socket);
      const room = await this.city.pickRoom(zone, socket.data.user.ageBand, me.room);
      if (me.room && me.room !== room) {
        await socket.leave(me.room);
        this.broadcast(socket, me.room, WsEvent.PLAYER_LEFT, { userId: socket.data.user.userId });
      }
      const entry = this.entry(socket, { zone: zone.id, room, x: zone.spawn.x, y: zone.spawn.y, dir: 0 });
      await this.presence.upsert(entry);
      await socket.join(room);
      const friends = new Set(await this.graph.friendIds(socket.data.user.userId));
      const players = await this.city.visibleInRoom(socket.data.user.userId, room, socket.data.blocked, friends);
      this.broadcast(socket, room, WsEvent.PLAYER_JOINED, this.city.toPublic(entry));
      socket.emit(WsEvent.ZONE_STATE, { zone: zone.id, room, you: { x: entry.x, y: entry.y }, players });
      return { zone: zone.id, room, x: entry.x, y: entry.y, players };
    });
  }

  @SubscribeMessage(WsEvent.ZONE_LEAVE)
  zoneLeave(@ConnectedSocket() socket: AppSocket): Promise<Ack> {
    return this.handle(socket, 'zone', z.any(), undefined, async () => {
      const me = await this.current(socket);
      if (me.room) {
        await socket.leave(me.room);
        this.broadcast(socket, me.room, WsEvent.PLAYER_LEFT, { userId: socket.data.user.userId });
      }
      await this.presence.upsert({ ...me, zone: null, room: null });
      return {};
    });
  }

  @SubscribeMessage(WsEvent.MOVE)
  move(@ConnectedSocket() socket: AppSocket, @MessageBody() body: unknown): Promise<Ack> {
    return this.handle(socket, 'move', wsMoveSchema, body, async (m) => {
      const me = await this.current(socket);
      if (!me.room || !me.zone) throw AppException.conflict(ErrorCode.NOT_IN_ZONE, 'Join a zone first');
      const zone = zoneById(me.zone)!;
      const now = Date.now();
      const dt = Math.max(50, now - me.lastMoveAt) / 1000;
      const dist = Math.hypot(m.x - me.x, m.y - me.y);
      const inBounds = m.x >= 0 && m.y >= 0 && m.x <= zone.width && m.y <= zone.height;
      // 25% + 0.5 unit tolerance for network jitter
      if (!inBounds || dist > MAX_MOVE_SPEED * dt * 1.25 + 0.5) {
        socket.emit(WsEvent.MOVE_CORRECTED, { x: me.x, y: me.y, seq: m.seq });
        throw AppException.badRequest(ErrorCode.MOVE_REJECTED, inBounds ? 'Moving too fast' : 'Out of bounds');
      }
      const dir = m.dir ?? me.dir;
      await this.presence.upsert({ ...me, x: m.x, y: m.y, dir, lastMoveAt: now });
      this.broadcast(socket, me.room, WsEvent.PLAYER_MOVED, { userId: me.userId, x: m.x, y: m.y, dir, seq: m.seq }, true);
      return { x: m.x, y: m.y };
    });
  }

  @SubscribeMessage(WsEvent.CHAT_PRESET)
  presetChat(@ConnectedSocket() socket: AppSocket, @MessageBody() body: unknown): Promise<Ack> {
    return this.handle(socket, 'chat', wsPresetChatSchema, body, async ({ presetId }) => {
      const me = await this.current(socket);
      if (!me.room) throw AppException.conflict(ErrorCode.NOT_IN_ZONE, 'Join a zone first');
      this.broadcast(socket, me.room, WsEvent.CHAT_PRESET_RECEIVED, { userId: me.userId, displayName: me.displayName, presetId });
      return { presetId };
    });
  }

  @SubscribeMessage(WsEvent.FRIEND_INVITE)
  invite(@ConnectedSocket() socket: AppSocket, @MessageBody() body: unknown): Promise<Ack> {
    return this.handle(socket, 'invite', wsInviteSchema, body, async ({ friendId, activity }) => {
      const { user } = socket.data;
      if (!(await this.graph.areFriends(user.userId, friendId)) || socket.data.blocked.has(friendId)) {
        throw AppException.forbidden(ErrorCode.NOT_FRIENDS, 'You can only invite friends');
      }
      const target = await this.presence.get(friendId);
      if (!target || !canInteract(user.ageBand, target.ageBand as AuthUser['ageBand'])) {
        throw AppException.conflict(ErrorCode.PLAYER_OFFLINE, 'Friend is offline');
      }
      const me = await this.current(socket);
      this.realtime.notifyUser(friendId, WsEvent.FRIEND_INVITED, {
        fromUserId: user.userId,
        displayName: me.displayName,
        activity,
        zone: me.zone,
      });
      return {};
    });
  }

  @SubscribeMessage(WsEvent.HEARTBEAT)
  heartbeat(@ConnectedSocket() socket: AppSocket): Promise<Ack> {
    return this.handle(socket, 'heartbeat', z.any(), undefined, async () => {
      await this.presence.upsert(await this.current(socket));
      return {};
    });
  }

  // ---------- helpers ----------

  /** Validates input, applies the per-socket rate limit and turns errors into an ack. */
  private async handle<S extends z.ZodType, R extends Record<string, unknown>>(
    socket: AppSocket,
    limit: keyof typeof LIMITS,
    schema: S,
    body: unknown,
    fn: (input: z.infer<S>) => Promise<R>,
  ): Promise<Ack> {
    try {
      if (!socket.data?.user) throw AppException.unauthorized();
      if (!this.allow(socket, limit)) {
        throw new AppException(ErrorCode.RATE_LIMITED, 'Too many messages', 429);
      }
      const parsed = schema.safeParse(body);
      if (!parsed.success) {
        throw AppException.badRequest(ErrorCode.VALIDATION_FAILED, parsed.error.issues[0]?.message ?? 'Invalid message');
      }
      return { ok: true, ...(await fn(parsed.data)) };
    } catch (e) {
      if (e instanceof AppException) return { ok: false, error: { code: e.code, message: e.message } };
      this.logger.error('Realtime handler failed', e instanceof Error ? e.stack : String(e));
      return { ok: false, error: { code: ErrorCode.INTERNAL_ERROR, message: 'Internal error' } };
    }
  }

  private allow(socket: AppSocket, key: keyof typeof LIMITS): boolean {
    const [rate, burst] = LIMITS[key];
    const now = Date.now();
    const b = (socket.data.buckets[key] ??= { tokens: burst, at: now });
    b.tokens = Math.min(burst, b.tokens + ((now - b.at) / 1000) * rate);
    b.at = now;
    if (b.tokens < 1) return false;
    b.tokens -= 1;
    return true;
  }

  /** Sends to everyone else in the room except players blocked either way. */
  private broadcast(socket: AppSocket, room: string, event: string, payload: unknown, volatile = false): void {
    const except = [...socket.data.blocked].map(userRoom);
    const op = socket.to(room).except(except);
    (volatile ? op.volatile : op).emit(event, payload);
  }

  private async current(socket: AppSocket): Promise<PresenceEntry> {
    const e = await this.presence.get(socket.data.user.userId);
    if (e && e.socketId === socket.id) return e;
    // expired (no heartbeat): start again outside any zone
    const fresh = this.entry(socket, { zone: null, room: null, x: 0, y: 0, dir: 0 });
    await this.presence.upsert(fresh);
    return fresh;
  }

  private entry(
    socket: AppSocket,
    pos: Pick<PresenceEntry, 'zone' | 'room' | 'x' | 'y' | 'dir'>,
  ): PresenceEntry {
    const { user, profile } = socket.data;
    return {
      userId: user.userId,
      socketId: socket.id,
      ageBand: user.ageBand,
      displayName: profile.displayName,
      level: profile.level,
      avatar: profile.avatar,
      ...pos,
      lastMoveAt: Date.now(),
      lastSeen: Date.now(),
    };
  }

  /** After a block/unblock: refresh cached block lists and make the two players vanish for each other. */
  private async reloadBlocks(ids: string[]): Promise<void> {
    for (const id of ids) {
      const sockets = await this.ns.in(userRoom(id)).local.fetchSockets();
      if (sockets.length === 0) continue;
      const blocked = await this.graph.blockedEitherWay(id);
      for (const s of sockets) {
        (s.data as SocketData).blocked = blocked;
        for (const other of ids) if (other !== id && blocked.has(other)) s.emit(WsEvent.PLAYER_LEFT, { userId: other });
      }
    }
  }
}
