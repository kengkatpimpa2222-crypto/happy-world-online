import { Injectable, Logger } from '@nestjs/common';
import { AgeBand, Prisma, ShopStatus } from '@prisma/client';
import {
  BUSINESS_PRODUCTS,
  BusinessProductCode,
  businessLevelConfig,
  ErrorCode,
  MARKET_STALL_SLOTS,
  MARKET_ZONE_ID,
  marketStallPosition,
  SetShelfRequest,
  SHOP_MAX_OFFLINE_TICKS,
  SHOP_TICK_SECONDS,
  WsEvent,
} from '@hwo/shared-contracts';
import { AppException } from '../common/app-exception';
import { AuthUser } from '../common/decorators';
import { AuditService } from '../audit/audit.service';
import { FestivalContributionResult, FestivalService } from '../festival/festival.service';
import { isDisplayNameAllowed } from '../player/name-filter';
import { PrismaService, Tx } from '../prisma/prisma.service';
import { QuestsService } from '../quests/quests.service';
import { RealtimeService } from '../realtime/realtime.service';
import { SocialGraphService } from '../social/social-graph.service';
import { WalletService } from '../wallet/wallet.service';
import { ProductTotals, SHOP_TIP_TEXT_TH, shopTips, SimulationResult, simulateCustomers, StallLine } from './living-shop-sim';

/** A business row locked with SELECT ... FOR UPDATE for the rest of the transaction. */
export interface LockedShop {
  id: string;
  userId: string;
  level: number;
  shopStatus: ShopStatus;
  stallSlot: number | null;
  marketAgeBand: AgeBand | null;
  cashBox: number;
  settledTick: number | null;
  lastSaleAt: Date | null;
}

export interface SettleResult {
  shop: LockedShop;
  toTick: number;
  sim: SimulationResult;
  /** stall after the customers (for the owner's live HUD) */
  stall: StallLine[];
  cashBox: number;
  /** Phase 6: featured units that counted toward the city festival */
  festival: FestivalContributionResult;
}

interface LockedRow {
  id: string;
  user_id: string;
  level: number;
  shop_status: ShopStatus;
  stall_slot: number | null;
  market_age_band: AgeBand | null;
  cash_box: number;
  settled_tick: number | null;
  last_sale_at: Date | null;
}

/** Max visits per market:activity message (the client animates them; more is just noise). */
const MAX_ACTIVITY_VISITS = 8;

/**
 * The "living shop": a market stall that NPC customers visit in real time, online or offline.
 *
 * Money rules (same as the rest of the economy):
 * - Customers pay into the shop's cash box; only `collect` moves Gold into the wallet, through
 *   WalletService (ledger row + idempotency key).
 * - Every change that affects sales settles the pending ticks FIRST, under the same row lock,
 *   so a new price or stock level never applies to customers who came earlier.
 * - Ticks are deterministic per (business, tick) and `settled_tick` only moves forward under the
 *   lock, so a tick can never be paid twice, from any number of backend instances.
 */
@Injectable()
export class LivingShopService {
  private readonly logger = new Logger('LivingShop');

  constructor(
    private readonly prisma: PrismaService,
    private readonly wallet: WalletService,
    private readonly quests: QuestsService,
    private readonly realtime: RealtimeService,
    private readonly graph: SocialGraphService,
    private readonly audit: AuditService,
    private readonly festival: FestivalService,
  ) {}

  nowTick(at = Date.now()): number {
    return Math.floor(at / 1000 / SHOP_TICK_SECONDS);
  }

  // ---------- locking & settling ----------

  /** Locks the player's own shop (404 for missing and for someone else's shop alike). */
  async lock(tx: Tx, userId: string, id: string): Promise<LockedShop> {
    const rows = await tx.$queryRaw<LockedRow[]>`
      SELECT "id", "user_id", "level", "shop_status", "stall_slot", "market_age_band", "cash_box", "settled_tick", "last_sale_at"
        FROM "businesses"
       WHERE "id" = ${id}::uuid AND "user_id" = ${userId}::uuid AND "deleted_at" IS NULL
       FOR UPDATE`;
    if (!rows[0]) throw AppException.notFound('Business not found');
    return toLocked(rows[0]);
  }

