import { Global, Injectable, Module } from '@nestjs/common';
import { AgeBand, HomeVisitPolicy } from '@prisma/client';
import { canInteract, ErrorCode } from '@hwo/shared-contracts';
import { AppException } from '../common/app-exception';
import { PrismaService, Tx } from '../prisma/prisma.service';
import { SocialGraphService } from '../social/social-graph.service';

export interface HomeAccess {
  homeId: string;
  ownerId: string;
  isOwner: boolean;
  isFriend: boolean;
  visitPolicy: HomeVisitPolicy;
}

/**
 * The one place that decides who may see or walk into a home (Phase 10), used by REST and realtime alike.
 * - the owner always may;
 * - nobody may see an unclaimed home, a home across age bands or a home across a block (404: not even that it exists);
 * - PRIVATE: owner only; FRIENDS: friends only; PUBLIC: anyone else who passed the checks above (403 otherwise).
 */
@Injectable()
export class HomeAccessService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly graph: SocialGraphService,
  ) {}

  async check(viewerId: string, viewerAgeBand: AgeBand, ownerId: string, tx?: Tx): Promise<HomeAccess> {
    const db = tx ?? this.prisma;
    const home = await db.playerHome.findUnique({
      where: { userId: ownerId },
      select: { id: true, claimedAt: true, visitPolicy: true, user: { select: { ageBand: true, status: true, deletedAt: true } } },
    });
    const base = { homeId: home?.id ?? '', ownerId, visitPolicy: home?.visitPolicy ?? 'PRIVATE' };
    if (home && viewerId === ownerId) return { ...base, isOwner: true, isFriend: false };
    if (!home || !home.claimedAt || home.user.status !== 'ACTIVE' || home.user.deletedAt) throw AppException.notFound('Home not found');
    if (!canInteract(viewerAgeBand, home.user.ageBand) || (await this.graph.isBlockedEitherWay(viewerId, ownerId, tx))) {
      throw AppException.notFound('Home not found');
    }
    const isFriend = await this.graph.areFriends(viewerId, ownerId, tx);
    const allowed = home.visitPolicy === 'PUBLIC' || (home.visitPolicy === 'FRIENDS' && isFriend);
    if (!allowed) {
      throw AppException.forbidden(ErrorCode.HOME_VISIT_NOT_ALLOWED, home.visitPolicy === 'PRIVATE' ? 'This home is private' : 'Only friends can visit this home');
    }
    return { ...base, isOwner: false, isFriend };
  }

  /** Same rules, as a yes/no (realtime re-checks visitors with it). */
  async allowed(viewerId: string, viewerAgeBand: AgeBand, ownerId: string): Promise<boolean> {
    try {
      await this.check(viewerId, viewerAgeBand, ownerId);
      return true;
    } catch (e) {
      if (e instanceof AppException) return false;
      throw e;
    }
  }
}

@Global()
@Module({ providers: [HomeAccessService], exports: [HomeAccessService] })
export class HomeAccessModule {}
