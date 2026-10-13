import { HttpStatus, Injectable } from '@nestjs/common';
import { PurchaseProvider } from '@prisma/client';
import { ErrorCode, IAP_PRODUCTS, IapProductId, VerifyPurchaseRequest } from '@hwo/shared-contracts';
import { AppException } from '../common/app-exception';
import { sha256 } from '../common/crypto';
import { AppConfig } from '../config/app-config.service';
import { AuditService } from '../audit/audit.service';
import { PrismaService } from '../prisma/prisma.service';
import { WalletService } from '../wallet/wallet.service';
import { BillingAdapter } from './adapters/billing-adapter';
import { GooglePlayBillingAdapter } from './adapters/google-play-billing.adapter';
import { MockBillingAdapter } from './adapters/mock-billing.adapter';

@Injectable()
export class PurchasesService {
  private readonly adapters: Record<PurchaseProvider, BillingAdapter>;

  constructor(
    private readonly prisma: PrismaService,
    private readonly wallet: WalletService,
    private readonly audit: AuditService,
    config: AppConfig,
  ) {
    const mockAllowed = config.get('BILLING_MODE') === 'mock' && !config.isProduction;
    this.adapters = {
      DEV_MOCK: new MockBillingAdapter(mockAllowed),
      GOOGLE_PLAY: new GooglePlayBillingAdapter(config.get('GOOGLE_PLAY_PACKAGE_NAME'), config.get('GOOGLE_PLAY_SERVICE_ACCOUNT_JSON')),
    };
  }

  /**
   * Verifies a real-money purchase with the store, then grants it exactly once.
   * Safe to call repeatedly with the same token (client retries, app restarts).
   */
  async verify(userId: string, dto: VerifyPurchaseRequest) {
    const product = IAP_PRODUCTS[dto.productId as IapProductId];
    if (!product) throw AppException.badRequest(ErrorCode.UNKNOWN_PRODUCT, 'Unknown product');
    const adapter = this.adapters[dto.provider];
    if (!adapter.isConfigured()) {
      throw new AppException(ErrorCode.BILLING_NOT_CONFIGURED, `${dto.provider} billing is not enabled on this server`, HttpStatus.SERVICE_UNAVAILABLE);
    }

    const tokenHash = sha256(dto.purchaseToken);
    const existing = await this.prisma.purchase.findUnique({
      where: { provider_purchaseTokenHash: { provider: dto.provider, purchaseTokenHash: tokenHash } },
    });
    if (existing && existing.userId !== userId) {
      await this.audit.log({ actorUserId: userId, action: 'purchase.token_reuse_other_user', targetType: 'purchase', targetId: existing.id });
      throw AppException.conflict(ErrorCode.PURCHASE_TOKEN_IN_USE, 'This purchase belongs to another account');
    }
    if (existing?.status === 'GRANTED') return this.result(existing.id, 'GRANTED', product.gems, true);
    if (existing?.status === 'REFUNDED') throw AppException.badRequest(ErrorCode.PURCHASE_INVALID, 'Purchase was refunded');

    const v = await adapter.verify(dto.productId, dto.purchaseToken);
    const valid = v.valid && v.productId === dto.productId;

    return this.prisma.tx(async (tx) => {
      const purchase = await tx.purchase.upsert({
        where: { provider_purchaseTokenHash: { provider: dto.provider, purchaseTokenHash: tokenHash } },
        create: { userId, provider: dto.provider, productId: dto.productId, purchaseToken: dto.purchaseToken, purchaseTokenHash: tokenHash, orderId: v.orderId, isTest: v.isTest, status: 'PENDING' },
        update: {},
      });
      if (purchase.userId !== userId) throw AppException.conflict(ErrorCode.PURCHASE_TOKEN_IN_USE, 'This purchase belongs to another account');
      if (purchase.status === 'GRANTED') return this.result(purchase.id, 'GRANTED', product.gems, true);

      await tx.purchaseEvent.create({ data: { purchaseId: purchase.id, type: 'VERIFICATION', payload: { valid, state: v.state, orderId: v.orderId ?? null } } });

      if (!valid || v.state === 'CANCELLED') {
        await tx.purchase.update({ where: { id: purchase.id }, data: { status: 'FAILED' } });
        return { failed: true as const };
      }
      if (v.state === 'PENDING') return this.result(purchase.id, 'PENDING', 0, false);

      // Conditional flip: only one concurrent verifier can move it to GRANTED.
      const flipped = await tx.purchase.updateMany({
        where: { id: purchase.id, status: { in: ['PENDING', 'VERIFIED', 'FAILED'] } },
        data: { status: 'GRANTED', grantedAt: new Date(), orderId: v.orderId },
      });
      if (flipped.count !== 1) return this.result(purchase.id, 'GRANTED', product.gems, true);

      if (product.gems > 0) {
        await this.wallet.applyChange(tx, {
          userId,
          currency: 'GEMS',
          amount: product.gems,
          type: 'IAP_GRANT',
          idempotencyKey: `iap:${purchase.id}`,
          referenceType: 'purchase',
          referenceId: purchase.id,
        });
      }
      await tx.purchaseEvent.create({ data: { purchaseId: purchase.id, type: 'GRANTED', payload: { gems: product.gems } } });
      await this.audit.log({ actorUserId: userId, action: 'purchase.granted', targetType: 'purchase', targetId: purchase.id, metadata: { productId: dto.productId, provider: dto.provider, isTest: v.isTest } }, tx);
      return this.result(purchase.id, 'GRANTED', product.gems, false);
    }).then((r) => {
      if ('failed' in r) throw AppException.badRequest(ErrorCode.PURCHASE_INVALID, 'Purchase could not be verified');
      return r;
    });
  }

  private result(purchaseId: string, status: 'GRANTED' | 'PENDING', gemsGranted: number, alreadyProcessed: boolean) {
    return { purchaseId, status, gemsGranted, alreadyProcessed };
  }
}
