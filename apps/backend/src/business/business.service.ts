import { Injectable } from '@nestjs/common';
import {
  BUSINESS_MAX_LEVEL,
  BUSINESS_PRODUCT_CODES,
  BUSINESS_PRODUCTS,
  BusinessProductCode,
  businessLevelConfig,
  ErrorCode,
  RestockRequest,
  SetPriceRequest,
} from '@hwo/shared-contracts';
import { randomUUID } from 'node:crypto';
import { AppException } from '../common/app-exception';
import { AppConfig } from '../config/app-config.service';
import { AuditService } from '../audit/audit.service';
import { PrismaService, Tx } from '../prisma/prisma.service';
import { QuestsService } from '../quests/quests.service';
import { WalletService } from '../wallet/wallet.service';
import { SALE_TIP_TEXT_TH, simulateSaleRound, weightedAvgCost } from './business-sim';
import { LivingShopService, SettleResult } from './living-shop.service';

/**
 * Simulated shop. Uses in-game Gold only; it is a learning simulation, not an investment product.
 * Every mutation first locks the business row (SELECT ... FOR UPDATE) after checking ownership,
 * so concurrent requests on one shop are serialized.
 */
@Injectable()
export class BusinessService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly wallet: WalletService,
    private readonly quests: QuestsService,
    private readonly audit: AuditService,
    private readonly config: AppConfig,
    private readonly shop: LivingShopService,
  ) {}

  async list(userId: string) {
    await this.shop.settleOwned(userId);
    const rows = await this.prisma.business.findMany({
      where: { userId, deletedAt: null },
      include: { inventory: { orderBy: { productCode: 'asc' } } },
      orderBy: { createdAt: 'asc' },
    });
    return rows.map((b) => this.present(b));
  }

  async get(userId: string, id: string) {
    await this.shop.settleOwned(userId, id);
    const b = await this.prisma.business.findFirst({
      where: { id, userId, deletedAt: null },
      include: { inventory: { orderBy: { productCode: 'asc' } } },
    });
    // Same 404 for "missing" and "someone else's" so ids cannot be probed.
    if (!b) throw AppException.notFound('Business not found');
    return this.present(b);
  }

  async setPrice(userId: string, id: string, dto: SetPriceRequest) {
    const code = dto.productCode as BusinessProductCode;
    const p = BUSINESS_PRODUCTS[code];
    if (dto.price < p.minPrice || dto.price > p.maxPrice) {
      throw AppException.badRequest(ErrorCode.BUSINESS_PRICE_OUT_OF_RANGE, `Price must be between ${p.minPrice} and ${p.maxPrice}`, {
        min: p.minPrice,
        max: p.maxPrice,
      });
    }
    const settled: SettleResult[] = [];
    await this.prisma.tx(async (tx) => {
      const biz = await this.lock(tx, userId, id, settled);
      this.assertUnlocked(code, biz.level);
      await tx.businessInventory.upsert({
        where: { businessId_productCode: { businessId: id, productCode: code } },
        create: { businessId: id, productCode: code, quantity: 0, sellPrice: dto.price },
        update: { sellPrice: dto.price },
      });
    });
    settled.forEach((r) => this.shop.publish(r));
    return this.get(userId, id);
  }

  async restock(userId: string, id: string, dto: RestockRequest, idempotencyKey: string) {
    const code = dto.productCode as BusinessProductCode;
    const product = BUSINESS_PRODUCTS[code];
    const cost = product.unitCost * dto.quantity;

    const settled: SettleResult[] = [];
    const replayed = await this.prisma.tx(async (tx) => {
      const biz = await this.lock(tx, userId, id, settled);
      this.assertUnlocked(code, biz.level);
      const line = await tx.businessInventory.findUnique({ where: { businessId_productCode: { businessId: id, productCode: code } } });

      const pay = await this.wallet.applyChange(tx, {
        userId,
        currency: 'GOLD',
        amount: -cost,
        type: 'BUSINESS_RESTOCK',
        idempotencyKey: `restock:${idempotencyKey}`,
        referenceType: 'business',
        referenceId: `${id}:${code}:${dto.quantity}`,
      });
      if (pay.replayed) return true;

      const current = line?.quantity ?? 0;
      const { stockCapacity } = businessLevelConfig(biz.level);
      if (current + dto.quantity > stockCapacity) {
        throw AppException.conflict(ErrorCode.BUSINESS_STOCK_FULL, `Stock limit is ${stockCapacity}`, { stockCapacity, current });
      }
      await tx.businessInventory.upsert({
        where: { businessId_productCode: { businessId: id, productCode: code } },
        create: { businessId: id, productCode: code, quantity: dto.quantity, avgUnitCost: product.unitCost, sellPrice: product.basePrice },
        // goods already on the market stall share the same average cost
        update: {
          quantity: { increment: dto.quantity },
          avgUnitCost: weightedAvgCost(current + (line?.shelfQuantity ?? 0), line?.avgUnitCost ?? 0, dto.quantity, product.unitCost),
        },
      });
      await tx.business.update({ where: { id }, data: { totalExpense: { increment: cost } } });
      await this.quests.recordEvent(tx, userId, 'BUSINESS_RESTOCK', dto.quantity);
      return false;
    });
    settled.forEach((r) => this.shop.publish(r));
    return { replayed, cost, business: await this.get(userId, id) };
  }

  /** Runs one sales round. Revenue is computed entirely on the server from stored price and stock. */
  async sell(userId: string, id: string) {
    const cooldown = this.config.get('BUSINESS_SALE_COOLDOWN_SECONDS');
    const settled: SettleResult[] = [];
    const summary = await this.prisma.tx(async (tx) => {
      const biz = await this.lock(tx, userId, id, settled);
      const now = new Date();
      if (biz.lastSaleAt && cooldown > 0) {
        const readyAt = biz.lastSaleAt.getTime() + cooldown * 1000;
        if (readyAt > now.getTime()) {
          throw AppException.conflict(ErrorCode.BUSINESS_SALE_COOLDOWN, 'Customers are still on their way', {
            retryAfterSeconds: Math.ceil((readyAt - now.getTime()) / 1000),
          });
        }
      }
      const stock = await tx.businessInventory.findMany({ where: { businessId: id }, orderBy: { productCode: 'asc' } });
      if (stock.every((s) => s.quantity === 0)) {
        throw AppException.conflict(ErrorCode.BUSINESS_NOTHING_TO_SELL, SALE_TIP_TEXT_TH.NO_STOCK);
      }

      const lines = simulateSaleRound(
        biz.level,
        stock.filter((s) => s.quantity > 0).map((s) => ({ productCode: s.productCode as BusinessProductCode, quantity: s.quantity, avgUnitCost: s.avgUnitCost, sellPrice: s.sellPrice })),
      );
      const revenue = lines.reduce((a, l) => a + l.revenue, 0);
      const units = lines.reduce((a, l) => a + l.unitsSold, 0);
      const profit = lines.reduce((a, l) => a + l.profit, 0);

      for (const l of lines) {
        if (l.unitsSold === 0) continue;
        const dec = await tx.businessInventory.updateMany({
          where: { businessId: id, productCode: l.productCode, quantity: { gte: l.unitsSold } },
          data: { quantity: { decrement: l.unitsSold } },
        });
        if (dec.count !== 1) throw new Error('Stock changed during sale'); // impossible under row lock
      }
      if (revenue > 0) {
        await this.wallet.applyChange(tx, {
          userId,
          currency: 'GOLD',
          amount: revenue,
          type: 'BUSINESS_SALE',
          idempotencyKey: `sale:${randomUUID()}`,
          referenceType: 'business',
          referenceId: id,
          metadata: { units, profit },
        });
      }
      await tx.business.update({
        where: { id },
        data: { lastSaleAt: now, totalRevenue: { increment: revenue }, totalUnitsSold: { increment: units } },
      });
      await this.quests.recordEvent(tx, userId, 'BUSINESS_SELL_UNITS', units);
      if (profit > 0) await this.quests.recordEvent(tx, userId, 'BUSINESS_EARN_PROFIT', profit);

      return {
        lines: lines.map((l) => ({ ...l, tipsText: l.tips.map((t) => SALE_TIP_TEXT_TH[t]) })),
        totals: { unitsSold: units, revenue, costOfGoods: revenue - profit, profit },
        nextSaleAvailableAt: new Date(now.getTime() + cooldown * 1000).toISOString(),
      };
    });
    settled.forEach((r) => this.shop.publish(r));
    return { ...summary, business: await this.get(userId, id) };
  }

  /** Upgrades one level. `expectedLevel` + a deterministic ledger key make double-taps harmless. */
  async upgrade(userId: string, id: string, expectedLevel: number) {
    const settled: SettleResult[] = [];
    const replayed = await this.prisma.tx(async (tx) => {
      const biz = await this.lock(tx, userId, id, settled);
      const idempotencyKey = `business-upgrade:${id}:${expectedLevel + 1}`;
      if (biz.level !== expectedLevel) {
        const done = await tx.walletTransaction.findUnique({ where: { userId_idempotencyKey: { userId, idempotencyKey } } });
        if (done) return true;
        throw AppException.conflict(ErrorCode.BUSINESS_LEVEL_CHANGED, 'Shop level changed, refresh and try again', { level: biz.level });
      }
      const cfg = businessLevelConfig(biz.level);
      if (cfg.upgradeCost === null || biz.level >= BUSINESS_MAX_LEVEL) {
        throw AppException.conflict(ErrorCode.BUSINESS_MAX_LEVEL, 'Shop is already at max level');
      }
      await this.wallet.applyChange(tx, {
        userId,
        currency: 'GOLD',
        amount: -cfg.upgradeCost,
        type: 'BUSINESS_UPGRADE',
        idempotencyKey,
        referenceType: 'business',
        referenceId: id,
      });
      await tx.business.update({
        where: { id },
        data: { level: biz.level + 1, totalExpense: { increment: cfg.upgradeCost } },
      });
      await this.quests.recordEvent(tx, userId, 'BUSINESS_UPGRADE');
      await this.audit.log({ actorUserId: userId, action: 'business.upgraded', targetType: 'business', targetId: id, metadata: { toLevel: biz.level + 1, cost: cfg.upgradeCost } }, tx);
      return false;
    });
    settled.forEach((r) => this.shop.publish(r));
    return { replayed, business: await this.get(userId, id) };
  }

  /**
   * Locks the shop row and first settles any NPC customers that are due, so the change being made
   * (price, stock, level) never applies retroactively to customers who already came. What the settle
   * produced goes to `settled` and is published by the caller once the transaction has committed.
   */
  private async lock(tx: Tx, userId: string, id: string, settled: SettleResult[]) {
    const shop = await this.shop.lock(tx, userId, id);
    settled.length = 0; // a retried transaction starts over
    const res = await this.shop.settleLocked(tx, shop);
    if (res) settled.push(res);
    return { id: shop.id, level: shop.level, lastSaleAt: shop.lastSaleAt };
  }

  private assertUnlocked(code: BusinessProductCode, level: number): void {
    const unlockLevel = BUSINESS_PRODUCTS[code].unlockLevel;
    if (unlockLevel > level) {
      throw AppException.conflict(ErrorCode.BUSINESS_PRODUCT_LOCKED, `Unlocks at shop level ${unlockLevel}`, { unlockLevel });
    }
  }

  private present(b: {
    id: string;
    name: string;
    level: number;
    totalRevenue: number;
    totalExpense: number;
    totalUnitsSold: number;
    lastSaleAt: Date | null;
    shopStatus: string;
    stallSlot: number | null;
    cashBox: number;
    totalCustomers: number;
    inventory: { productCode: string; quantity: number; avgUnitCost: number; sellPrice: number; shelfQuantity: number; onStall: boolean }[];
  }) {
    const cfg = businessLevelConfig(b.level);
    return {
      id: b.id,
      name: b.name,
      level: b.level,
      maxLevel: BUSINESS_MAX_LEVEL,
      upgradeCost: cfg.upgradeCost,
      stockCapacity: cfg.stockCapacity,
      salesCapacity: cfg.salesCapacity,
      finance: { totalRevenue: b.totalRevenue, totalExpense: b.totalExpense, profit: b.totalRevenue - b.totalExpense },
      totalUnitsSold: b.totalUnitsSold,
      lastSaleAt: b.lastSaleAt,
      products: b.inventory.map((i) => {
        const p = BUSINESS_PRODUCTS[i.productCode as BusinessProductCode];
        return {
          ...i,
          unitCost: p.unitCost,
          minPrice: p.minPrice,
          maxPrice: p.maxPrice,
          basePrice: p.basePrice,
          unlockLevel: p.unlockLevel,
        };
      }),
      // ---- living shop (Phase 5) ----
      stall: {
        status: b.shopStatus,
        slot: b.stallSlot,
        cashBox: b.cashBox,
        totalCustomers: b.totalCustomers,
        shelfSlots: cfg.shelfSlots,
        shelfCapacity: cfg.shelfCapacity,
        customersPerMinute: cfg.customersPerMinute,
        slotsUsed: b.inventory.filter((i) => i.onStall).length,
      },
      /** every product of the game, so the client can show what unlocks at which level */
      catalog: BUSINESS_PRODUCT_CODES.map((code) => {
        const p = BUSINESS_PRODUCTS[code];
        return { productCode: code, unitCost: p.unitCost, minPrice: p.minPrice, maxPrice: p.maxPrice, basePrice: p.basePrice, unlockLevel: p.unlockLevel, unlocked: p.unlockLevel <= b.level };
      }),
    };
  }
}
