import { HttpStatus, Injectable } from '@nestjs/common';
import { Prisma } from '@prisma/client';
import {
  canInteract,
  ErrorCode,
  MAX_FRIENDS,
  MAX_PENDING_OUTGOING_REQUESTS,
  pairKey,
  SendFriendRequest,
  WsEvent,
} from '@hwo/shared-contracts';
import { AppException } from '../common/app-exception';
import { AuthUser } from '../common/decorators';
import { AuditService } from '../audit/audit.service';
import { PresenceService } from '../presence/presence.service';
import { PrismaService, Tx } from '../prisma/prisma.service';
import { QuestsService } from '../quests/quests.service';
import { RealtimeService } from '../realtime/realtime.service';
import { SocialGraphService } from './social-graph.service';

const PROFILE_SELECT = { displayName: true, level: true, avatar: true } as const;

/**
 * Friend requests and friendships. Every write for a pair runs under a per-pair advisory lock,
 * and the database allows only one PENDING request per pair, so double taps, retries and
 * crossed requests (A->B while B->A) can never create duplicates.
 */
@Injectable()
export class FriendsService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly graph: SocialGraphService,
    private readonly quests: QuestsService,
    private readonly audit: AuditService,
    private readonly realtime: RealtimeService,
    private readonly presence: PresenceService,
  ) {}

  async sendRequest(me: AuthUser, dto: SendFriendRequest) {
    const target = await this.prisma.user.findFirst({
      where: dto.playerId ? { id: dto.playerId } : { profile: { friendCode: dto.friendCode } },
      select: { id: true, ageBand: true, status: true, deletedAt: true },
    });
    if (!target || target.status !== 'ACTIVE' || target.deletedAt) throw AppException.notFound('Player not found');
    if (target.id === me.userId) throw AppException.badRequest(ErrorCode.CANNOT_TARGET_SELF, 'You cannot add yourself');
    // Same answer for "blocked" and "different age band": never reveal why.
    const notAllowed = () => AppException.forbidden(ErrorCode.FRIEND_REQUEST_NOT_ALLOWED, 'You cannot send a friend request to this player');
    if (!canInteract(me.ageBand, target.ageBand)) throw notAllowed();

    const result = await this.prisma.tx(async (tx) => {
      await this.graph.lockPair(tx, me.userId, target.id);
      if (await this.graph.isBlockedEitherWay(me.userId, target.id, tx)) throw notAllowed();
      if (await this.graph.areFriends(me.userId, target.id, tx)) {
        throw AppException.conflict(ErrorCode.ALREADY_FRIENDS, 'You are already friends');
      }

      const pending = await tx.friendRequest.findFirst({ where: { pairKey: pairKey(me.userId, target.id), status: 'PENDING' } });
      if (pending?.fromUserId === me.userId) return { request: pending, created: false, autoAccepted: false }; // retry
      if (pending) {
        // Crossed request: they already asked us, so this counts as accepting theirs.
        await this.acceptInTx(tx, pending.id, me.userId);
        return { request: { ...pending, status: 'ACCEPTED' as const }, created: false, autoAccepted: true };
      }

      const [outgoing, friendCount] = await Promise.all([
        tx.friendRequest.count({ where: { fromUserId: me.userId, status: 'PENDING' } }),
        tx.friend.count({ where: { userId: me.userId } }),
      ]);
      if (outgoing >= MAX_PENDING_OUTGOING_REQUESTS || friendCount >= MAX_FRIENDS) {
        throw AppException.conflict(ErrorCode.FRIEND_LIMIT_REACHED, 'Friend or request limit reached');
      }
      const request = await tx.friendRequest.create({
        data: { fromUserId: me.userId, toUserId: target.id, pairKey: pairKey(me.userId, target.id) },
      });
      return { request, created: true, autoAccepted: false };
    });

    if (result.created) {
      const profile = await this.prisma.playerProfile.findUnique({ where: { userId: me.userId }, select: PROFILE_SELECT });
      this.realtime.notifyUser(target.id, WsEvent.FRIEND_REQUEST_RECEIVED, { requestId: result.request.id, from: { userId: me.userId, ...profile } });
    }
    if (result.autoAccepted) this.notifyAccepted(result.request.fromUserId, me.userId);
    return {
      requestId: result.request.id,
      status: result.request.status,
      created: result.created,
      autoAccepted: result.autoAccepted,
    };
  }

  async incoming(userId: string) {
    const rows = await this.prisma.friendRequest.findMany({
      where: { toUserId: userId, status: 'PENDING' },
      include: { fromUser: { select: { id: true, profile: { select: PROFILE_SELECT } } } },
      orderBy: { createdAt: 'desc' },
    });
    return rows.map((r) => ({ requestId: r.id, createdAt: r.createdAt, from: { userId: r.fromUser.id, ...r.fromUser.profile } }));
  }

  async outgoing(userId: string) {
    const rows = await this.prisma.friendRequest.findMany({
      where: { fromUserId: userId, status: 'PENDING' },
      include: { toUser: { select: { id: true, profile: { select: PROFILE_SELECT } } } },
      orderBy: { createdAt: 'desc' },
    });
    return rows.map((r) => ({ requestId: r.id, createdAt: r.createdAt, to: { userId: r.toUser.id, ...r.toUser.profile } }));
  }

  async accept(userId: string, requestId: string) {
    const request = await this.prisma.friendRequest.findUnique({ where: { id: requestId } });
    // Only the receiver may answer; anyone else gets the same 404 as a missing request.
    if (!request || request.toUserId !== userId) throw AppException.notFound('Friend request not found');
    const outcome = await this.prisma.tx(async (tx) => {
      await this.graph.lockPair(tx, request.fromUserId, request.toUserId);
      return this.acceptInTx(tx, requestId, userId);
    });
    if (outcome === 'accepted') this.notifyAccepted(request.fromUserId, userId);
    return { requestId, status: 'ACCEPTED' as const, alreadyAccepted: outcome === 'already' };
  }

  async decline(userId: string, requestId: string) {
    const request = await this.prisma.friendRequest.findUnique({ where: { id: requestId } });
    if (!request || request.toUserId !== userId) throw AppException.notFound('Friend request not found');
    if (request.status === 'DECLINED') return { requestId, status: 'DECLINED' as const };
    const updated = await this.prisma.friendRequest.updateMany({
      where: { id: requestId, toUserId: userId, status: 'PENDING' },
      data: { status: 'DECLINED', respondedAt: new Date() },
    });
    if (updated.count === 0) {
      const now = await this.prisma.friendRequest.findUniqueOrThrow({ where: { id: requestId } });
      if (now.status !== 'DECLINED') throw AppException.conflict(ErrorCode.FRIEND_REQUEST_CLOSED, `Request is already ${now.status.toLowerCase()}`);
    }
    return { requestId, status: 'DECLINED' as const };
  }

  /** Sender withdraws a pending request. Idempotent. */
  async cancel(userId: string, requestId: string): Promise<void> {
    const request = await this.prisma.friendRequest.findUnique({ where: { id: requestId } });
    if (!request || request.fromUserId !== userId) throw AppException.notFound('Friend request not found');
    await this.prisma.friendRequest.updateMany({
      where: { id: requestId, fromUserId: userId, status: 'PENDING' },
      data: { status: 'CANCELLED', respondedAt: new Date() },
    });
  }

  async list(userId: string) {
    const rows = await this.prisma.friend.findMany({
      where: { userId },
      include: { friend: { select: { id: true, profile: { select: { ...PROFILE_SELECT, friendCode: true } } } } },
      orderBy: { createdAt: 'asc' },
    });
    const online = await this.presence.getMany(rows.map((r) => r.friendId));
    return rows.map((r) => ({
      userId: r.friend.id,
      ...r.friend.profile,
      friendsSince: r.createdAt,
      online: online.has(r.friendId),
      zone: online.get(r.friendId)?.zone ?? null,
    }));
  }

  /** Removes the friendship only (both directions). Accounts and everything else stay. Idempotent. */
  async remove(userId: string, friendId: string): Promise<void> {
    await this.prisma.tx(async (tx) => {
      await this.graph.lockPair(tx, userId, friendId);
      const deleted = await this.deleteFriendship(tx, userId, friendId);
      if (deleted > 0) await this.audit.log({ actorUserId: userId, action: 'friends.removed', targetType: 'user', targetId: friendId }, tx);
    });
    // Phase 10: a friends-only home is closed to an ex-friend straight away
    await this.realtime.homeAccessChanged([userId, friendId]);
  }

  async deleteFriendship(tx: Tx, a: string, b: string): Promise<number> {
    const res = await tx.friend.deleteMany({
      where: { OR: [{ userId: a, friendId: b }, { userId: b, friendId: a }] },
    });
    return res.count;
  }

  /** Caller must hold the pair lock. Returns 'accepted' or 'already' (idempotent retry). */
  private async acceptInTx(tx: Tx, requestId: string, receiverId: string): Promise<'accepted' | 'already'> {
    const req = await tx.friendRequest.findUniqueOrThrow({ where: { id: requestId } });
    if (req.status === 'ACCEPTED') return 'already';
    if (req.status !== 'PENDING') {
      throw new AppException(ErrorCode.FRIEND_REQUEST_CLOSED, `Request is already ${req.status.toLowerCase()}`, HttpStatus.CONFLICT);
    }
    if (await this.graph.isBlockedEitherWay(req.fromUserId, req.toUserId, tx)) {
      throw AppException.forbidden(ErrorCode.FRIEND_REQUEST_NOT_ALLOWED, 'This request can no longer be accepted');
    }
    const counts = await Promise.all([req.fromUserId, req.toUserId].map((id) => tx.friend.count({ where: { userId: id } })));
    if (counts.some((c) => c >= MAX_FRIENDS)) throw AppException.conflict(ErrorCode.FRIEND_LIMIT_REACHED, 'Friend limit reached');

    await tx.friendRequest.update({ where: { id: requestId }, data: { status: 'ACCEPTED', respondedAt: new Date() } });
    await tx.friend.createMany({
      data: [
        { userId: req.fromUserId, friendId: req.toUserId },
        { userId: req.toUserId, friendId: req.fromUserId },
      ],
      skipDuplicates: true,
    });
    await this.quests.recordEvent(tx, req.fromUserId, 'ADD_FRIEND');
    await this.quests.recordEvent(tx, req.toUserId, 'ADD_FRIEND');
    await this.audit.log(
      { actorUserId: receiverId, action: 'friends.accepted', targetType: 'friend_request', targetId: requestId, metadata: { with: req.fromUserId } as Prisma.InputJsonValue },
      tx,
    );
    return 'accepted';
  }

  private notifyAccepted(senderId: string, receiverId: string): void {
    this.realtime.notifyUser(senderId, WsEvent.FRIEND_REQUEST_ACCEPTED, { userId: receiverId });
    this.realtime.notifyUser(receiverId, WsEvent.FRIEND_REQUEST_ACCEPTED, { userId: senderId });
  }
}