  /** Background variant: skips a shop another request is working on right now. */
  private async lockOpenForSettler(tx: Tx, id: string): Promise<LockedShop | null> {
    const rows = await tx.$queryRaw<LockedRow[]>`
      SELECT "id", "user_id", "level", "shop_status", "stall_slot", "market_age_band", "cash_box", "settled_tick", "last_sale_at"
        FROM "businesses"
       WHERE "id" = ${id}::uuid AND "shop_status" = 'OPEN'::"ShopStatus" AND "deleted_at" IS NULL
       FOR UPDATE SKIP LOCKED`;
    return rows[0] ? toLocked(rows[0]) : null;
  }

  /**
   * Runs NPC customers for every tick since the last settle, up to `nowTick`. Call with the row locked.
   * Returns null when nothing happened (shop not open, no new tick, or nothing on the stall).
   */
  async settleLocked(tx: Tx, shop: LockedShop, nowTick = this.nowTick()): Promise<SettleResult | null> {
    if (shop.shopStatus !== ShopStatus.OPEN) return null;
    const last = shop.settledTick ?? nowTick;
    const from = Math.max(last, nowTick - SHOP_MAX_OFFLINE_TICKS);
    if (from >= nowTick) return null;

    const lines = await tx.businessInventory.findMany({ where: { businessId: shop.id, onStall: true }, orderBy: { productCode: 'asc' } });
    const stallIn: StallLine[] = lines.map((l) => ({
      productCode: l.productCode as BusinessProductCode,
      shelfQuantity: l.shelfQuantity,
      avgUnitCost: l.avgUnitCost,
      sellPrice: l.sellPrice,
    }));
    const sim = simulateCustomers(shop.id, shop.level, from, nowTick, stallIn, this.festival.festivalAtTick);

    for (const [i, after] of sim.stall.entries()) {
      const sold = stallIn[i]!.shelfQuantity - after.shelfQuantity;
      if (sold <= 0) continue;
      const dec = await tx.businessInventory.updateMany({
        where: { businessId: shop.id, productCode: after.productCode, shelfQuantity: { gte: sold } },
        data: { shelfQuantity: { decrement: sold } },
      });
      if (dec.count !== 1) throw new Error('Stall changed during settlement'); // impossible under the row lock
    }

    const t = sim.totals;
    await tx.business.update({
      where: { id: shop.id },
      data: {
        settledTick: nowTick,
        cashBox: { increment: t.revenue },
        totalRevenue: { increment: t.revenue },
        totalUnitsSold: { increment: t.unitsSold },
        totalCustomers: { increment: t.customers },
      },
    });
    if (t.customers > 0) {
      await tx.shopSettlement.create({
        data: {
          businessId: shop.id,
          fromTick: from,
          toTick: nowTick,
          customers: t.customers,
          buyers: t.buyers,
          unitsSold: t.unitsSold,
          revenue: t.revenue,
          costOfGoods: t.costOfGoods,
          lostTooExpensive: t.lostTooExpensive,
          lostSoldOut: t.lostSoldOut,
          lostNotSelling: t.lostNotSelling,
          lines: sim.perProduct as Prisma.InputJsonValue,
        },
      });
    }
    await this.quests.recordEvent(tx, shop.userId, 'BUSINESS_SELL_UNITS', t.unitsSold);
    if (t.profit > 0) await this.quests.recordEvent(tx, shop.userId, 'BUSINESS_EARN_PROFIT', t.profit);
    await this.quests.recordEvent(tx, shop.userId, 'SHOP_SERVE_CUSTOMERS', t.buyers);
    const festival = await this.festival.recordSales(tx, shop.userId, sim.festivalPoints);

    return { shop, toTick: nowTick, sim, stall: sim.stall, cashBox: shop.cashBox + t.revenue, festival };
  }

  /** Lazy settle of the player's own open shop(s) before showing them. */
  async settleOwned(userId: string, id?: string): Promise<void> {
    const now = this.nowTick();
    const due = await this.prisma.business.findMany({
      where: { userId, deletedAt: null, shopStatus: ShopStatus.OPEN, settledTick: { lt: now }, ...(id ? { id } : {}) },
      select: { id: true },
    });
    for (const { id: shopId } of due) {
      const res = await this.prisma.tx(async (tx) => this.settleLocked(tx, await this.lock(tx, userId, shopId), now));
      if (res) this.publish(res);
    }
  }

