import { HttpStatus, Injectable } from '@nestjs/common';
import { ItemCategory, Prisma } from '@prisma/client';
import { Avatar, ErrorCode, UpdateProfileRequest, xpForLevel } from '@hwo/shared-contracts';
import { AppException } from '../common/app-exception';
import { PrismaService } from '../prisma/prisma.service';
import { QuestsService } from '../quests/quests.service';
import { WalletService } from '../wallet/wallet.service';
import { isDisplayNameAllowed } from './name-filter';

const AVATAR_SLOT_CATEGORY: Record<'outfitItemId' | 'hairItemId' | 'accessoryItemId', ItemCategory> = {
  outfitItemId: 'OUTFIT',
  hairItemId: 'HAIR',
  accessoryItemId: 'ACCESSORY',
};

@Injectable()
export class PlayerService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly wallet: WalletService,
    private readonly quests: QuestsService,
  ) {}

  async getProfile(userId: string) {
    const user = await this.prisma.user.findUniqueOrThrow({ where: { id: userId }, include: { profile: true } });
    const p = user.profile!;
    return {
      userId: user.id,
      displayName: p.displayName,
      friendCode: p.friendCode,
      level: p.level,
      xp: p.xp,
      xpForNextLevel: xpForLevel(p.level + 1),
      avatar: p.avatar,
      achievements: p.achievements,
      locale: user.locale,
      ageBand: user.ageBand,
      isGuest: user.email === null,
      createdAt: user.createdAt,
    };
  }

  async updateProfile(userId: string, dto: UpdateProfileRequest) {
    if (dto.displayName !== undefined && !isDisplayNameAllowed(dto.displayName)) {
      throw new AppException(ErrorCode.DISPLAY_NAME_REJECTED, 'This name is not allowed', HttpStatus.UNPROCESSABLE_ENTITY);
    }
    await this.prisma.tx(async (tx) => {
      const profile = await tx.playerProfile.findUniqueOrThrow({ where: { userId } });
      let avatar: Avatar | undefined;
      if (dto.avatar) {
        avatar = { ...(profile.avatar as Avatar), ...dto.avatar };
        // Only items the server knows the player owns can be worn.
        for (const slot of Object.keys(AVATAR_SLOT_CATEGORY) as (keyof typeof AVATAR_SLOT_CATEGORY)[]) {
          const itemId = dto.avatar[slot];
          if (!itemId) continue;
          const owned = await tx.playerInventory.findFirst({
            where: { userId, itemId, quantity: { gt: 0 }, item: { category: AVATAR_SLOT_CATEGORY[slot] } },
          });
          if (!owned) throw AppException.conflict(ErrorCode.ITEM_NOT_OWNED, `Item ${itemId} is not owned or does not fit ${slot}`);
        }
      }
      await tx.playerProfile.update({
        where: { userId },
        data: { displayName: dto.displayName, avatar: avatar as Prisma.InputJsonValue | undefined },
      });
      if (dto.locale) await tx.user.update({ where: { id: userId }, data: { locale: dto.locale } });
      await this.quests.recordEvent(tx, userId, 'UPDATE_PROFILE');
    });
    return this.getProfile(userId);
  }

  async getInventory(userId: string) {
    const rows = await this.prisma.playerInventory.findMany({
      where: { userId, quantity: { gt: 0 } },
      include: { item: { select: { category: true, nameKey: true, isPremium: true, metadata: true } } },
      orderBy: { acquiredAt: 'asc' },
    });
    return rows.map((r) => ({ itemId: r.itemId, quantity: r.quantity, acquiredAt: r.acquiredAt, ...r.item }));
  }

  async getWallet(userId: string) {
    const [balances, recentTransactions] = await Promise.all([
      this.wallet.getBalances(userId),
      this.wallet.recentTransactions(userId),
    ]);
    return { balances, recentTransactions };
  }
}
