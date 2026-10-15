import { Injectable } from '@nestjs/common';
import { levelForXp, SKILL_CODES, SKILL_MAX_LEVEL, SkillCode, skillLevelForXp, skillXpForLevel } from '@hwo/shared-contracts';
import { PrismaService, Tx } from '../prisma/prisma.service';

export interface SkillView {
  skill: SkillCode;
  level: number;
  xp: number;
  /** XP where the current level started and where the next one starts (null at max level) */
  levelXp: number;
  nextLevelXp: number | null;
}

/** Life skills (Explorer, Crafter) and the player's own XP, shared by expeditions and crafting. */
@Injectable()
export class SkillsService {
  async all(db: Tx | PrismaService, userId: string): Promise<Record<SkillCode, SkillView>> {
    const rows = await db.playerSkill.findMany({ where: { userId } });
    return Object.fromEntries(SKILL_CODES.map((s) => [s, view(s, rows.find((r) => r.skill === s)?.xp ?? 0)])) as Record<SkillCode, SkillView>;
  }

  async level(db: Tx | PrismaService, userId: string, skill: SkillCode): Promise<number> {
    const row = await db.playerSkill.findUnique({ where: { userId_skill: { userId, skill } } });
    return skillLevelForXp(row?.xp ?? 0);
  }

  /** Adds skill XP; returns the new view and whether the level went up. */
  async addXp(tx: Tx, userId: string, skill: SkillCode, xp: number): Promise<SkillView & { leveledUp: boolean }> {
    const row = await tx.playerSkill.upsert({
      where: { userId_skill: { userId, skill } },
      create: { userId, skill, xp },
      update: { xp: { increment: xp } },
    });
    return { ...view(skill, row.xp), leveledUp: skillLevelForXp(row.xp) > skillLevelForXp(row.xp - xp) };
  }

  /** Adds the player's own XP (profile level); returns the new level when it changed, else null. */
  async addPlayerXp(tx: Tx, userId: string, xp: number): Promise<number | null> {
    if (xp <= 0) return null;
    const profile = await tx.playerProfile.update({ where: { userId }, data: { xp: { increment: xp } } });
    const level = levelForXp(profile.xp);
    if (level === profile.level) return null;
    await tx.playerProfile.update({ where: { userId }, data: { level } });
    return level;
  }
}

function view(skill: SkillCode, xp: number): SkillView {
  const level = skillLevelForXp(xp);
  return { skill, level, xp, levelXp: skillXpForLevel(level), nextLevelXp: level >= SKILL_MAX_LEVEL ? null : skillXpForLevel(level + 1) };
}