  /** Settles open shops whose customers are due. Used by the background settler; returns how many settled. */
  async settleDue(limit = 100): Promise<number> {
    const now = this.nowTick();
    const due = await this.prisma.$queryRaw<{ id: string }[]>`
      SELECT "id" FROM "businesses"
       WHERE "shop_status" = 'OPEN'::"ShopStatus" AND "deleted_at" IS NULL AND "settled_tick" < ${now}
       ORDER BY "settled_tick" ASC
       LIMIT ${limit}`;
    let settled = 0;
    for (const { id } of due) {
      try {
        const res = await this.prisma.tx(async (tx) => {
          const shop = await this.lockOpenForSettler(tx, id);
          return shop ? this.settleLocked(tx, shop, now) : null;
        });
        if (res) {
          settled++;
          this.publish(res);
        }
      } catch (e) {
        this.logger.warn(`Settling shop ${id} failed: ${e instanceof Error ? e.message : String(e)}`);
      }
    }
    return settled;
  }

  /** Live updates after a settle: the owner's HUD, and the customers' walk for players in the market. */
  publish(res: SettleResult): void {
    const { shop, sim } = res;
    this.festival.publishMilestones(res.festival.milestones);
    if (sim.totals.customers === 0) return;
    this.realtime.notifyUser(shop.userId, WsEvent.SHOP_UPDATED, {
      businessId: shop.id,
      cashBox: res.cashBox,
      customers: sim.totals.customers,
      buyers: sim.totals.buyers,
      unitsSold: sim.totals.unitsSold,
      revenue: sim.totals.revenue,
      stall: res.stall.map((l) => ({ productCode: l.productCode, shelfQuantity: l.shelfQuantity })),
      festivalPoints: Object.values(res.festival.added).reduce((a, b) => a + b, 0),
    });
    if (shop.stallSlot === null || !shop.marketAgeBand) return;
    // Only the latest tick is animated; older ticks were "while nobody was watching".
    const visits = sim.visits
      .filter((v) => v.tick === res.toTick)
      .slice(0, MAX_ACTIVITY_VISITS)
      .map((v) => ({ index: v.index, customerType: v.customerType, productCode: v.productCode, outcome: v.outcome, quantity: v.quantity }));
    if (visits.length === 0) return;
    this.realtime.toZone(MARKET_ZONE_ID, shop.marketAgeBand, WsEvent.MARKET_ACTIVITY, {
      slot: shop.stallSlot,
      businessId: shop.id,
      tick: res.toTick,
      tickSeconds: SHOP_TICK_SECONDS,
      visits,
    });
  }

  private stallChanged(ageBand: AgeBand | null, slot: number | null): void {
    if (ageBand && slot !== null) this.realtime.toZone(MARKET_ZONE_ID, ageBand, WsEvent.MARKET_STALL_CHANGED, { slot });
  }

  // ---------- owner actions ----------

  async rename(userId: string, id: string, name: string): Promise<void> {
    if (!isDisplayNameAllowed(name)) throw AppException.badRequest(ErrorCode.SHOP_NAME_REJECTED, 'This shop name is not allowed');
    const shop = await this.prisma.tx(async (tx) => {
      const s = await this.lock(tx, userId, id);
      await tx.business.update({ where: { id }, data: { name } });
      return s;
    });
    this.stallChanged(shop.marketAgeBand, shop.stallSlot);
  }

