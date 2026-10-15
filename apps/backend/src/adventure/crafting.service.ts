import { Injectable } from '@nestjs/common';
import {
  BUSINESS_PRODUCTS,
  businessLevelConfig,
  craftOutputUnits,
  ErrorCode,
  MATERIAL_CODES,
  materialItemId,
  RECIPE_CODES,
  RECIPES,
  RecipeCode,
} from '@hwo/shared-contracts';
import { AppException } from '../common/app-exception';
import { weightedAvgCost } from '../business/business-sim';
import { BusinessService } from '../business/business.service';
import { LivingShopService, SettleResult } from '../business/living-shop.service';
import { InventoryService } from '../inventory/inventory.service';
import { PrismaService } from '../prisma/prisma.service';
import { QuestsService } from '../quests/quests.service';
import { WalletService } from '../wallet/wallet.service';
import { materialsOf } from './materials';
import { SkillsService } from './skills.service';

/** Player XP per craft (Crafter XP comes from the recipe). */
export const CRAFT_PLAYER_XP = 2;

/**
 * Crafting: materials + a small Gold workshop fee -> craft-only goods straight into a shop's storage.
 * The fee is the goods' cost per unit (materials were paid with time, like mini-game goods), recorded as a
 * shop expense. Idempotent by the Idempotency-Key header: the fee's ledger row is the "already done" marker.
 */
@Injectable()
export class CraftingService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly wallet: WalletService,
    private readonly inventory: InventoryService,
    private readonly skills: SkillsService,
    private readonly shop: LivingShopService,
    private readonly business: BusinessService,
    private readonly quests: QuestsService,
  ) {}

  async overview(userId: string) {
    const [materials, skills] = await Promise.all([materialsOf(this.prisma, userId), this.skills.all(this.prisma, userId)]);
    const crafter = skills.CRAFTER.level;
    return {
      recipes: RECIPE_CODES.map((recipe) => {
        const r = RECIPES[recipe];
        const p = BUSINESS_PRODUCTS[r.productCode];
        const canMake = Math.min(...MATERIAL_CODES.filter((c) => r.inputs[c]).map((c) => Math.floor(materials[c] / r.inputs[c]!)));
        return {
          recipe,
          productCode: r.productCode,
          inputs: r.inputs,
          outputUnits: craftOutputUnits(recipe, crafter),
          feeGold: r.feeGold,
          crafterLevel: r.crafterLevel,
          unlocked: crafter >= r.crafterLevel,
          canMake,
          basePrice: p.basePrice,
          minPrice: p.minPrice,
          maxPrice: p.maxPrice,
        };
      }),
      materials,
      crafter: skills.CRAFTER,
    };
  }

  async craft(userId: string, businessId: string, recipe: RecipeCode, times: number, idempotencyKey: string) {
    const r = RECIPES[recipe];
    const code = r.productCode;
    const fee = r.feeGold * times;
    const settled: SettleResult[] = [];
    const res = await this.prisma.tx(async (tx) => {
      settled.length = 0;
      const shop = await this.shop.lock(tx, userId, businessId); // 404 for someone else's shop
      const s = await this.shop.settleLocked(tx, shop);
      if (s) settled.push(s);

      const crafterLevel = await this.skills.level(tx, userId, 'CRAFTER');
      if (crafterLevel < r.crafterLevel) throw AppException.conflict(ErrorCode.RECIPE_LOCKED, `Needs Crafter level ${r.crafterLevel}`, { crafterLevel: r.crafterLevel });
      const units = craftOutputUnits(recipe, crafterLevel) * times;

      const pay = await this.wallet.applyChange(tx, {
        userId,
        currency: 'GOLD',
        amount: -fee,
        type: 'CRAFT_FEE',
        idempotencyKey: `craft:${idempotencyKey}`,
        referenceType: 'business',
        referenceId: `${businessId}:${recipe}:${times}`,
      });
      if (pay.replayed) return { replayed: true, units: 0, crafterLeveledUp: false, playerLevel: null as number | null };

      const line = await tx.businessInventory.findUnique({ where: { businessId_productCode: { businessId, productCode: code } } });
      const current = line?.quantity ?? 0;
      const { stockCapacity } = businessLevelConfig(shop.level);
      if (current + units > stockCapacity) {
        throw AppException.conflict(ErrorCode.BUSINESS_STOCK_FULL, `Stock limit is ${stockCapacity}`, { stockCapacity, current });
      }
      for (const m of MATERIAL_CODES) {
        const need = (r.inputs[m] ?? 0) * times;
        if (need <= 0) continue;
        await this.inventory.applyChange(tx, {
          userId,
          itemId: materialItemId(m),
          delta: -need,
          type: 'CRAFT_CONSUME',
          idempotencyKey: `craft:${idempotencyKey}:${m}`,
          referenceType: 'business',
          referenceId: `${businessId}:${recipe}:${times}`,
        });
      }
      const unitCost = Math.round(fee / units);
      await tx.businessInventory.upsert({
        where: { businessId_productCode: { businessId, productCode: code } },
        create: { businessId, productCode: code, quantity: units, avgUnitCost: unitCost, sellPrice: BUSINESS_PRODUCTS[code].basePrice },
        update: { quantity: { increment: units }, avgUnitCost: weightedAvgCost(current + (line?.shelfQuantity ?? 0), line?.avgUnitCost ?? 0, units, unitCost) },
      });
      await tx.business.update({ where: { id: businessId }, data: { totalExpense: { increment: fee } } });
      const crafter = await this.skills.addXp(tx, userId, 'CRAFTER', r.crafterXp * times);
      const playerLevel = await this.skills.addPlayerXp(tx, userId, CRAFT_PLAYER_XP * times);
      await this.quests.recordEvent(tx, userId, 'CRAFT_ITEM', times);
      return { replayed: false, units, crafterLeveledUp: crafter.leveledUp, playerLevel };
    });
    settled.forEach((x) => this.shop.publish(x));
    return {
      replayed: res.replayed,
      recipe,
      productCode: code,
      times,
      unitsMade: res.units,
      feeGold: fee,
      crafterLeveledUp: res.crafterLeveledUp,
      level: res.playerLevel,
      crafter: (await this.skills.all(this.prisma, userId)).CRAFTER,
      materials: await materialsOf(this.prisma, userId),
      business: await this.business.get(userId, businessId),
    };
  }
}
