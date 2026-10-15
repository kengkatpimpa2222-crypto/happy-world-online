import { Injectable } from '@nestjs/common';
import { AgeBand, HomeFurniture, HomeVisitPolicy, PlayerHome, Prisma } from '@prisma/client';
import {
  ErrorCode,
  findLayoutProblem,
  footprintOf,
  homeInterior,
  plotAddress,
  plotPosition,
  PLOTS_PER_STREET,
  SaveHomeLayoutRequest,
  STARTER_HOME_GIFT,
  WsEvent,
} from '@hwo/shared-contracts';
import { randomUUID } from 'node:crypto';
import { AppException } from '../common/app-exception';
import { AuthUser } from '../common/decorators';
import { AuditService } from '../audit/audit.service';
import { InventoryService } from '../inventory/inventory.service';
import { PrismaService, Tx } from '../prisma/prisma.service';
import { QuestsService } from '../quests/quests.service';
import { RealtimeService } from '../realtime/realtime.service';
import { SocialGraphService } from '../social/social-graph.service';
import { HomeAccessService } from './home-access.service';

type HomeRow = Pick<PlayerHome, 'id' | 'userId' | 'gridWidth' | 'gridHeight' | 'layoutVersion' | 'claimedAt' | 'plotNumber' | 'layoutSaveKey'>;

/**
 * Phase 10: the first home. Reuses `player_homes` / `home_furniture` (first migration) and the Phase 9 item ledger.
 *
 * Ownership rule: a player owns (pieces in `player_inventory`) + (rows in `home_furniture` of their home).
 * Placing moves one piece out of the bag (ledger HOME_PLACE, a conditional decrement), taking it out moves it
 * back (HOME_REMOVE), both in the transaction that changes the layout, so furniture can't be duplicated or lost.
 * The home row is locked FOR UPDATE and `layout_version` must match the version the client edited from.
 */
