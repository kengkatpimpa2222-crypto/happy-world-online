import { INestApplication } from '@nestjs/common';
import {
  craftOutputUnits,
  EXPEDITION_PLAN_CODES,
  EXPEDITION_SITE_CODES,
  EXPEDITION_SITES,
  MATERIAL_CODES,
  MaterialCode,
  materialItemId,
  RECIPE_CODES,
  RECIPES,
} from '@hwo/shared-contracts';
import { PrismaService } from '../src/prisma/prisma.service';
import { createTestApp, key, newGuest, setFestivalClock, TestPlayer } from './helpers';

type Bag = Record<string, number>;

/**
 * Phase 9.1: the whole exploring and crafting loop end to end, over HTTP like the client: every site and
 * plan, the first-visit safety net, relogin mid-trip, retries and parallel requests, all four recipes and
 * the Explorer/Crafter skills.
 */
describe('Exploring and crafting, full verification (Phase 9.1)', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let http: Awaited<ReturnType<typeof createTestApp>>['http'];

  beforeAll(async () => {
    ({ app, prisma, http } = await createTestApp());
    setFestivalClock(app, 'weekday');
  });
  afterAll(async () => app.close());

  const overview = async (p: TestPlayer) => (await http().get('/api/v1/expeditions').set(p.auth).expect(200)).body;
  const start = (p: TestPlayer, site = 'ORCHARD', plan = 'STEADY') => http().post('/api/v1/expeditions/start').set(p.auth).send({ site, plan });
  const claim = (p: TestPlayer, id: string) => http().post(`/api/v1/expeditions/${id}/claim`).set(p.auth);
  const arrive = (id: string) =>
    prisma.$executeRaw`UPDATE "expeditions" SET "started_at" = "started_at" - interval '1 hour', "ends_at" = now() - interval '1 second' WHERE "id" = ${id}::uuid`;
  /** start, arrive, claim: one whole trip */
  const trip = async (p: TestPlayer, site: string, plan: string) => {
    const s = await start(p, site, plan).expect(200);
    await arrive(s.body.id);
    return (await claim(p, s.body.id).expect(200)).body;
  };
  const shopOf = async (p: TestPlayer) => (await http().get('/api/v1/businesses').set(p.auth).expect(200)).body[0];
  const gold = async (p: TestPlayer) => (await http().get('/api/v1/player/wallet').set(p.auth).expect(200)).body.balances.GOLD as number;
  const materials = async (p: TestPlayer): Promise<Bag> => (await http().get('/api/v1/crafting').set(p.auth).expect(200)).body.materials;
  const craft = (p: TestPlayer, businessId: string, recipe: string, times: number, k = key()) =>
    http().post('/api/v1/crafting/craft').set(p.auth).set('Idempotency-Key', k).send({ recipe, businessId, times });
  const giveMaterials = async (p: TestPlayer, bag: Partial<Record<MaterialCode, number>>) => {
    for (const [code, n] of Object.entries(bag)) {
      const itemId = materialItemId(code as MaterialCode);
      const row = await prisma.playerInventory.upsert({
        where: { userId_itemId: { userId: p.userId, itemId } },
        create: { userId: p.userId, itemId, quantity: n! },
        update: { quantity: { increment: n! } },
      });
      await prisma.inventoryTransaction.create({
        data: { userId: p.userId, itemId, type: 'ADMIN_ADJUSTMENT', delta: n!, quantityAfter: row.quantity, idempotencyKey: `test-grant:${key()}` },
      });
    }
  };
  const giveSkillXp = (p: TestPlayer, skill: 'EXPLORER' | 'CRAFTER', xp: number) =>
    prisma.playerSkill.upsert({ where: { userId_skill: { userId: p.userId, skill } }, create: { userId: p.userId, skill, xp }, update: { xp } });
  const sets = (bag: Bag, recipe: keyof typeof RECIPES) =>
    Math.min(...Object.entries(RECIPES[recipe].inputs).map(([c, n]) => Math.floor((bag[c] ?? 0) / n!)));
  const sum = (bag: Bag) => Object.values(bag).reduce((a, n) => a + n, 0);
  /** a fresh session for the same account, like closing the app and signing back in */
  const relogin = async (p: TestPlayer): Promise<TestPlayer> => {
    const r = await http().post('/api/v1/auth/refresh').send({ refreshToken: p.refreshToken }).expect(200);
    return { ...p, accessToken: r.body.accessToken, refreshToken: r.body.refreshToken, auth: { Authorization: `Bearer ${r.body.accessToken}` } };
  };

  it("a new player's first orchard trip always brings home a full set for mango sticky rice, once", async () => {
    // several players and every plan: the safety net must hold whatever the seed
    for (const plan of EXPEDITION_PLAN_CODES) {
      for (let i = 0; i < 4; i++) {
        const p = await newGuest(app);
        const before = await overview(p);
        expect(before.sites[0]).toMatchObject({ site: 'ORCHARD', firstVisit: true, firstVisitRecipe: 'MANGO_STICKY_RICE' });
        const first = await trip(p, 'ORCHARD', plan);
        expect(sets(first.materials, 'MANGO_STICKY_RICE')).toBeGreaterThanOrEqual(1);
        // what was topped up is part of the loot, and every unit came through the item ledger
        for (const [c, n] of Object.entries(first.result.firstVisitBonus as Bag)) expect(first.result.loot[c]).toBeGreaterThanOrEqual(n);
        const ledger = await prisma.inventoryTransaction.aggregate({ where: { userId: p.userId, referenceId: first.id }, _sum: { delta: true } });
        expect(ledger._sum.delta).toBe(sum(first.result.loot));
        // the crafting screen agrees: the first recipe can be made right away
        const list = (await http().get('/api/v1/crafting').set(p.auth).expect(200)).body;
        expect(list.recipes.find((r: { recipe: string }) => r.recipe === 'MANGO_STICKY_RICE').canMake).toBeGreaterThanOrEqual(1);
        expect((await overview(p)).sites[0].firstVisit).toBe(false);
        if (i === 0) {
          const second = await trip(p, 'ORCHARD', plan);
          expect(second.result.firstVisitBonus).toEqual({});
        }
      }
    }
  });

  it('every site with every plan: events, losses only on bold trips, materials and XP added once', async () => {
    const p = await newGuest(app);
    await giveSkillXp(p, 'EXPLORER', 360); // level 4: every site is open
    expect((await overview(p)).sites.every((s: { unlocked: boolean }) => s.unlocked)).toBe(true);
    let xp = 360;
    for (const site of EXPEDITION_SITE_CODES) {
      for (const plan of EXPEDITION_PLAN_CODES) {
        const had = await materials(p);
        const t = await trip(p, site, plan);
        expect(t).toMatchObject({ site, plan, explorerLevel: 4, claimed: true, replayed: false });
        const { result } = t;
        expect(result.events).toHaveLength(EXPEDITION_SITES[site].eventCount);
        for (const e of result.events as { event: string; success: boolean; lost: Bag; gained: Bag }[]) {
          expect(Object.keys(EXPEDITION_SITES[site].events)).toContain(e.event);
          if (e.success) expect(e.lost).toEqual({});
          else expect(e.gained).toEqual({});
          if (plan !== 'BOLD') expect(e.lost).toEqual({});
        }
        for (const c of MATERIAL_CODES) expect(t.materials[c]).toBe((had[c] ?? 0) + (result.loot[c] ?? 0));
        xp += result.explorerXp;
        expect(t.skills.EXPLORER.xp).toBe(xp);
      }
    }
    // the first trip to each site got the net, later ones did not
    const trips = await prisma.expedition.findMany({ where: { userId: p.userId }, orderBy: { startedAt: 'asc' } });
    expect(trips).toHaveLength(9);
    for (const [i, row] of trips.entries()) {
      const r = row.result as { firstVisitBonus: Bag; loot: Bag };
      if (i % 3 === 0) expect(sets(r.loot, EXPEDITION_SITES[row.site as 'ORCHARD'].firstVisitRecipe)).toBeGreaterThanOrEqual(1);
      else expect(r.firstVisitBonus).toEqual({});
    }
  });

  it('a bold trip that goes badly can lose materials, but never below zero and never home empty-handed', async () => {
    const p = await newGuest(app);
    await giveSkillXp(p, 'EXPLORER', 360);
    let sawLoss = false;
    for (let i = 0; i < 12 && !sawLoss; i++) {
      const t = await trip(p, 'HERB_VALLEY', 'BOLD');
      const lost = (t.result.events as { lost: Bag }[]).reduce((a, e) => a + sum(e.lost), 0);
      if (lost > 0) sawLoss = true;
      expect(sum(t.result.loot)).toBeGreaterThan(0);
      expect(Object.values(t.result.loot as Bag).every((n) => n > 0)).toBe(true);
    }
    expect(sawLoss).toBe(true);
  });

  it('closing the app mid-trip: a new session sees the same trip, refreshing never starts one, and it can be claimed after', async () => {
    const p = await newGuest(app);
    const s = await start(p, 'ORCHARD', 'CAREFUL').expect(200);
    const again = await relogin(p);
    for (let i = 0; i < 5; i++) {
      const o = await overview(again);
      expect(o.active).toMatchObject({ id: s.body.id, endsAt: s.body.endsAt, ready: false, claimed: false, result: null });
      expect(o.active.secondsLeft).toBeLessThanOrEqual(s.body.secondsLeft);
    }
    expect(await prisma.expedition.count({ where: { userId: p.userId } })).toBe(1);
    // signing in again retires the old session's token; the client signs in again rather than guessing
    await http().get('/api/v1/expeditions').set(p.auth).expect(401);
    expect((await claim(again, s.body.id).expect(409)).body.error).toMatchObject({ code: 'EXPEDITION_NOT_FINISHED' });

    await arrive(s.body.id);
    const done = await claim(again, s.body.id).expect(200);
    expect(done.body.replayed).toBe(false);
    // a retry (a reconnect after a timeout) gets the same story and nothing more
    const retry = await claim(again, s.body.id).expect(200);
    expect(retry.body).toMatchObject({ replayed: true, result: done.body.result, materials: done.body.materials });
  });

  it('parallel starts make exactly one trip; five parallel claims add the materials once', async () => {
    const p = await newGuest(app);
    const starts = await Promise.all(Array.from({ length: 5 }, () => start(p, 'ORCHARD', 'STEADY')));
    expect(starts.filter((r) => r.status === 200)).toHaveLength(1);
    expect(starts.filter((r) => r.status === 409).every((r) => r.body.error.code === 'EXPEDITION_IN_PROGRESS')).toBe(true);
    expect(await prisma.expedition.count({ where: { userId: p.userId } })).toBe(1);

    const id = starts.find((r) => r.status === 200)!.body.id;
    await arrive(id);
    const claims = await Promise.all(Array.from({ length: 5 }, () => claim(p, id)));
    expect(claims.every((r) => r.status === 200)).toBe(true);
    expect(claims.filter((r) => !r.body.replayed)).toHaveLength(1);
    const loot = claims[0]!.body.result.loot as Bag;
    expect(await materials(p)).toMatchObject(loot);
    expect(await prisma.inventoryTransaction.count({ where: { userId: p.userId, referenceId: id } })).toBe(Object.keys(loot).length);
    const skill = await prisma.playerSkill.findUniqueOrThrow({ where: { userId_skill: { userId: p.userId, skill: 'EXPLORER' } } });
    expect(skill.xp).toBe(claims[0]!.body.result.explorerXp);
  });

  it('all four recipes: exact materials used, fee, cost per unit, storage, shop expense and craft-only flags', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    await giveSkillXp(p, 'CRAFTER', 40); // level 2: every recipe is open
    await giveMaterials(p, { MANGO: 4, STICKY_RICE: 2, COCONUT: 2, JASMINE: 6, MARIGOLD: 2, HERB: 4, LEMONGRASS: 2, BEESWAX: 2, WILD_HONEY: 1 });
    let expense = (await shopOf(p)).finance.totalExpense as number;
    for (const recipe of RECIPE_CODES) {
      const r = RECIPES[recipe];
      const times = recipe === 'HONEY_MANGO_JAM' ? 1 : 2;
      const mats = await materials(p);
      const goldBefore = await gold(p);
      const lineBefore = (await shopOf(p)).products.find((x: { productCode: string }) => x.productCode === r.productCode);
      const res = await craft(p, shop.id, recipe, times).expect(200);
      const units = craftOutputUnits(recipe, 2) * times;
      expect(res.body).toMatchObject({ replayed: false, recipe, productCode: r.productCode, unitsMade: units, feeGold: r.feeGold * times });
      for (const c of MATERIAL_CODES) expect(res.body.materials[c]).toBe(mats[c]! - (r.inputs[c] ?? 0) * times);
      expect(await gold(p)).toBe(goldBefore - r.feeGold * times);
      const line = res.body.business.products.find((x: { productCode: string }) => x.productCode === r.productCode);
      expect(line).toMatchObject({ quantity: (lineBefore?.quantity ?? 0) + units, avgUnitCost: Math.round((r.feeGold * times) / units), craftOnly: true });
      expense += r.feeGold * times;
      expect(res.body.business.finance.totalExpense).toBe(expense);
      // the shop screen shows "งานฝีมือ" for these instead of a Gold restock: the server refuses that restock
      const buy = await http().post(`/api/v1/businesses/${shop.id}/restock`).set(p.auth).set('Idempotency-Key', key()).send({ productCode: r.productCode, quantity: 1 }).expect(400);
      expect(buy.body.error.code).toBe('PRODUCT_CRAFT_ONLY');
    }
    expect(Object.values(await materials(p)).every((n) => n === 0)).toBe(true);
    const catalog = (await shopOf(p)).products as { productCode: string; craftOnly: boolean }[];
    for (const x of catalog) expect(x.craftOnly).toBe(RECIPE_CODES.some((c) => RECIPES[c].productCode === x.productCode));
  });

  it('a craft retried with the same key (timeout, double tap) charges and consumes once', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    await giveMaterials(p, { MANGO: 2, STICKY_RICE: 2, COCONUT: 2 });
    const k = key();
    const first = await craft(p, shop.id, 'MANGO_STICKY_RICE', 1, k).expect(200);
    const goldAfter = await gold(p);
    for (let i = 0; i < 3; i++) {
      const again = await craft(p, shop.id, 'MANGO_STICKY_RICE', 1, k).expect(200);
      expect(again.body).toMatchObject({ replayed: true, unitsMade: 0 });
    }
    expect(await gold(p)).toBe(goldAfter);
    expect(await materials(p)).toMatchObject({ MANGO: 1, STICKY_RICE: 1, COCONUT: 1 });
    const line = (await shopOf(p)).products.find((x: { productCode: string }) => x.productCode === 'MANGO_STICKY_RICE');
    expect(line.quantity).toBe(first.body.unitsMade);
    expect(await prisma.inventoryTransaction.count({ where: { userId: p.userId, type: 'CRAFT_CONSUME' } })).toBe(3);
  });

  it('Explorer and Crafter level up through play and keep their levels after signing in again', async () => {
    let p = await newGuest(app);
    const shop = await shopOf(p);
    let explorerUp: number | null = null;
    for (let i = 1; i <= 5 && explorerUp === null; i++) {
      const t = await trip(p, 'ORCHARD', 'CAREFUL');
      if (t.result.explorerLeveledUp) {
        explorerUp = i;
        expect(t.result.explorerLevelAfter).toBe(2);
        expect(t.skills.EXPLORER.level).toBe(2);
      } else expect(t.skills.EXPLORER.level).toBe(1);
    }
    expect(explorerUp).not.toBeNull();
    expect(explorerUp!).toBeLessThanOrEqual(4); // 10 XP + 3 per event handled well; 40 XP for level 2

    await giveMaterials(p, { MANGO: 10, STICKY_RICE: 10, COCONUT: 10 });
    const c1 = await craft(p, shop.id, 'MANGO_STICKY_RICE', 7).expect(200); // 35 XP
    expect(c1.body).toMatchObject({ crafterLeveledUp: false, crafter: { level: 1, xp: 35 } });
    const c2 = await craft(p, shop.id, 'MANGO_STICKY_RICE', 1).expect(200); // 40 XP
    expect(c2.body).toMatchObject({ crafterLeveledUp: true, crafter: { level: 2, xp: 40 } });

    const before = (await overview(p)).skills;
    p = await relogin(p);
    const after = await overview(p);
    expect(after.skills).toEqual(before);
    expect(after.skills).toMatchObject({ EXPLORER: { level: 2 }, CRAFTER: { level: 2 } });
    expect(after.sites.find((s: { site: string }) => s.site === 'FLOWER_FOREST').unlocked).toBe(true);
    const recipes = (await http().get('/api/v1/crafting').set(p.auth).expect(200)).body.recipes;
    expect(recipes.every((r: { unlocked: boolean }) => r.unlocked)).toBe(true);
  });
});
