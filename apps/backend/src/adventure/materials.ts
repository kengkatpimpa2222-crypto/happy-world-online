import { MATERIAL_CODES, materialFromItemId, materialItemId, MaterialBag } from '@hwo/shared-contracts';
import { PrismaService, Tx } from '../prisma/prisma.service';

/** The player's materials as a full bag (every material, 0 when none). */
export async function materialsOf(db: Tx | PrismaService, userId: string): Promise<Required<MaterialBag>> {
  const rows = await db.playerInventory.findMany({ where: { userId, itemId: { in: MATERIAL_CODES.map(materialItemId) } } });
  const bag = Object.fromEntries(MATERIAL_CODES.map((c) => [c, 0])) as Required<MaterialBag>;
  for (const r of rows) {
    const code = materialFromItemId(r.itemId);
    if (code) bag[code] = r.quantity;
  }
  return bag;
}
