import {
  CITY_PROJECTS,
  findLayoutProblem,
  footprintOf,
  HOME_MAX_FURNITURE,
  homeInterior,
  pieceCells,
  plotAddress,
  plotPosition,
  PLOTS_PER_STREET,
  saveHomeLayoutSchema,
  zoneById,
} from '@hwo/shared-contracts';

const fp = new Map<string, [number, number]>([
  ['chair', [1, 1]],
  ['table', [2, 1]],
  ['bed', [1, 2]],
]);

describe('home placement rules (Phase 10)', () => {
  it('turning a piece 90 or 270 degrees swaps its width and depth', () => {
    expect(pieceCells({ itemId: 'table', x: 2, y: 3, rotation: 0 }, [2, 1])).toEqual([{ x: 2, y: 3 }, { x: 3, y: 3 }]);
    expect(pieceCells({ itemId: 'table', x: 2, y: 3, rotation: 90 }, [2, 1])).toEqual([{ x: 2, y: 3 }, { x: 2, y: 4 }]);
    expect(pieceCells({ itemId: 'table', x: 2, y: 3, rotation: 180 }, [2, 1])).toHaveLength(2);
    expect(pieceCells({ itemId: 'bed', x: 0, y: 0, rotation: 270 }, [1, 2])).toEqual([{ x: 0, y: 0 }, { x: 1, y: 0 }]);
  });

  it('accepts a tidy room', () => {
    const ok = [
      { itemId: 'bed', x: 0, y: 0, rotation: 0 },
      { itemId: 'table', x: 3, y: 3, rotation: 0 },
      { itemId: 'chair', x: 7, y: 7, rotation: 270 },
    ];
    expect(findLayoutProblem(ok, fp, 8, 8)).toBeNull();
    expect(findLayoutProblem([], fp, 8, 8)).toBeNull();
  });

  it('rejects pieces that stick out of the room, overlap or are turned oddly', () => {
    expect(findLayoutProblem([{ itemId: 'table', x: 7, y: 0, rotation: 0 }], fp, 8, 8)).toEqual({ problem: 'OUT_OF_BOUNDS', index: 0 });
    expect(findLayoutProblem([{ itemId: 'bed', x: 0, y: 7, rotation: 0 }], fp, 8, 8)).toEqual({ problem: 'OUT_OF_BOUNDS', index: 0 });
    expect(findLayoutProblem([{ itemId: 'bed', x: 0, y: 7, rotation: 90 }], fp, 8, 8)).toBeNull(); // turned it fits
    expect(findLayoutProblem([{ itemId: 'chair', x: -1, y: 0, rotation: 0 }], fp, 8, 8)).toEqual({ problem: 'OUT_OF_BOUNDS', index: 0 });
    const overlap = [
      { itemId: 'table', x: 2, y: 2, rotation: 0 },
      { itemId: 'chair', x: 3, y: 2, rotation: 0 },
    ];
    expect(findLayoutProblem(overlap, fp, 8, 8)).toEqual({ problem: 'OVERLAP', index: 1, with: 0 });
    expect(findLayoutProblem([{ itemId: 'chair', x: 1, y: 1, rotation: 45 }], fp, 8, 8)).toEqual({ problem: 'BAD_ROTATION', index: 0 });
    const tooMany = Array.from({ length: HOME_MAX_FURNITURE + 1 }, (_, i) => ({ itemId: 'chair', x: i % 8, y: Math.floor(i / 8), rotation: 0 }));
    expect(findLayoutProblem(tooMany, fp, 8, 8)).toEqual({ problem: 'TOO_MANY', max: HOME_MAX_FURNITURE });
  });

  it('reads footprints defensively', () => {
    expect(footprintOf({ footprint: [2, 1] })).toEqual([2, 1]);
    expect(footprintOf({})).toEqual([1, 1]);
    expect(footprintOf({ footprint: [0, 9] })).toEqual([1, 1]);
    expect(footprintOf(null)).toEqual([1, 1]);
  });

  it('the save request refuses the same piece twice and odd rotations', () => {
    const id = '11111111-1111-4111-8111-111111111111';
    expect(saveHomeLayoutSchema.safeParse({ baseVersion: 0, pieces: [] }).success).toBe(true);
    expect(saveHomeLayoutSchema.safeParse({ baseVersion: 0, pieces: [{ id, itemId: 'a', x: 0, y: 0, rotation: 0 }, { id, itemId: 'a', x: 1, y: 0, rotation: 0 }] }).success).toBe(false);
    expect(saveHomeLayoutSchema.safeParse({ baseVersion: 0, pieces: [{ itemId: 'a', x: 0, y: 0, rotation: 45 }] }).success).toBe(false);
    expect(saveHomeLayoutSchema.safeParse({ baseVersion: 0, pieces: [{ itemId: 'a', x: 0, y: 0, rotation: 0, ownerId: id }] }).success).toBe(false);
  });

  it('the room is 1.5 world units per cell with the door at the bottom middle', () => {
    expect(homeInterior(8, 8)).toEqual({ width: 12, height: 12, spawn: { x: 6, y: 0.8 } });
  });
});

describe('residential district (Phase 10)', () => {
  const zone = zoneById('HOMES')!;

  it('numbers plots street by street', () => {
    expect(plotAddress(0)).toEqual({ plotNumber: 0, street: 0, slot: 0 });
    expect(plotAddress(PLOTS_PER_STREET + 3)).toEqual({ plotNumber: PLOTS_PER_STREET + 3, street: 1, slot: 3 });
  });

  it('every plot stands inside the zone, apart from the others, away from the portal and the office project', () => {
    const plots = Array.from({ length: PLOTS_PER_STREET }, (_, s) => plotPosition(s));
    const office = CITY_PROJECTS.find((p) => p.zone === 'HOMES')!;
    for (const [i, p] of plots.entries()) {
      for (const pt of [p, p.door]) {
        expect(pt.x).toBeGreaterThan(1);
        expect(pt.x).toBeLessThan(zone.width - 1);
        expect(pt.y).toBeGreaterThan(1);
        expect(pt.y).toBeLessThan(zone.height - 1);
      }
      expect(Math.hypot(p.x - office.x, p.y - office.y)).toBeGreaterThan(5);
      for (const portal of zone.portals) expect(Math.hypot(p.x - portal.x, p.y - portal.y)).toBeGreaterThan(5);
      for (const q of plots.slice(i + 1)) expect(Math.hypot(p.x - q.x, p.y - q.y)).toBeGreaterThanOrEqual(5);
    }
    expect(() => plotPosition(PLOTS_PER_STREET)).toThrow();
  });
});
