import { Injectable } from '@nestjs/common';
import { Currency, Prisma, WalletTransaction, WalletTransactionType } from '@prisma/client';
import { ErrorCode } from '@hwo/shared-contracts';
import { AppException } from '../common/app-exception';
import { PrismaService, Tx } from '../prisma/prisma.service';

export interface WalletChange {
  userId: string;
  currency: Currency;
  /** Positive = credit, negative = debit. Never zero. */
  amount: number;
  type: WalletTransactionType;
  /** Unique per player. A repeated key returns the original transaction instead of moving money twice. */
  idempotencyKey: string;
  /** Describes WHAT was paid for; a reused key with a different reference is rejected. */
  referenceType?: string;
  referenceId?: string;
  metadata?: Prisma.InputJsonValue;
}

export interface WalletChangeResult {
  transaction: WalletTransaction;
  /** true when this key was already processed and nothing changed now */
  replayed: boolean;
}

/**
 * The ONLY place that changes wallet balances.
 * - Balance update is a single conditional UPDATE (`balance + amount >= 0`), so concurrent
 *   debits can never overdraw; the DB CHECK constraint is a second guard.
 * - Every change writes an append-only ledger row with balance before/after.
 * - Must be called inside a transaction together with whatever the money pays for.
 */
@Injectable()
export class WalletService {
  constructor(private readonly prisma: PrismaService) {}

  async applyChange(tx: Tx, change: WalletChange): Promise<WalletChangeResult> {
    if (!Number.isSafeInteger(change.amount) || change.amount === 0) {
      throw new Error(`Invalid wallet amount: ${change.amount}`);
    }

    const existing = await tx.walletTransaction.findUnique({
      where: { userId_idempotencyKey: { userId: change.userId, idempotencyKey: change.idempotencyKey } },
    });
    if (existing) {
      const same =
        existing.amount === change.amount &&
        existing.currency === change.currency &&
        existing.type === change.type &&
        (existing.referenceId ?? undefined) === change.referenceId;
      if (!same) {
        throw AppException.conflict(ErrorCode.IDEMPOTENCY_KEY_REUSED, 'Idempotency-Key was already used for a different request');
      }
      return { transaction: existing, replayed: true };
    }

    const rows = await tx.$queryRaw<{ id: string; balance: number }[]>`
      UPDATE "player_wallets"
         SET "balance" = "balance" + ${change.amount}, "version" = "version" + 1, "updated_at" = now()
       WHERE "user_id" = ${change.userId}::uuid
         AND "currency" = ${change.currency}::"Currency"
         AND "balance" + ${change.amount} >= 0
      RETURNING "id", "balance"`;

    if (rows.length === 0) {
      const wallet = await tx.playerWallet.findUnique({
        where: { userId_currency: { userId: change.userId, currency: change.currency } },
      });
      if (!wallet) throw AppException.notFound('Wallet not found');
      throw AppException.conflict(ErrorCode.INSUFFICIENT_FUNDS, `Not enough ${change.currency}`, {
        currency: change.currency,
        required: -change.amount,
        balance: wallet.balance,
      });
    }

    const { id: walletId, balance: balanceAfter } = rows[0]!;
    const transaction = await tx.walletTransaction.create({
      data: {
        userId: change.userId,
        walletId,
        currency: change.currency,
        type: change.type,
        amount: change.amount,
        balanceBefore: balanceAfter - change.amount,
        balanceAfter,
        referenceType: change.referenceType,
        referenceId: change.referenceId,
        idempotencyKey: change.idempotencyKey,
        metadata: change.metadata,
      },
    });
    return { transaction, replayed: false };
  }

  async getBalances(userId: string) {
    const wallets = await this.prisma.playerWallet.findMany({ where: { userId }, orderBy: { currency: 'asc' } });
    return Object.fromEntries(wallets.map((w) => [w.currency, w.balance])) as Record<Currency, number>;
  }

  async recentTransactions(userId: string, take = 20) {
    return this.prisma.walletTransaction.findMany({
      where: { userId },
      orderBy: { createdAt: 'desc' },
      take,
      select: {
        id: true,
        currency: true,
        type: true,
        amount: true,
        balanceBefore: true,
        balanceAfter: true,
        referenceType: true,
        referenceId: true,
        createdAt: true,
      },
    });
  }
}
