import { Injectable } from '@nestjs/common';
import { ItemCategory } from '@prisma/client';
import { ErrorCode, StorePurchaseRequest } from '@hwo/shared-contracts';
import { AppException } from '../common/app-exception';
import { AuditService } from '../audit/audit.service';
import { PrismaService } from '../prisma/prisma.service';
import { QuestsService } from '../quests/quests.service';
import { WalletService } from '../wallet/wallet.service';

/** Cosmetics are owned once; furniture can stack. */
const SINGLE_OWNERSHIP: ReadonlySet<ItemCategory> = new Set(['OUTFIT', 'HAIR', 'ACCESSORY', 'FLOOR', 'WALL', 'THEME']);

@Injectable()
export class StoreService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly wallet: WalletService,
    private readonly quests: QuestsService,
    private readonly audit: AuditService,
  ) {}

  async catalog(userId: string) {
    const [items, owned] = await Promise.all([
      this.prisma.item.findMany({
        where: { isActive: true, deletedAt: null, OR: [{ priceGold: { not: null } }, { priceGems: { not: null } }] },
        orderBy: [{ category: 'asc' }, { sortOrder: 'asc' }],
      }),
      this.prisma.playerInventory.findMany({ where: { userId }, select: { itemId: true, quantity: true } }),
    ]);
    const ownedMap = new Map(owned.map((o) => [o.itemId, o.quantity]));
    return items.map((i) => ({
      itemId: i.id,
      category: i.category,
      nameKey: i.nameKey,
      priceGold: i.priceGold,
      priceGems: i.priceGems,
      isPremium: i.isPremium,
      metadata: i.metadata,
      ownedQuantity: ownedMap.get(i.id) ?? 0,
      stackable: !SINGLE_OWNERSHIP.has(i.category),
    }));
  }

  /**
   * Buys an item with in-game currency. The price comes from the database, never the client.
   * Debit + inventory grant happen in one transaction; the Idempotency-Key makes retries safe.
   */
  async purchase(userId: string, dto: StorePurchaseRequest, idempotencyKey: string) {
    return this.prisma.tx(async (tx) => {
      const item = await tx.item.findFirst({ where: { id: dto.itemId, isActive: true, deletedAt: null } });
      if (!item) throw AppException.notFound('Item not found');
      const unitPrice = dto.currency === 'GOLD' ? item.priceGold : item.priceGems;
      if (unitPrice === null || unitPrice <= 0) {
        throw AppException.conflict(ErrorCode.ITEM_NOT_AVAILABLE, `Item cannot be bought with ${dto.currency}`);
      }

      const { transaction, replayed } = await this.wallet.applyChange(tx, {
        userId,
        currency: dto.currency,
        amount: -unitPrice * dto.quantity,
        type: 'STORE_PURCHASE',
        idempotencyKey: `store:${idempotencyKey}`,
        referenceType: 'item',
        referenceId: `${item.id}x${dto.quantity}`,
        metadata: { itemId: item.id, quantity: dto.quantity, unitPrice },
      });

      if (!replayed) {
        if (SINGLE_OWNERSHIP.has(item.category)) {
          const has = await tx.playerInventory.findUnique({ where: { userId_itemId: { userId, itemId: item.id } } });
          if (dto.quantity !== 1 || (has && has.quantity > 0)) {
            // throwing rolls back the debit above
            throw AppException.conflict(ErrorCode.ITEM_NOT_AVAILABLE, 'You already own this item');
          }
        }
        await tx.playerInventory.upsert({
          where: { userId_itemId: { userId, itemId: item.id } },
          create: { userId, itemId: item.id, quantity: dto.quantity },
          update: { quantity: { increment: dto.quantity } },
        });
        await this.quests.recordEvent(tx, userId, 'BUY_ITEM', dto.quantity);
        await this.audit.log(
          { actorUserId: userId, action: 'store.purchase', targetType: 'item', targetId: item.id, metadata: { transactionId: transaction.id, currency: dto.currency, amount: transaction.amount } },
          tx,
        );
      }

      const inv = await tx.playerInventory.findUnique({ where: { userId_itemId: { userId, itemId: item.id } } });
      return {
        replayed,
        transactionId: transaction.id,
        currency: transaction.currency,
        amount: transaction.amount,
        balanceAfter: transaction.balanceAfter,
        item: { itemId: item.id, quantity: inv?.quantity ?? 0 },
      };
    });
  }
}
