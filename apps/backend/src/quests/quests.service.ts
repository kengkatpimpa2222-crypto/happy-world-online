import { Injectable } from '@nestjs/common';
import { QuestObjectiveType, QuestStatus } from '@prisma/client';
import { ErrorCode, levelForXp } from '@hwo/shared-contracts';
import { AppException } from '../common/app-exception';
import { AuditService } from '../audit/audit.service';
import { PrismaService, Tx } from '../prisma/prisma.service';
import { WalletService } from '../wallet/wallet.service';
import { DailyRewardsService } from './daily-rewards.service';

@Injectable()
export class QuestsService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly wallet: WalletService,
    private readonly audit: AuditService,
    private readonly daily: DailyRewardsService,
  ) {}

  async list(userId: string) {
    const [quests, progress, profile] = await Promise.all([
      this.prisma.quest.findMany({ where: { isActive: true }, orderBy: [{ category: 'asc' }, { sortOrder: 'asc' }] }),
      this.prisma.questProgress.findMany({ where: { userId } }),
      this.prisma.playerProfile.findUniqueOrThrow({ where: { userId }, select: { level: true } }),
    ]);
    const byQuest = new Map(progress.map((p) => [p.questId, p]));
    return quests.map((q) => {
      const p = byQuest.get(q.id);
      const prereqDone = !q.prerequisiteQuestId || byQuest.get(q.prerequisiteQuestId)?.status === QuestStatus.CLAIMED;
      return {
        id: q.id,
        category: q.category,
        chapter: q.chapter,
        titleKey: q.titleKey,
        descriptionKey: q.descriptionKey,
        objective: { type: q.objectiveType, target: q.objectiveTarget },
        rewards: { gold: q.rewardGold, gems: q.rewardGems, xp: q.rewardXp, itemId: q.rewardItemId },
        minLevel: q.minLevel,
        prerequisiteQuestId: q.prerequisiteQuestId,
        canStart: !p && prereqDone && profile.level >= q.minLevel,
        progress: p ? { status: p.status, value: p.progress, startedAt: p.startedAt, claimedAt: p.claimedAt } : null,
      };
    });
  }

  async start(userId: string, questId: string) {
    return this.prisma.tx(async (tx) => {
      const quest = await tx.quest.findFirst({ where: { id: questId, isActive: true } });
      if (!quest) throw AppException.notFound('Quest not found');
      const profile = await tx.playerProfile.findUniqueOrThrow({ where: { userId }, select: { level: true } });
      if (profile.level < quest.minLevel) {
        throw AppException.conflict(ErrorCode.QUEST_NOT_AVAILABLE, `Requires level ${quest.minLevel}`);
      }
      if (quest.prerequisiteQuestId) {
        const pre = await tx.questProgress.findUnique({ where: { userId_questId: { userId, questId: quest.prerequisiteQuestId } } });
        if (pre?.status !== QuestStatus.CLAIMED) {
          throw AppException.conflict(ErrorCode.QUEST_NOT_AVAILABLE, 'Finish the previous quest first');
        }
      }
      const existing = await tx.questProgress.findUnique({ where: { userId_questId: { userId, questId } } });
      if (existing) throw AppException.conflict(ErrorCode.QUEST_ALREADY_STARTED, 'Quest already started');
      return tx.questProgress.create({ data: { userId, questId } });
    });
  }

  /**
   * Claims a completed quest's reward exactly once.
   * The status flip COMPLETED -> CLAIMED is a conditional update (row lock), so two parallel
   * claims cannot both succeed; reward ledger rows use deterministic idempotency keys as a second guard.
   */
  async claim(userId: string, questId: string) {
    return this.prisma.tx(async (tx) => {
      const quest = await tx.quest.findUnique({ where: { id: questId } });
      if (!quest) throw AppException.notFound('Quest not found');

      const flipped = await tx.questProgress.updateMany({
        where: { userId, questId, status: QuestStatus.COMPLETED },
        data: { status: QuestStatus.CLAIMED, claimedAt: new Date() },
      });
      if (flipped.count !== 1) {
        const p = await tx.questProgress.findUnique({ where: { userId_questId: { userId, questId } } });
        if (p?.status === QuestStatus.CLAIMED) throw AppException.conflict(ErrorCode.QUEST_ALREADY_CLAIMED, 'Reward already claimed');
        throw AppException.conflict(ErrorCode.QUEST_NOT_COMPLETED, 'Quest is not completed yet');
      }

      const ref = { referenceType: 'quest', referenceId: questId };
      if (quest.rewardGold > 0) {
        await this.wallet.applyChange(tx, { userId, currency: 'GOLD', amount: quest.rewardGold, type: 'QUEST_REWARD', idempotencyKey: `quest:${questId}:GOLD`, ...ref });
      }
      if (quest.rewardGems > 0) {
        await this.wallet.applyChange(tx, { userId, currency: 'GEMS', amount: quest.rewardGems, type: 'QUEST_REWARD', idempotencyKey: `quest:${questId}:GEMS`, ...ref });
      }
      let level: number | undefined;
      if (quest.rewardXp > 0) {
        const profile = await tx.playerProfile.update({ where: { userId }, data: { xp: { increment: quest.rewardXp } } });
        level = levelForXp(profile.xp);
        if (level !== profile.level) await tx.playerProfile.update({ where: { userId }, data: { level } });
      }
      if (quest.rewardItemId) {
        await tx.playerInventory.upsert({
          where: { userId_itemId: { userId, itemId: quest.rewardItemId } },
          create: { userId, itemId: quest.rewardItemId, quantity: 1 },
          update: { quantity: { increment: 1 } },
        });
      }
      await this.audit.log({ actorUserId: userId, action: 'quest.claimed', targetType: 'quest', targetId: questId }, tx);
      const balances = await tx.playerWallet.findMany({ where: { userId } });
      return {
        questId,
        rewards: { gold: quest.rewardGold, gems: quest.rewardGems, xp: quest.rewardXp, itemId: quest.rewardItemId },
        level,
        wallet: Object.fromEntries(balances.map((w) => [w.currency, w.balance])),
      };
    });
  }

  /**
   * Advances every IN_PROGRESS quest of this objective type, server-side only, and the player's
   * daily/weekly missions that count the same thing (Phase 11).
   * Called from inside the transaction of the action that earned the progress.
   */
  async recordEvent(tx: Tx, userId: string, type: QuestObjectiveType, amount = 1): Promise<void> {
    if (amount <= 0) return;
    await this.daily.record(tx, userId, type, amount);
    await tx.$executeRaw`
      UPDATE "quest_progress" qp
         SET "progress" = LEAST(q."objective_target", qp."progress" + ${amount}),
             "status" = CASE WHEN qp."progress" + ${amount} >= q."objective_target"
                             THEN 'COMPLETED'::"QuestStatus" ELSE 'IN_PROGRESS'::"QuestStatus" END,
             "completed_at" = CASE WHEN qp."progress" + ${amount} >= q."objective_target" THEN now() ELSE NULL END,
             "updated_at" = now()
        FROM "quests" q
       WHERE qp."quest_id" = q."id"
         AND qp."user_id" = ${userId}::uuid
         AND qp."status" = 'IN_PROGRESS'::"QuestStatus"
         AND q."is_active" = true
         AND q."objective_type" = ${type}::"QuestObjectiveType"`;
  }
}