  /**
   * Sets how many units of one product stand on the stall, moving goods between the shop's
   * storage (`quantity`) and the stall (`shelf_quantity`). 0 takes the product off the stall.
   */
  async setShelf(userId: string, id: string, dto: SetShelfRequest): Promise<void> {
    const code = dto.productCode as BusinessProductCode;
    const res = await this.prisma.tx(async (tx) => {
      const shop = await this.lock(tx, userId, id);
      const settled = await this.settleLocked(tx, shop);
      const cfg = businessLevelConfig(shop.level);
      if (BUSINESS_PRODUCTS[code].unlockLevel > shop.level) {
        throw AppException.conflict(ErrorCode.BUSINESS_PRODUCT_LOCKED, `Unlocks at shop level ${BUSINESS_PRODUCTS[code].unlockLevel}`, {
          unlockLevel: BUSINESS_PRODUCTS[code].unlockLevel,
        });
      }
      if (dto.quantity > cfg.shelfCapacity) {
        throw AppException.conflict(ErrorCode.SHOP_SHELF_FULL, `The stall holds at most ${cfg.shelfCapacity} of one product`, {
          shelfCapacity: cfg.shelfCapacity,
        });
      }
      const lines = await tx.businessInventory.findMany({ where: { businessId: id } });
      const line = lines.find((l) => l.productCode === code);
      if (dto.quantity > 0 && !line?.onStall) {
        const used = lines.filter((l) => l.onStall).length;
        if (used >= cfg.shelfSlots) {
          throw AppException.conflict(ErrorCode.SHOP_SHELF_FULL, `The stall has room for ${cfg.shelfSlots} products`, { shelfSlots: cfg.shelfSlots });
        }
      }
      const onShelf = line?.shelfQuantity ?? 0;
      const delta = dto.quantity - onShelf;
      const inStorage = line?.quantity ?? 0;
      if (delta > inStorage) {
        throw AppException.conflict(ErrorCode.SHOP_NOT_ENOUGH_STOCK, 'Not enough goods in storage, restock first', { inStorage, needed: delta });
      }
      if (!line) {
        // dto.quantity must be 0 here (nothing in storage): nothing to do
        return { shop, settled };
      }
      await tx.businessInventory.update({
        where: { id: line.id },
        data: { quantity: inStorage - delta, shelfQuantity: dto.quantity, onStall: dto.quantity > 0 },
      });
      return { shop, settled };
    });
    if (res.settled) this.publish(res.settled);
    this.stallChanged(res.shop.marketAgeBand, res.shop.stallSlot);
  }

  /** OPEN takes a stall spot in the market (if one is free) and starts customers from now on. */
  async setStatus(user: AuthUser, id: string, status: ShopStatus): Promise<void> {
    const res = await this.prisma.tx(async (tx) => {
      const shop = await this.lock(tx, user.userId, id);
      const settled = await this.settleLocked(tx, shop);
      const before = { ageBand: shop.marketAgeBand, slot: shop.stallSlot };
      if (status === shop.shopStatus) return { settled, before, after: before };

      if (status === ShopStatus.OPEN) {
        const ready = await tx.businessInventory.count({ where: { businessId: id, onStall: true, shelfQuantity: { gt: 0 } } });
        if (ready === 0) throw AppException.conflict(ErrorCode.SHOP_SHELF_EMPTY, 'Put some goods on the stall first');
        const slot = shop.stallSlot !== null && shop.marketAgeBand === user.ageBand ? shop.stallSlot : await this.freeSlot(tx, user.ageBand);
        await tx.business.update({
          where: { id },
          data: {
            shopStatus: ShopStatus.OPEN,
            settledTick: this.nowTick(),
            stallSlot: slot,
            marketAgeBand: slot === null ? null : user.ageBand,
            ...(shop.shopStatus === ShopStatus.CLOSED ? { openedAt: new Date() } : {}),
          },
        });
        await this.quests.recordEvent(tx, user.userId, 'SHOP_OPEN');
        await this.audit.log({ actorUserId: user.userId, action: 'shop.opened', targetType: 'business', targetId: id, metadata: { slot } }, tx);
        return { settled, before, after: { ageBand: slot === null ? null : user.ageBand, slot } };
      }
      if (status === ShopStatus.PAUSED) {
        if (shop.shopStatus === ShopStatus.CLOSED) return { settled, before, after: before }; // nothing to pause
        await tx.business.update({ where: { id }, data: { shopStatus: ShopStatus.PAUSED } });
        return { settled, before, after: before };
      }
      await tx.business.update({ where: { id }, data: { shopStatus: ShopStatus.CLOSED, stallSlot: null, marketAgeBand: null } });
      return { settled, before, after: { ageBand: null, slot: null } };
    });
    if (res.settled) this.publish(res.settled);
    this.stallChanged(res.before.ageBand, res.before.slot);
    if (res.after.slot !== res.before.slot || res.after.ageBand !== res.before.ageBand) this.stallChanged(res.after.ageBand, res.after.slot);
  }

  /** Lowest free stall spot in this age band's market, or null when the market is full. */
  private async freeSlot(tx: Tx, ageBand: AgeBand): Promise<number | null> {
    const taken = await tx.business.findMany({ where: { marketAgeBand: ageBand, stallSlot: { not: null } }, select: { stallSlot: true } });
    const used = new Set(taken.map((t) => t.stallSlot));
    for (let s = 0; s < MARKET_STALL_SLOTS; s++) if (!used.has(s)) return s;
    return null;
  }

