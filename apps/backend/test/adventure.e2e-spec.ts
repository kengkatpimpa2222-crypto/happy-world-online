import { INestApplication } from '@nestjs/common';
import { EXPEDITION_SITES, MaterialCode, materialItemId } from '@hwo/shared-contracts';
import { PrismaService } from '../src/prisma/prisma.service';
import { createTestApp, key, newGuest, setFestivalClock, TestPlayer } from './helpers';

describe('Exploring and crafting (Phase 9)', () => {
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
  /** The trip "took" its full length: move its times back instead of waiting minutes. */
  const arrive = (id: string) =>
    prisma.$executeRaw`UPDATE "expeditions" SET "started_at" = "started_at" - interval '1 hour', "ends_at" = now() - interval '1 second' WHERE "id" = ${id}::uuid`;
  const shopOf = async (p: TestPlayer) => (await http().get('/api/v1/businesses').set(p.auth).expect(200)).body[0];
  const gold = async (p: TestPlayer) => (await http().get('/api/v1/player/wallet').set(p.auth).expect(200)).body.balances.GOLD as number;
  const craft = (p: TestPlayer, businessId: string, recipe: string, times: number, k = key()) =>
    http().post('/api/v1/crafting/craft').set(p.auth).set('Idempotency-Key', k).send({ recipe, businessId, times });
  /** Test-only shortcut: give materials through the real item ledger table. */
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

  it('a new player can explore the orchard; the other places unlock with the Explorer skill', async () => {
    const p = await newGuest(app);
    const o = await overview(p);
    expect(o.sites.map((s: { site: string; unlocked: boolean }) => [s.site, s.unlocked])).toEqual([
      ['ORCHARD', true],
      ['FLOWER_FOREST', false],
      ['HERB_VALLEY', false],
    ]);
    expect(o.active).toBeNull();
    expect(o.skills.EXPLORER).toMatchObject({ level: 1, xp: 0, nextLevelXp: 40 });
    expect(Object.values(o.materials).every((n) => n === 0)).toBe(true);
    const locked = await start(p, 'HERB_VALLEY').expect(409);
    expect(locked.body.error).toMatchObject({ code: 'EXPEDITION_LOCKED', details: { explorerLevel: 4 } });
  });

  it('a trip takes real time, one at a time, and brings materials and XP home', async () => {
    const p = await newGuest(app);
    await http().post('/api/v1/quests/explore_first_trip/start').set(p.auth).expect(201);
    const s = await start(p).expect(200);
    expect(s.body).toMatchObject({ site: 'ORCHARD', plan: 'STEADY', explorerLevel: 1, ready: false, claimed: false, result: null });
    expect(s.body.secondsLeft).toBeGreaterThan(EXPEDITION_SITES.ORCHARD.minutes * 60 - 5);
    expect((await start(p, 'ORCHARD', 'BOLD').expect(409)).body.error.code).toBe('EXPEDITION_IN_PROGRESS');
    const early = await claim(p, s.body.id).expect(409);
    expect(early.body.error.code).toBe('EXPEDITION_NOT_FINISHED');
    expect((await overview(p)).active).toMatchObject({ id: s.body.id, ready: false });

    await arrive(s.body.id);
    expect((await overview(p)).active).toMatchObject({ id: s.body.id, ready: true });
    const r = await claim(p, s.body.id).expect(200);
    expect(r.body).toMatchObject({ claimed: true, replayed: false });
    const { result } = r.body;
    expect(result.events).toHaveLength(EXPEDITION_SITES.ORCHARD.eventCount);
    const total = Object.values(result.loot as Record<string, number>).reduce((a, n) => a + n, 0);
    expect(total).toBeGreaterThan(0);
    for (const [code, n] of Object.entries(result.loot as Record<string, number>)) expect(r.body.materials[code]).toBe(n);
    expect(r.body.skills.EXPLORER.xp).toBe(result.explorerXp);

    // every material arrived through the item ledger, once
    const ledger = await prisma.inventoryTransaction.findMany({ where: { userId: p.userId, referenceId: s.body.id } });
    expect(ledger.reduce((a, t) => a + t.delta, 0)).toBe(total);
    expect(ledger.every((t) => t.type === 'EXPEDITION_LOOT')).toBe(true);
    const quests = (await http().get('/api/v1/quests').set(p.auth).expect(200)).body;
    const q = quests.find((x: { id: string }) => x.id === 'explore_first_trip');
    expect(q.progress).toMatchObject({ status: 'COMPLETED', value: 1 });

    // the next trip can start now
    await start(p, 'ORCHARD', 'CAREFUL').expect(200);
  });

  it('claiming twice (even at the same moment) tells the same story and adds the materials once', async () => {
    const p = await newGuest(app);
    const s = await start(p, 'ORCHARD', 'BOLD').expect(200);
    await arrive(s.body.id);
    const [a, b] = await Promise.all([claim(p, s.body.id), claim(p, s.body.id)]);
    expect([a.status, b.status]).toEqual([200, 200]);
    expect([a.body.replayed, b.body.replayed].sort()).toEqual([false, true]);
    expect(a.body.result).toEqual(b.body.result);
    const again = await claim(p, s.body.id).expect(200);
    expect(again.body.materials).toEqual(a.body.materials);
    const total = Object.values(a.body.result.loot as Record<string, number>).reduce((x, n) => x + n, 0);
    const held = await prisma.playerInventory.aggregate({ where: { userId: p.userId, item: { category: 'MATERIAL' } }, _sum: { quantity: true } });
    expect(held._sum.quantity).toBe(total);
  });

  it("nobody else can claim my trip", async () => {
    const owner = await newGuest(app);
    const other = await newGuest(app);
    const s = await start(owner).expect(200);
    await arrive(s.body.id);
    await claim(other, s.body.id).expect(404);
    await claim(owner, s.body.id).expect(200);
  });

  it('crafting turns materials and a small fee into craft-only goods in my storage', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    await giveMaterials(p, { MANGO: 3, STICKY_RICE: 2, COCONUT: 2 });
    const before = await gold(p);
    const list = (await http().get('/api/v1/crafting').set(p.auth).expect(200)).body;
    expect(list.recipes.find((r: { recipe: string }) => r.recipe === 'MANGO_STICKY_RICE')).toMatchObject({ unlocked: true, canMake: 2, outputUnits: 3, feeGold: 6 });
    expect(list.recipes.find((r: { recipe: string }) => r.recipe === 'HERBAL_BALM')).toMatchObject({ unlocked: false, crafterLevel: 2 });

    const r = await craft(p, shop.id, 'MANGO_STICKY_RICE', 2).expect(200);
    expect(r.body).toMatchObject({ replayed: false, unitsMade: 6, feeGold: 12 });
    expect(r.body.materials).toMatchObject({ MANGO: 1, STICKY_RICE: 0, COCONUT: 0 });
    expect(r.body.crafter.xp).toBe(10);
    const line = r.body.business.products.find((x: { productCode: string }) => x.productCode === 'MANGO_STICKY_RICE');
    expect(line).toMatchObject({ quantity: 6, avgUnitCost: 2, sellPrice: 35, craftOnly: true });
    expect(r.body.business.finance.totalExpense).toBe(12);
    expect(await gold(p)).toBe(before - 12);
  });

  it('a repeated craft request is done once; not enough materials changes nothing', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    await giveMaterials(p, { JASMINE: 6, MARIGOLD: 2 });
    const k = key();
    const [a, b] = await Promise.all([craft(p, shop.id, 'JASMINE_GARLAND', 2, k), craft(p, shop.id, 'JASMINE_GARLAND', 2, k)]);
    expect([a.status, b.status]).toEqual([200, 200]);
    expect([a.body.replayed, b.body.replayed].sort()).toEqual([false, true]);
    const goldNow = await gold(p);
    const short = await craft(p, shop.id, 'JASMINE_GARLAND', 1).expect(409);
    expect(short.body.error).toMatchObject({ code: 'NOT_ENOUGH_MATERIALS', details: { itemId: 'mat_jasmine', required: 3, have: 0 } });
    expect(await gold(p)).toBe(goldNow); // the fee was rolled back with the failed craft
    const line = (await shopOf(p)).products.find((x: { productCode: string }) => x.productCode === 'JASMINE_GARLAND');
    expect(line.quantity).toBe(4);
  });

  it('parallel crafts can never use more materials than I have', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    await giveMaterials(p, { MANGO: 3, STICKY_RICE: 3, COCONUT: 3 });
    const results = await Promise.all(Array.from({ length: 5 }, () => craft(p, shop.id, 'MANGO_STICKY_RICE', 1)));
    expect(results.filter((r) => r.status === 200)).toHaveLength(3);
    expect(results.filter((r) => r.status === 409).every((r) => r.body.error.code === 'NOT_ENOUGH_MATERIALS')).toBe(true);
    const mats = (await http().get('/api/v1/crafting').set(p.auth).expect(200)).body.materials;
    expect(mats).toMatchObject({ MANGO: 0, STICKY_RICE: 0, COCONUT: 0 });
  });

  it('recipes unlock with the Crafter skill, and Crafter 4 makes one more unit per craft', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    await giveMaterials(p, { HERB: 4, LEMONGRASS: 2, BEESWAX: 2 });
    expect((await craft(p, shop.id, 'HERBAL_BALM', 1).expect(409)).body.error.code).toBe('RECIPE_LOCKED');
    await giveSkillXp(p, 'CRAFTER', 360); // level 4
    const r = await craft(p, shop.id, 'HERBAL_BALM', 2).expect(200);
    expect(r.body.unitsMade).toBe(6);
    // every material has a use: wild honey goes into the jam
    await giveMaterials(p, { MANGO: 2, WILD_HONEY: 1 });
    expect((await craft(p, shop.id, 'HONEY_MANGO_JAM', 1).expect(200)).body.unitsMade).toBe(3);
  });

  it("craft-only goods can't be bought with Gold or made in a mini-game, and someone else's shop is off limits", async () => {
    const p = await newGuest(app);
    const other = await newGuest(app);
    const shop = await shopOf(p);
    const buy = await http().post(`/api/v1/businesses/${shop.id}/restock`).set(p.auth).set('Idempotency-Key', key()).send({ productCode: 'HERBAL_BALM', quantity: 5 }).expect(400);
    expect(buy.body.error.code).toBe('PRODUCT_CRAFT_ONLY');
    const mini = await http().post('/api/v1/minigames/start').set(p.auth).send({ businessId: shop.id, productCode: 'MANGO_STICKY_RICE' }).expect(400);
    expect(mini.body.error.code).toBe('NO_MINIGAME_FOR_PRODUCT');
    await giveMaterials(other, { MANGO: 1, STICKY_RICE: 1, COCONUT: 1 });
    await craft(other, shop.id, 'MANGO_STICKY_RICE', 1).expect(404);
  });

  it('crafted goods go on the market stall and NPC customers buy them', async () => {
    const p = await newGuest(app);
    const shop = await shopOf(p);
    await giveMaterials(p, { MANGO: 5, STICKY_RICE: 5, COCONUT: 5 });
    await craft(p, shop.id, 'MANGO_STICKY_RICE', 5).expect(200);
    await http().put(`/api/v1/businesses/${shop.id}/shelf`).set(p.auth).send({ productCode: 'MANGO_STICKY_RICE', quantity: 15 }).expect(200);
    await http().post(`/api/v1/businesses/${shop.id}/status`).set(p.auth).send({ status: 'OPEN' }).expect(200);
    await prisma.$executeRaw`UPDATE "businesses" SET "settled_tick" = "settled_tick" - 60 WHERE "id" = ${shop.id}::uuid`;
    const after = await shopOf(p);
    const line = after.products.find((x: { productCode: string }) => x.productCode === 'MANGO_STICKY_RICE');
    expect(line.shelfQuantity).toBeLessThan(15);
    expect(after.stall.cashBox).toBeGreaterThan(0);
  });
});
