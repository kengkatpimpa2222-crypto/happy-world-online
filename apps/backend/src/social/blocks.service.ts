import { Injectable } from '@nestjs/common';
import { ErrorCode, pairKey } from '@hwo/shared-contracts';
import { AppException } from '../common/app-exception';
import { AuditService } from '../audit/audit.service';
import { PrismaService } from '../prisma/prisma.service';
import { RealtimeService } from '../realtime/realtime.service';
import { FriendsService } from './friends.service';
import { SocialGraphService } from './social-graph.service';

/**
 * Blocking is always allowed (any age band, any relationship) and takes effect everywhere at once:
 * friendship removed, pending requests cancelled, both players vanish from each other's city view.
 */
@Injectable()
export class BlocksService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly graph: SocialGraphService,
    private readonly friends: FriendsService,
    private readonly audit: AuditService,
    private readonly realtime: RealtimeService,
  ) {}

  async block(userId: string, targetId: string) {
    if (userId === targetId) throw AppException.badRequest(ErrorCode.CANNOT_TARGET_SELF, 'You cannot block yourself');
    const target = await this.prisma.user.findUnique({ where: { id: targetId }, select: { id: true } });
    if (!target) throw AppException.notFound('Player not found');

    const created = await this.prisma.tx(async (tx) => {
      await this.graph.lockPair(tx, userId, targetId);
      const res = await tx.blockedPlayer.createMany({ data: [{ blockerId: userId, blockedId: targetId }], skipDuplicates: true });
      await this.friends.deleteFriendship(tx, userId, targetId);
      await tx.friendRequest.updateMany({
        where: { pairKey: pairKey(userId, targetId), status: 'PENDING' },
        data: { status: 'CANCELLED', respondedAt: new Date() },
      });
      if (res.count > 0) await this.audit.log({ actorUserId: userId, action: 'player.blocked', targetType: 'user', targetId }, tx);
      return res.count > 0;
    });
    await this.realtime.blocksChanged(userId, targetId);
    return { playerId: targetId, blocked: true, alreadyBlocked: !created };
  }

  async unblock(userId: string, targetId: string): Promise<void> {
    const res = await this.prisma.blockedPlayer.deleteMany({ where: { blockerId: userId, blockedId: targetId } });
    if (res.count > 0) {
      await this.audit.log({ actorUserId: userId, action: 'player.unblocked', targetType: 'user', targetId });
      await this.realtime.blocksChanged(userId, targetId);
    }
  }

  async list(userId: string) {
    const rows = await this.prisma.blockedPlayer.findMany({
      where: { blockerId: userId },
      include: { blocked: { select: { id: true, profile: { select: { displayName: true } } } } },
      orderBy: { createdAt: 'desc' },
    });
    return rows.map((r) => ({ playerId: r.blocked.id, displayName: r.blocked.profile?.displayName ?? null, blockedAt: r.createdAt }));
  }
}