@Injectable()
export class HomeService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly inventory: InventoryService,
    private readonly access: HomeAccessService,
    private readonly graph: SocialGraphService,
    private readonly quests: QuestsService,
    private readonly audit: AuditService,
    private readonly realtime: RealtimeService,
  ) {}

  // ---------- reading ----------

  async mine(userId: string) {
    const home = await this.prisma.playerHome.findUnique({ where: { userId } });
    if (!home) throw AppException.notFound('Home not found'); // every account gets a row at sign-up
    return this.view(this.prisma, home, true, false);
  }

  /** Someone's home as a visitor sees it (the owner gets the same view as GET /homes/me). */
  async visit(viewer: AuthUser, ownerId: string) {
    const a = await this.access.check(viewer.userId, viewer.ageBand, ownerId);
    const home = await this.prisma.playerHome.findUniqueOrThrow({ where: { id: a.homeId } });
    return this.view(this.prisma, home, a.isOwner, a.isFriend);
  }

  /** Furniture I own: in the bag (can be placed) and already standing in my home. */
  async furniture(userId: string) {
    const home = await this.prisma.playerHome.findUnique({ where: { userId }, select: { id: true } });
    const [bag, placed] = await Promise.all([
      this.prisma.playerInventory.findMany({ where: { userId, quantity: { gt: 0 }, item: { category: 'FURNITURE' } }, include: { item: true } }),
      home ? this.prisma.homeFurniture.groupBy({ by: ['itemId'], where: { homeId: home.id }, _count: { _all: true } }) : Promise.resolve([]),
    ]);
    const placedCount = new Map(placed.map((p) => [p.itemId, p._count._all]));
    const ids = [...new Set([...bag.map((b) => b.itemId), ...placedCount.keys()])];
    const items = await this.prisma.item.findMany({ where: { id: { in: ids } } });
    const inBag = new Map(bag.map((b) => [b.itemId, b.quantity]));
    return items
      .sort((a, b) => a.sortOrder - b.sortOrder || a.id.localeCompare(b.id))
      .map((i) => ({
        itemId: i.id,
        nameKey: i.nameKey,
        footprint: footprintOf(i.metadata),
        inBag: inBag.get(i.id) ?? 0,
        placed: placedCount.get(i.id) ?? 0,
      }));
  }

  /**
   * One street of the district of my age band (default: the street my home is on). Blocked players' homes show
   * as taken but nameless; `canVisit` already applies the visit policy and friendship.
   */
  async district(viewer: AuthUser, street?: number) {
    const mine = await this.prisma.playerHome.findUnique({ where: { userId: viewer.userId }, select: { plotNumber: true } });
    const s = street ?? (mine?.plotNumber != null ? plotAddress(mine.plotNumber).street : 0);
    const from = s * PLOTS_PER_STREET;
    const [homes, blocked, friendIds, maxPlot] = await Promise.all([
      this.prisma.playerHome.findMany({
        where: { plotAgeBand: viewer.ageBand, plotNumber: { gte: from, lt: from + PLOTS_PER_STREET }, user: { status: 'ACTIVE', deletedAt: null } },
        select: { userId: true, plotNumber: true, visitPolicy: true, user: { select: { profile: { select: { displayName: true, level: true } } } } },
      }),
      this.graph.blockedEitherWay(viewer.userId),
      this.graph.friendIds(viewer.userId),
      this.prisma.playerHome.aggregate({ where: { plotAgeBand: viewer.ageBand }, _max: { plotNumber: true } }),
    ]);
    const friends = new Set(friendIds);
    const byPlot = new Map(homes.map((h) => [h.plotNumber!, h]));
    const plots = Array.from({ length: PLOTS_PER_STREET }, (_, slot) => {
      const plotNumber = from + slot;
      const h = byPlot.get(plotNumber);
      const pos = plotPosition(slot);
      const base = { plotNumber, slot, x: pos.x, y: pos.y, door: pos.door, facing: pos.facing };
      if (!h) return { ...base, taken: false, owner: null, isMine: false, isFriend: false, visitPolicy: null, canVisit: false };
      const isMine = h.userId === viewer.userId;
      if (!isMine && blocked.has(h.userId)) return { ...base, taken: true, owner: null, isMine: false, isFriend: false, visitPolicy: null, canVisit: false };
      const isFriend = friends.has(h.userId);
      const canVisit = isMine || h.visitPolicy === 'PUBLIC' || (h.visitPolicy === 'FRIENDS' && isFriend);
      return {
        ...base,
        taken: true,
        owner: { userId: h.userId, displayName: h.user.profile?.displayName ?? '', level: h.user.profile?.level ?? 1 },
        isMine,
        isFriend,
        visitPolicy: h.visitPolicy,
        canVisit,
      };
    });
    const lastStreet = maxPlot._max.plotNumber != null ? plotAddress(maxPlot._max.plotNumber).street : 0;
    return { street: s, streets: lastStreet + 1, myPlot: mine?.plotNumber ?? null, plots };
  }

  // ---------- claiming ----------

  /**
   * Gives the player the first free plot of their age band's district and the starter furniture, once.
   * The home row lock makes a double tap claim once; the per-age-band advisory lock plus the partial unique
   * index make two players never get the same plot.
   */
  async claim(userId: string) {
    const result = await this.prisma.tx(async (tx) => {
      const home = await this.lockHome(tx, userId);
      if (home.claimedAt) throw AppException.conflict(ErrorCode.HOME_ALREADY_CLAIMED, 'You already have a home', { plotNumber: home.plotNumber });
      const { ageBand } = await tx.user.findUniqueOrThrow({ where: { id: userId }, select: { ageBand: true } });
      await tx.$executeRaw`SELECT pg_advisory_xact_lock(hashtextextended(${`home-plots:${ageBand}`}, 0))`;
      const plotNumber = await this.firstFreePlot(tx, ageBand);
      await tx.playerHome.update({ where: { id: home.id }, data: { plotAgeBand: ageBand, plotNumber, claimedAt: new Date() } });
      for (const g of STARTER_HOME_GIFT) {
        await this.inventory.applyChange(tx, {
          userId,
          itemId: g.itemId,
          delta: g.quantity,
          type: 'HOME_STARTER_GIFT',
          idempotencyKey: `home-claim:${g.itemId}`, // per player: a gift can never be given twice
          referenceType: 'home',
          referenceId: home.id,
        });
      }
      await this.audit.log({ actorUserId: userId, action: 'home.claim', targetType: 'home', targetId: home.id, metadata: { ageBand, plotNumber, gift: [...STARTER_HOME_GIFT] } }, tx);
      return { homeId: home.id, plotNumber };
    });
    return { claimed: true, plotNumber: result.plotNumber, gift: STARTER_HOME_GIFT, home: await this.mine(userId) };
  }

  private async firstFreePlot(tx: Tx, ageBand: AgeBand): Promise<number> {
    const rows = await tx.$queryRaw<{ n: number }[]>`
      SELECT n::int AS n FROM generate_series(
               0, (SELECT COALESCE(MAX("plot_number") + 1, 0) FROM "player_homes" WHERE "plot_age_band" = ${ageBand}::"AgeBand")) AS n
       WHERE NOT EXISTS (SELECT 1 FROM "player_homes" WHERE "plot_age_band" = ${ageBand}::"AgeBand" AND "plot_number" = n)
       ORDER BY n LIMIT 1`;
    return rows[0]!.n;
  }

  // ---------- editing ----------

  /**
   * Saves the whole layout. Pieces with an `id` are moved/turned, pieces without one are placed from the bag,
   * and pieces of the home that are not in the list go back to the bag.
   */
  async saveLayout(userId: string, dto: SaveHomeLayoutRequest, idempotencyKey: string) {
    const out = await this.prisma.tx(async (tx) => {
      const home = await this.lockHome(tx, userId);
      if (!home.claimedAt) throw AppException.conflict(ErrorCode.HOME_NOT_CLAIMED, 'Claim your home first');
      if (home.layoutSaveKey === idempotencyKey) return { replayed: true, placed: 0, moved: 0, removed: 0, version: home.layoutVersion };
      if (dto.baseVersion !== home.layoutVersion) {
        throw AppException.conflict(ErrorCode.HOME_LAYOUT_CONFLICT, 'The home was changed after you started editing', { layoutVersion: home.layoutVersion });
      }

      const existing = await tx.homeFurniture.findMany({ where: { homeId: home.id } });
      const byId = new Map(existing.map((f) => [f.id, f]));
      for (const [i, p] of dto.pieces.entries()) {
        if (!p.id) continue;
        const f = byId.get(p.id);
        if (!f || f.itemId !== p.itemId) throw AppException.badRequest(ErrorCode.HOME_PLACEMENT_INVALID, 'Unknown piece', { problem: 'UNKNOWN_PIECE', index: i });
      }

      const itemIds = [...new Set([...dto.pieces.map((p) => p.itemId), ...existing.map((f) => f.itemId)])];
      const items = await tx.item.findMany({ where: { id: { in: itemIds } } });
      const itemById = new Map(items.map((i) => [i.id, i]));
      for (const [i, p] of dto.pieces.entries()) {
        const item = itemById.get(p.itemId);
        // new pieces must be live furniture; pieces already standing stay even if the item was retired from the store
        if (!item || item.category !== 'FURNITURE' || (!p.id && (!item.isActive || item.deletedAt))) {
          throw AppException.badRequest(ErrorCode.HOME_PLACEMENT_INVALID, 'Not a piece of furniture', { problem: 'NOT_FURNITURE', index: i });
        }
      }
      const footprints = new Map(items.map((i) => [i.id, footprintOf(i.metadata)]));
      const problem = findLayoutProblem(dto.pieces, footprints, home.gridWidth, home.gridHeight);
      if (problem) throw AppException.badRequest(ErrorCode.HOME_PLACEMENT_INVALID, `Furniture does not fit: ${problem.problem}`, problem);

      const keep = new Set(dto.pieces.filter((p) => p.id).map((p) => p.id!));
      const removed = existing.filter((f) => !keep.has(f.id));
      const moved = dto.pieces.filter((p) => {
        const f = p.id ? byId.get(p.id) : undefined;
        return f !== undefined && (f.x !== p.x || f.y !== p.y || f.rotation !== p.rotation);
      });
      const placed = dto.pieces.filter((p) => !p.id);

      // bag first: removed pieces go back before new ones are taken, so swapping a piece for the same item works
      await this.moveItems(tx, userId, home.id, idempotencyKey, 'remove', countBy(removed.map((f) => f.itemId)));
      await this.moveItems(tx, userId, home.id, idempotencyKey, 'place', countBy(placed.map((p) => p.itemId)));

      const rewrite = [...removed.map((f) => f.id), ...moved.map((p) => p.id!)];
      if (rewrite.length) await tx.homeFurniture.deleteMany({ where: { homeId: home.id, id: { in: rewrite } } });
      const rows: Prisma.HomeFurnitureCreateManyInput[] = [
        ...moved.map((p) => ({ id: p.id!, homeId: home.id, itemId: p.itemId, x: p.x, y: p.y, rotation: p.rotation })),
        ...placed.map((p) => ({ id: randomUUID(), homeId: home.id, itemId: p.itemId, x: p.x, y: p.y, rotation: p.rotation })),
      ];
      if (rows.length) await tx.homeFurniture.createMany({ data: rows });
      const version = home.layoutVersion + 1;
      await tx.playerHome.update({ where: { id: home.id }, data: { layoutVersion: version, layoutSaveKey: idempotencyKey } });

      if (placed.length) await this.quests.recordEvent(tx, userId, 'PLACE_FURNITURE', placed.length);
      if (placed.length || removed.length) {
        await this.audit.log(
          {
            actorUserId: userId,
            action: 'home.layout_save',
            targetType: 'home',
            targetId: home.id,
            metadata: { version, placed: Object.fromEntries(countBy(placed.map((p) => p.itemId))), removed: Object.fromEntries(countBy(removed.map((f) => f.itemId))), moved: moved.length },
          },
          tx,
        );
      }
      return { replayed: false, placed: placed.length, moved: moved.length, removed: removed.length, version };
    });
    if (!out.replayed) this.realtime.toHome(userId, WsEvent.HOME_LAYOUT_CHANGED, { ownerId: userId, layoutVersion: out.version });
    return { ...out, home: await this.mine(userId) };
  }

  private async moveItems(tx: Tx, userId: string, homeId: string, key: string, dir: 'place' | 'remove', counts: Map<string, number>) {
    for (const [itemId, n] of counts) {
      try {
        const r = await this.inventory.applyChange(tx, {
          userId,
          itemId,
          delta: dir === 'place' ? -n : n,
          type: dir === 'place' ? 'HOME_PLACE' : 'HOME_REMOVE',
          idempotencyKey: `home:${key}:${dir}:${itemId}`,
          referenceType: 'home',
          referenceId: homeId,
        });
        // the layout key changes every save, so an old ledger row means the key was reused for another save
        if (r.replayed) throw AppException.conflict(ErrorCode.IDEMPOTENCY_KEY_REUSED, 'Idempotency-Key was already used for a different request');
      } catch (e) {
        if (e instanceof AppException && e.code === ErrorCode.NOT_ENOUGH_MATERIALS) {
          throw AppException.conflict(ErrorCode.ITEM_NOT_OWNED, `You have no more ${itemId} in your bag`, (e.getResponse() as { details?: unknown }).details);
        }
        throw e;
      }
    }
  }

  async updateSettings(userId: string, visitPolicy: HomeVisitPolicy) {
    const before = await this.prisma.tx(async (tx) => {
      const home = await this.lockHome(tx, userId);
      const old = await tx.playerHome.findUniqueOrThrow({ where: { id: home.id }, select: { visitPolicy: true } });
      if (old.visitPolicy !== visitPolicy) {
        await tx.playerHome.update({ where: { id: home.id }, data: { visitPolicy } });
        await this.audit.log({ actorUserId: userId, action: 'home.visit_policy', targetType: 'home', targetId: home.id, metadata: { from: old.visitPolicy, to: visitPolicy } }, tx);
      }
      return old.visitPolicy;
    });
    // visitors who may no longer be here are walked out at once
    if (before !== visitPolicy) await this.realtime.homeAccessChanged([userId]);
    return this.mine(userId);
  }

  // ---------- helpers ----------

  /** Locks my home row for the rest of the transaction (creates it for an account that somehow has none). */
  private async lockHome(tx: Tx, userId: string): Promise<HomeRow> {
    const select = () => tx.$queryRaw<
      { id: string; user_id: string; grid_width: number; grid_height: number; layout_version: number; claimed_at: Date | null; plot_number: number | null; layout_save_key: string | null }[]
    >`SELECT "id", "user_id", "grid_width", "grid_height", "layout_version", "claimed_at", "plot_number", "layout_save_key"
        FROM "player_homes" WHERE "user_id" = ${userId}::uuid FOR UPDATE`;
    let rows = await select();
    if (!rows[0]) {
      await tx.playerHome.createMany({ data: [{ userId }], skipDuplicates: true });
      rows = await select();
    }
    const r = rows[0]!;
    return {
      id: r.id,
      userId: r.user_id,
      gridWidth: r.grid_width,
      gridHeight: r.grid_height,
      layoutVersion: r.layout_version,
      claimedAt: r.claimed_at,
      plotNumber: r.plot_number,
      layoutSaveKey: r.layout_save_key,
    };
  }

  private async view(db: Tx | PrismaService, home: PlayerHome, isOwner: boolean, isFriend: boolean) {
    const [furniture, owner] = await Promise.all([
      db.homeFurniture.findMany({ where: { homeId: home.id }, include: { item: { select: { metadata: true } } }, orderBy: [{ y: 'asc' }, { x: 'asc' }] }),
      db.playerProfile.findUnique({ where: { userId: home.userId }, select: { displayName: true, level: true } }),
    ]);
    const plot = home.plotNumber != null ? { ...plotAddress(home.plotNumber), ...plotPosition(plotAddress(home.plotNumber).slot) } : null;
    return {
      homeId: home.id,
      owner: { userId: home.userId, displayName: owner?.displayName ?? '', level: owner?.level ?? 1 },
      isMine: isOwner,
      isFriend,
      claimed: home.claimedAt !== null,
      claimedAt: home.claimedAt,
      plot,
      // visitors don't need to know the owner's privacy setting beyond "you are allowed in"
      visitPolicy: isOwner ? home.visitPolicy : undefined,
      gridWidth: home.gridWidth,
      gridHeight: home.gridHeight,
      interior: homeInterior(home.gridWidth, home.gridHeight),
      layoutVersion: home.layoutVersion,
      furniture: furniture.map((f: HomeFurniture & { item: { metadata: Prisma.JsonValue } }) => ({
        id: f.id,
        itemId: f.itemId,
        x: f.x,
        y: f.y,
        rotation: f.rotation,
        footprint: footprintOf(f.item.metadata),
      })),
    };
  }
}

function countBy(ids: string[]): Map<string, number> {
  const m = new Map<string, number>();
  for (const id of ids) m.set(id, (m.get(id) ?? 0) + 1);
  return m;
}
