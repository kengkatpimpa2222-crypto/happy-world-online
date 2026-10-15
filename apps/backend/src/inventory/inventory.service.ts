import { Injectable } from '@nestjs/common';
import { InventoryTransaction, InventoryTransactionType } from '@prisma/client';
import { ErrorCode } from '@hwo/shared-contracts';
import { AppException } from '../common/app-exception';
import { Tx } from '../prisma/prisma.service';

export interface ItemChange {
  userId: string;
  itemId: string;
  /** Positive = add, negative = use up. Never zero. */
  delta: number;
  type: InventoryTransactionType;
  /** Unique per player. A repeated key returns the original change instead of moving items twice. */
  idempotencyKey: string;
  referenceType?: string;
  referenceId?: string;
}

/**
 * Changes item quantities with a ledger row per change (Phase 9), the item twin of WalletService:
 * - using up items is a single conditional UPDATE (`quantity >= n`), so parallel crafts can't go negative;
 * - every change writes an append-only `inventory_transactions` row with the quantity after;
 * - must be called inside the transaction of whatever the items are for.
 */
@Injectable()
export class InventoryService {
  async applyChange(tx: Tx, c: ItemChange): Promise<{ transaction: InventoryTransaction; replayed: boolean }> {
    if (!Number.isSafeInteger(c.delta) || c.delta === 0) throw new Error(`Invalid item delta: ${c.delta}`);

    const existing = await tx.inventoryTransaction.findUnique({ where: { userId_idempotencyKey: { userId: c.userId, idempotencyKey: c.idempotencyKey } } });
    if (existing) {
      if (existing.itemId !== c.itemId || existing.delta !== c.delta || existing.type !== c.type) {
        throw AppException.conflict(ErrorCode.IDEMPOTENCY_KEY_REUSED, 'Idempotency-Key was already used for a different request');
      }
      return { transaction: existing, replayed: true };
    }

    let quantityAfter: number;
    if (c.delta > 0) {
      const row = await tx.playerInventory.upsert({
        where: { userId_itemId: { userId: c.userId, itemId: c.itemId } },
        create: { userId: c.userId, itemId: c.itemId, quantity: c.delta },
        update: { quantity: { increment: c.delta } },
      });
      quantityAfter = row.quantity;
    } else {
      const rows = await tx.$queryRaw<{ quantity: number }[]>`
        UPDATE "player_inventory" SET "quantity" = "quantity" + ${c.delta}, "updated_at" = now()
         WHERE "user_id" = ${c.userId}::uuid AND "item_id" = ${c.itemId} AND "quantity" + ${c.delta} >= 0
        RETURNING "quantity"`;
      if (!rows[0]) {
        const have = await tx.playerInventory.findUnique({ where: { userId_itemId: { userId: c.userId, itemId: c.itemId } } });
        throw AppException.conflict(ErrorCode.NOT_ENOUGH_MATERIALS, `Not enough ${c.itemId}`, { itemId: c.itemId, required: -c.delta, have: have?.quantity ?? 0 });
      }
      quantityAfter = rows[0].quantity;
    }

    const transaction = await tx.inventoryTransaction.create({
      data: {
        userId: c.userId,
        itemId: c.itemId,
        type: c.type,
        delta: c.delta,
        quantityAfter,
        referenceType: c.referenceType,
        referenceId: c.referenceId,
        idempotencyKey: c.idempotencyKey,
      },
    });
    return { transaction, replayed: false };
  }
}