  /** Moves everything in the cash box into the wallet. A repeated Idempotency-Key changes nothing. */
  async collect(userId: string, id: string, idempotencyKey: string) {
    const ledgerKey = `shop-collect:${idempotencyKey}`;
    const res = await this.prisma.tx(async (tx) => {
      const shop = await this.lock(tx, userId, id);
      const done = await tx.walletTransaction.findUnique({ where: { userId_idempotencyKey: { userId, idempotencyKey: ledgerKey } } });
      if (done) {
        if (done.referenceType !== 'shop-collect' || done.referenceId !== id) {
          throw AppException.conflict(ErrorCode.IDEMPOTENCY_KEY_REUSED, 'Idempotency-Key was already used for a different request');
        }
        return { replayed: true, collected: done.amount, settled: null };
      }
      const settled = await this.settleLocked(tx, shop);
      const amount = settled ? settled.cashBox : shop.cashBox;
      if (amount <= 0) throw AppException.conflict(ErrorCode.SHOP_NOTHING_TO_COLLECT, 'The cash box is empty');
      await this.wallet.applyChange(tx, {
        userId,
        currency: 'GOLD',
        amount,
        type: 'BUSINESS_SALE',
        idempotencyKey: ledgerKey,
        referenceType: 'shop-collect',
        referenceId: id,
        metadata: { source: 'npc_customers' },
      });
      const dec = await tx.business.updateMany({ where: { id, cashBox: { gte: amount } }, data: { cashBox: { decrement: amount } } });
      if (dec.count !== 1) throw new Error('Cash box changed during collect'); // impossible under the row lock
      return { replayed: false, collected: amount, settled };
    });
    if (res.settled) this.publish(res.settled);
    const balances = await this.wallet.getBalances(userId);
    return { replayed: res.replayed, collected: res.collected, wallet: balances };
  }

  // ---------- reports ----------

  /** What happened at the stall since the player last read the report ("while you were away"). */
  async report(userId: string, id: string) {
    await this.settleOwned(userId, id);
    const b = await this.prisma.business.findFirst({
      where: { id, userId, deletedAt: null },
      include: { inventory: { orderBy: { productCode: 'asc' } } },
    });
    if (!b) throw AppException.notFound('Business not found');
    const since = b.reportSeenAt ?? b.openedAt ?? b.createdAt;
    const until = new Date();
    const rows = await this.prisma.shopSettlement.findMany({
      where: { businessId: id, createdAt: { gt: since, lte: until } },
      orderBy: { createdAt: 'asc' },
      take: 5000,
    });

    const t = { customers: 0, buyers: 0, unitsSold: 0, revenue: 0, costOfGoods: 0, lostTooExpensive: 0, lostSoldOut: 0, lostNotSelling: 0 };
    const per = new Map<string, ProductTotals>();
    for (const r of rows) {
      t.customers += r.customers;
      t.buyers += r.buyers;
      t.unitsSold += r.unitsSold;
      t.revenue += r.revenue;
      t.costOfGoods += r.costOfGoods;
      t.lostTooExpensive += r.lostTooExpensive;
      t.lostSoldOut += r.lostSoldOut;
      t.lostNotSelling += r.lostNotSelling;
      for (const [code, l] of Object.entries((r.lines ?? {}) as unknown as Record<string, Partial<ProductTotals>>)) {
        const p = per.get(code) ?? { unitsSold: 0, revenue: 0, costOfGoods: 0, wanted: 0, lostTooExpensive: 0, lostSoldOut: 0 };
        p.unitsSold += l.unitsSold ?? 0;
        p.revenue += l.revenue ?? 0;
        p.costOfGoods += l.costOfGoods ?? 0;
        p.wanted += l.wanted ?? 0;
        p.lostTooExpensive += l.lostTooExpensive ?? 0;
        p.lostSoldOut += l.lostSoldOut ?? 0;
        per.set(code, p);
      }
    }
    const profit = t.revenue - t.costOfGoods;
    const stall = new Map(b.inventory.map((i) => [i.productCode, i]));
    const codes = new Set([...per.keys(), ...b.inventory.filter((i) => i.onStall).map((i) => i.productCode)]);
    const products = [...codes].sort().map((code) => {
      const p = per.get(code) ?? { unitsSold: 0, revenue: 0, costOfGoods: 0, wanted: 0, lostTooExpensive: 0, lostSoldOut: 0 };
      const line = stall.get(code);
      return {
        productCode: code,
        ...p,
        profit: p.revenue - p.costOfGoods,
        onStall: line?.onStall ?? false,
        shelfLeft: line?.shelfQuantity ?? 0,
        price: line?.sellPrice ?? null,
      };
    });
    const best = products.filter((p) => p.unitsSold > 0).sort((a, b2) => b2.profit - a.profit || b2.unitsSold - a.unitsSold)[0];
    const belowCost = b.inventory.some((i) => i.onStall && i.sellPrice <= i.avgUnitCost);
    const tips = shopTips({ ...t, profit }, belowCost).map((code) => ({ code, textTh: SHOP_TIP_TEXT_TH[code] }));
    return {
      businessId: b.id,
      shopName: b.name,
      status: b.shopStatus,
      since,
      until,
      hasNews: t.customers > 0,
      ...t,
      profit,
      cashBox: b.cashBox,
      bestSeller: best ? best.productCode : null,
      products,
      tips,
    };
  }

