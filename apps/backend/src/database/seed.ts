/**
 * Idempotent reference-data seed (items + quests). Safe to run on every deploy:
 * it upserts catalog rows and never touches player data.
 */
import { PrismaClient } from '@prisma/client';
import { ITEMS, QUESTS } from './seed-data';

export async function seed(prisma: PrismaClient): Promise<void> {
  for (const [i, item] of ITEMS.entries()) {
    const data = {
      category: item.category,
      nameKey: `item.${item.id}.name`,
      priceGold: item.priceGold,
      priceGems: item.priceGems,
      isPremium: item.isPremium ?? false,
      sortOrder: i,
      metadata: (item.metadata ?? {}) as object,
    };
    // Prices are only set on create so admin price changes survive re-seeding.
    await prisma.item.upsert({
      where: { id: item.id },
      create: { id: item.id, ...data },
      update: { category: data.category, nameKey: data.nameKey, metadata: data.metadata },
    });
  }
  for (const [i, q] of QUESTS.entries()) {
    const data = {
      category: q.category,
      chapter: q.chapter ?? null,
      titleKey: `quest.${q.id}.title`,
      descriptionKey: `quest.${q.id}.description`,
      objectiveType: q.objectiveType,
      objectiveTarget: q.objectiveTarget,
      rewardGold: q.rewardGold ?? 0,
      rewardGems: q.rewardGems ?? 0,
      rewardXp: q.rewardXp ?? 0,
      rewardItemId: q.rewardItemId ?? null,
      minLevel: q.minLevel ?? 1,
      prerequisiteQuestId: q.prerequisiteQuestId ?? null,
      sortOrder: i,
    };
    await prisma.quest.upsert({ where: { id: q.id }, create: { id: q.id, ...data }, update: data });
  }
}

if (require.main === module) {
  const prisma = new PrismaClient();
  seed(prisma)
    .then(() => process.stdout.write(`Seeded ${ITEMS.length} items and ${QUESTS.length} quests\n`))
    .catch((e) => {
      process.stderr.write(String(e) + '\n');
      process.exitCode = 1;
    })
    .finally(() => prisma.$disconnect());
}
