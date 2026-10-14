import { Injectable } from '@nestjs/common';
import {
  ErrorCode,
  EXPEDITION_PLAN_CODES,
  EXPEDITION_PLANS,
  EXPEDITION_SITE_CODES,
  EXPEDITION_SITES,
  ExpeditionPlan,
  ExpeditionSite,
  expeditionEventChance,
  MATERIAL_CODES,
  materialItemId,
} from '@hwo/shared-contracts';
import { Expedition } from '@prisma/client';
import { randomInt } from 'node:crypto';
import { AppException } from '../common/app-exception';
import { AppConfig } from '../config/app-config.service';
import { InventoryService } from '../inventory/inventory.service';
import { PrismaService } from '../prisma/prisma.service';
import { QuestsService } from '../quests/quests.service';
import { bagTotal, ExpeditionOutcome, rollExpedition } from './expedition-sim';
import { materialsOf } from './materials';
import { SkillsService } from './skills.service';

type StoredResult = ExpeditionOutcome & { playerLevel: number | null; explorerLevelAfter: number; explorerLeveledUp: boolean };

/**
 * Expeditions (see shared-contracts/adventure.ts): one trip at a time, real minutes long, rolled on the
 * server when it is claimed. Claiming is idempotent: the trip id is the key, a retry returns the same story
 * and adds the materials once (item ledger keys `expedition:<id>:<material>`).
 */
@Injectable()
export class ExpeditionService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly inventory: InventoryService,
    private readonly skills: SkillsService,
    private readonly quests: QuestsService,
    private readonly config: AppConfig,
  ) {}

  async overview(userId: string) {
    const [skills, active, last, materials] = await Promise.all([
      this.skills.all(this.prisma, userId),
      this.prisma.expedition.findFirst({ where: { userId, claimedAt: null } }),
      this.prisma.expedition.findFirst({ where: { userId, claimedAt: { not: null } }, orderBy: { claimedAt: 'desc' } }),
      materialsOf(this.prisma, userId),
    ]);
    const explorer = skills.EXPLORER.level;
    return {
      sites: EXPEDITION_SITE_CODES.map((site) => {
        const d = EXPEDITION_SITES[site];
        return {
          site,
          explorerLevel: d.explorerLevel,
          unlocked: explorer >= d.explorerLevel,
          minutes: d.minutes,
          seconds: this.durationSeconds(site),
          materials: MATERIAL_CODES.filter((c) => d.gather[c] || Object.values(d.events).some((e) => e.reward[c])),
          eventCount: d.eventCount,
          explorerXp: d.explorerXp,
        };
      }),
      plans: EXPEDITION_PLAN_CODES.map((plan) => ({ plan, ...EXPEDITION_PLANS[plan] })),
      active: active ? this.present(active) : null,
      lastClaimed: last ? this.present(last) : null,
      skills,
      materials,
    };
  }

  async start(userId: string, site: ExpeditionSite, plan: ExpeditionPlan) {
    const exp = await this.prisma.tx(async (tx) => {
      // one player's trips start one at a time (the partial unique index is the second guard)
      await tx.$queryRaw`SELECT 1 FROM "player_profiles" WHERE "user_id" = ${userId}::uuid FOR UPDATE`;
      const active = await tx.expedition.findFirst({ where: { userId, claimedAt: null } });
      if (active) {
        throw AppException.conflict(ErrorCode.EXPEDITION_IN_PROGRESS, 'You are already on a trip', { expeditionId: active.id, endsAt: active.endsAt.toISOString() });
      }
      const explorerLevel = await this.skills.level(tx, userId, 'EXPLORER');
      const need = EXPEDITION_SITES[site].explorerLevel;
      if (explorerLevel < need) throw AppException.conflict(ErrorCode.EXPEDITION_LOCKED, `Needs Explorer level ${need}`, { explorerLevel: need });
      const now = Date.now();
      return tx.expedition.create({
        data: {
          userId,
          site,
          plan,
          seed: randomInt(1, 2_147_483_647),
          explorerLevel,
          startedAt: new Date(now),
          endsAt: new Date(now + this.durationSeconds(site) * 1000),
        },
      });
    });
    return this.present(exp);
  }

  async claim(userId: string, id: string) {
    const out = await this.prisma.tx(async (tx) => {
      const rows = await tx.$queryRaw<{ id: string }[]>`
        SELECT "id" FROM "expeditions" WHERE "id" = ${id}::uuid AND "user_id" = ${userId}::uuid FOR UPDATE`;
      if (!rows[0]) throw AppException.notFound('Trip not found');
      const exp = await tx.expedition.findUniqueOrThrow({ where: { id } });
      if (exp.claimedAt) return { exp, replayed: true };
      const secondsLeft = Math.ceil((exp.endsAt.getTime() - Date.now()) / 1000);
      if (secondsLeft > 0) throw AppException.conflict(ErrorCode.EXPEDITION_NOT_FINISHED, 'Still on the way', { secondsLeft });

      const outcome = rollExpedition(exp.seed, exp.site as ExpeditionSite, exp.plan as ExpeditionPlan, exp.explorerLevel);
      for (const code of MATERIAL_CODES) {
        const n = outcome.loot[code] ?? 0;
        if (n <= 0) continue;
        await this.inventory.applyChange(tx, {
          userId,
          itemId: materialItemId(code),
          delta: n,
          type: 'EXPEDITION_LOOT',
          idempotencyKey: `expedition:${id}:${code}`,
          referenceType: 'expedition',
          referenceId: id,
        });
      }
      const explorer = await this.skills.addXp(tx, userId, 'EXPLORER', outcome.explorerXp);
      const playerLevel = await this.skills.addPlayerXp(tx, userId, outcome.playerXp);
      await this.quests.recordEvent(tx, userId, 'EXPEDITION_COMPLETE', 1);
      await this.quests.recordEvent(tx, userId, 'GATHER_MATERIAL', bagTotal(outcome.loot));
      const result: StoredResult = { ...outcome, playerLevel, explorerLevelAfter: explorer.level, explorerLeveledUp: explorer.leveledUp };
      const done = await tx.expedition.update({ where: { id }, data: { claimedAt: new Date(), result: result as object } });
      return { exp: done, replayed: false };
    });
    return {
      ...this.present(out.exp),
      replayed: out.replayed,
      skills: await this.skills.all(this.prisma, userId),
      materials: await materialsOf(this.prisma, userId),
    };
  }

  private durationSeconds(site: ExpeditionSite): number {
    return Math.max(1, Math.round(EXPEDITION_SITES[site].minutes * 60 * this.config.get('EXPEDITION_TIME_SCALE')));
  }

  private present(e: Expedition) {
    const site = e.site as ExpeditionSite;
    const plan = e.plan as ExpeditionPlan;
    const secondsLeft = Math.max(0, Math.ceil((e.endsAt.getTime() - Date.now()) / 1000));
    return {
      id: e.id,
      site,
      plan,
      explorerLevel: e.explorerLevel,
      startedAt: e.startedAt.toISOString(),
      endsAt: e.endsAt.toISOString(),
      secondsLeft,
      ready: !e.claimedAt && secondsLeft === 0,
      claimed: !!e.claimedAt,
      /** the chance of each possible event on this trip, so the plan choice can be explained */
      eventChances: Object.fromEntries(
        Object.entries(EXPEDITION_SITES[site].events).map(([code, ev]) => [code, Math.round(expeditionEventChance(ev.chance, e.explorerLevel, plan) * 100) / 100]),
      ),
      result: (e.result as StoredResult | null) ?? null,
    };
  }
}
