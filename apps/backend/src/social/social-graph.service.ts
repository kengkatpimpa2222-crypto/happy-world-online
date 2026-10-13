import { Injectable } from '@nestjs/common';
import { pairKey } from '@hwo/shared-contracts';
import { PrismaService, Tx } from '../prisma/prisma.service';

/** Read-only questions about relationships between two players. No side effects. */
@Injectable()
export class SocialGraphService {
  constructor(private readonly prisma: PrismaService) {}

  /** Everyone this player blocked OR was blocked by: they must never see or reach each other. */
  async blockedEitherWay(userId: string, tx?: Tx): Promise<Set<string>> {
    const rows = await (tx ?? this.prisma).blockedPlayer.findMany({
      where: { OR: [{ blockerId: userId }, { blockedId: userId }] },
      select: { blockerId: true, blockedId: true },
    });
    return new Set(rows.map((r) => (r.blockerId === userId ? r.blockedId : r.blockerId)));
  }

  async isBlockedEitherWay(a: string, b: string, tx?: Tx): Promise<boolean> {
    const n = await (tx ?? this.prisma).blockedPlayer.count({
      where: { OR: [{ blockerId: a, blockedId: b }, { blockerId: b, blockedId: a }] },
    });
    return n > 0;
  }

  async areFriends(a: string, b: string, tx?: Tx): Promise<boolean> {
    const row = await (tx ?? this.prisma).friend.findUnique({ where: { userId_friendId: { userId: a, friendId: b } } });
    return row !== null;
  }

  async friendIds(userId: string): Promise<string[]> {
    const rows = await this.prisma.friend.findMany({ where: { userId }, select: { friendId: true } });
    return rows.map((r) => r.friendId);
  }

  /**
   * Serializes every friend/block change for one pair of players inside the current transaction
   * (Postgres advisory lock released at commit). Prevents double-tap and crossed-request races.
   */
  async lockPair(tx: Tx, a: string, b: string): Promise<void> {
    await tx.$executeRaw`SELECT pg_advisory_xact_lock(hashtextextended(${pairKey(a, b)}, 0))`;
  }
}
