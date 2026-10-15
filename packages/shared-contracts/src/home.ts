import { z } from 'zod';

/**
 * Phase 10: the first home and the residential district.
 * The server is authoritative for ownership, furniture and layouts; the client uses these rules only to
 * colour the placement preview (green/red) before it asks the server.
 */

// ---------- visitors ----------
/** PRIVATE = owner only, FRIENDS = owner + friends, PUBLIC = anyone in the same age band (never across a block). */
export const HOME_VISIT_POLICIES = ['PRIVATE', 'FRIENDS', 'PUBLIC'] as const;
export type HomeVisitPolicy = (typeof HOME_VISIT_POLICIES)[number];

/** Players (owner included) inside one home at the same time. */
export const HOME_MAX_OCCUPANTS = 12;

// ---------- the room ----------
/** World units per grid cell inside a home (the client draws the room at this scale; the server checks moves with it). */
export const HOME_CELL_SIZE = 1.5;
/** Most pieces a home can hold (keeps layouts small to send and check). */
export const HOME_MAX_FURNITURE = 40;
export const HOME_ROTATIONS = [0, 90, 180, 270] as const;
export type HomeRotation = (typeof HOME_ROTATIONS)[number];

/** The walkable room for a grid: the door is in the middle of the bottom wall. */
export function homeInterior(gridWidth: number, gridHeight: number) {
  const width = gridWidth * HOME_CELL_SIZE;
  const height = gridHeight * HOME_CELL_SIZE;
  return { width, height, spawn: { x: width / 2, y: 0.8 } };
}

/** Furniture gifted once when the starter home is claimed (on top of the chair every player starts with). */
export const STARTER_HOME_GIFT: readonly { itemId: string; quantity: number }[] = [
  { itemId: 'furn_single_bed', quantity: 1 },
  { itemId: 'furn_wood_table', quantity: 1 },
  { itemId: 'furn_plant_pot', quantity: 2 },
];

// ---------- the residential district (zone HOMES) ----------
/** Houses per street. Plot n is on street floor(n / 12), slot n % 12. Each age band has its own district. */
export const PLOTS_PER_STREET = 12;

/**
 * Where a plot stands in the HOMES zone (40x40). Slots 0-5 face the road from the north, 6-11 from the south.
 * `door` is the walk-in point on the road side.
 */
export function plotPosition(slot: number): { x: number; y: number; door: { x: number; y: number }; facing: 'N' | 'S' } {
  if (!Number.isInteger(slot) || slot < 0 || slot >= PLOTS_PER_STREET) throw new Error(`Bad plot slot ${slot}`);
  const xs = [4, 9.5, 15, 20.5, 26, 31];
  const north = slot < 6;
  const x = xs[slot % 6]!;
  return north ? { x, y: 24, door: { x, y: 21.8 }, facing: 'S' } : { x, y: 13, door: { x, y: 15.2 }, facing: 'N' };
}

export function plotAddress(plotNumber: number) {
  return { plotNumber, street: Math.floor(plotNumber / PLOTS_PER_STREET), slot: plotNumber % PLOTS_PER_STREET };
}

// ---------- placement rules ----------
export interface FurniturePiece {
  itemId: string;
  x: number;
  y: number;
  rotation: number;
}

/** Cells a piece covers: footprint [w, h] from the item's metadata, swapped when turned 90 or 270 degrees. */
export function pieceCells(p: FurniturePiece, footprint: readonly [number, number]): { x: number; y: number }[] {
  const [w, h] = p.rotation === 90 || p.rotation === 270 ? [footprint[1], footprint[0]] : [footprint[0], footprint[1]];
  const out: { x: number; y: number }[] = [];
  for (let dx = 0; dx < w; dx++) for (let dy = 0; dy < h; dy++) out.push({ x: p.x + dx, y: p.y + dy });
  return out;
}

/** Reads `metadata.footprint` safely; anything odd counts as 1x1. */
export function footprintOf(metadata: unknown): [number, number] {
  const fp = (metadata as { footprint?: unknown } | null)?.footprint;
  if (Array.isArray(fp) && fp.length === 2 && fp.every((n) => Number.isInteger(n) && n >= 1 && n <= 4)) return [fp[0] as number, fp[1] as number];
  return [1, 1];
}

export type LayoutProblem =
  | { problem: 'OUT_OF_BOUNDS'; index: number }
  | { problem: 'OVERLAP'; index: number; with: number }
  | { problem: 'BAD_ROTATION'; index: number }
  | { problem: 'TOO_MANY'; max: number };

/**
 * First problem in a whole layout, or null when every piece fits inside the grid without overlapping.
 * `footprints` maps itemId -> [w, h].
 */
export function findLayoutProblem(
  pieces: readonly FurniturePiece[],
  footprints: ReadonlyMap<string, readonly [number, number]>,
  gridWidth: number,
  gridHeight: number,
): LayoutProblem | null {
  if (pieces.length > HOME_MAX_FURNITURE) return { problem: 'TOO_MANY', max: HOME_MAX_FURNITURE };
  const taken = new Map<string, number>();
  for (let i = 0; i < pieces.length; i++) {
    const p = pieces[i]!;
    if (!(HOME_ROTATIONS as readonly number[]).includes(p.rotation)) return { problem: 'BAD_ROTATION', index: i };
    for (const c of pieceCells(p, footprints.get(p.itemId) ?? [1, 1])) {
      if (c.x < 0 || c.y < 0 || c.x >= gridWidth || c.y >= gridHeight) return { problem: 'OUT_OF_BOUNDS', index: i };
      const k = `${c.x},${c.y}`;
      const other = taken.get(k);
      if (other !== undefined) return { problem: 'OVERLAP', index: i, with: other };
      taken.set(k, i);
    }
  }
  return null;
}

// ---------- REST schemas ----------
const cell = z.number().int().min(0).max(63);
export const homePieceSchema = z
  .object({
    /** id of a piece already in the home (move/rotate it); leave out to place a new piece from the bag */
    id: z.uuid().optional(),
    itemId: z.string().min(1).max(64),
    x: cell,
    y: cell,
    rotation: z.union([z.literal(0), z.literal(90), z.literal(180), z.literal(270)]),
  })
  .strict();

/** The whole layout the player wants. Pieces left out go back to the bag. */
export const saveHomeLayoutSchema = z
  .object({
    /** layoutVersion the client started editing from; a newer saved version means someone else saved first */
    baseVersion: z.number().int().min(0),
    pieces: z.array(homePieceSchema).max(HOME_MAX_FURNITURE),
  })
  .strict()
  .refine((v) => new Set(v.pieces.filter((p) => p.id).map((p) => p.id)).size === v.pieces.filter((p) => p.id).length, 'A piece id appears twice');
export type SaveHomeLayoutRequest = z.infer<typeof saveHomeLayoutSchema>;

export const updateHomeSettingsSchema = z.object({ visitPolicy: z.enum(HOME_VISIT_POLICIES) }).strict();
export type UpdateHomeSettingsRequest = z.infer<typeof updateHomeSettingsSchema>;

export const homeDistrictQuerySchema = z.object({ street: z.coerce.number().int().min(0).max(9999).optional() }).strict();

// ---------- realtime ----------
export const wsHomeEnterSchema = z.object({ ownerId: z.uuid() }).strict();

/** Presence zone name used while inside a home (moves are checked against homeInterior). */
export const HOME_ZONE = 'HOME';
export const homeRoomName = (ownerId: string) => `home:${ownerId}`;