  async ackReport(userId: string, id: string, until?: string): Promise<void> {
    const at = until ? new Date(until) : new Date();
    const now = new Date();
    const seen = at > now ? now : at;
    const res = await this.prisma.business.updateMany({
      where: { id, userId, deletedAt: null, OR: [{ reportSeenAt: null }, { reportSeenAt: { lt: seen } }] },
      data: { reportSeenAt: seen },
    });
    if (res.count === 0 && !(await this.prisma.business.findFirst({ where: { id, userId, deletedAt: null }, select: { id: true } }))) {
      throw AppException.notFound('Business not found');
    }
  }

  // ---------- market view ----------

  /**
   * Stalls in the viewer's market: same age band only (child safety), never a player blocked
   * either way. Closed shops have no stall.
   */
  async market(user: AuthUser) {
    await this.settleOwned(user.userId);
    const [blocked, friendIds] = await Promise.all([this.graph.blockedEitherWay(user.userId), this.graph.friendIds(user.userId)]);
    const friends = new Set(friendIds);
    const rows = await this.prisma.business.findMany({
      where: { marketAgeBand: user.ageBand, stallSlot: { not: null }, deletedAt: null, shopStatus: { not: ShopStatus.CLOSED } },
      include: {
        inventory: { where: { onStall: true }, orderBy: { productCode: 'asc' } },
        user: { select: { profile: { select: { displayName: true } } } },
      },
      orderBy: { stallSlot: 'asc' },
    });
    const stalls = rows
      .filter((b) => b.userId === user.userId || !blocked.has(b.userId))
      .map((b) => {
        const pos = marketStallPosition(b.stallSlot!);
        return {
          slot: b.stallSlot!,
          x: pos.x,
          y: pos.y,
          face: pos.face,
          businessId: b.id,
          name: b.name,
          ownerId: b.userId,
          ownerName: b.user.profile?.displayName ?? '',
          level: b.level,
          status: b.shopStatus,
          isMine: b.userId === user.userId,
          isFriend: friends.has(b.userId),
          products: b.inventory.map((i) => ({ productCode: i.productCode, price: i.sellPrice, shelfQuantity: i.shelfQuantity })),
        };
      });
    const mine = await this.prisma.business.findFirst({
      where: { userId: user.userId, deletedAt: null },
      select: { id: true, shopStatus: true, stallSlot: true, marketAgeBand: true },
      orderBy: { createdAt: 'asc' },
    });
    return {
      zone: MARKET_ZONE_ID,
      slots: MARKET_STALL_SLOTS,
      tickSeconds: SHOP_TICK_SECONDS,
      stalls,
      myShop: mine
        ? { businessId: mine.id, status: mine.shopStatus, stallSlot: mine.marketAgeBand === user.ageBand ? mine.stallSlot : null }
        : null,
    };
  }
}

function toLocked(r: LockedRow): LockedShop {
  return {
    id: r.id,
    userId: r.user_id,
    level: r.level,
    shopStatus: r.shop_status,
    stallSlot: r.stall_slot,
    marketAgeBand: r.market_age_band,
    cashBox: r.cash_box,
    settledTick: r.settled_tick,
    lastSaleAt: r.last_sale_at,
  };
}
