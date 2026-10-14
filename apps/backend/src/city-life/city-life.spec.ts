import {
  bangkokHour,
  CITY_PROJECTS,
  cityProjectIndexAt,
  cityProjectThresholds,
  crowdMix,
  dayPhase,
  HOURLY_CROWD,
  SEASON_WEATHER,
  seasonOf,
  WEATHER_BLOCK_MS,
  weatherAt,
  weatherOfBlock,
  WEATHERS,
} from '@hwo/shared-contracts';
import { simulateCustomers, StallLine, TickWorld } from '../business/living-shop-sim';
import { AppConfig } from '../config/app-config.service';
import { FestivalService } from '../festival/festival.service';
import { CityLifeService } from './city-life.service';

const HOUR = 3600_000;
const project = (code: string) => CITY_PROJECTS.find((p) => p.code === code)!;

describe('city clock, seasons and weather', () => {
  it('tells the Bangkok hour and the part of the day', () => {
    const midnightBkk = Date.UTC(2026, 9, 14, 17, 0, 0); // 2026-10-15 00:00 in Bangkok
    expect(bangkokHour(midnightBkk)).toBe(0);
    expect(bangkokHour(midnightBkk + 7.5 * HOUR)).toBe(7.5);
    expect(bangkokHour(midnightBkk - HOUR)).toBe(23);
    expect(dayPhase(3)).toBe('NIGHT');
    expect(dayPhase(6)).toBe('DAWN');
    expect(dayPhase(8)).toBe('MORNING');
    expect(dayPhase(12)).toBe('NOON');
    expect(dayPhase(15)).toBe('AFTERNOON');
    expect(dayPhase(19)).toBe('EVENING');
    expect(dayPhase(21)).toBe('NIGHT');
  });

  it('follows the three Thai seasons', () => {
    expect([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12].map(seasonOf).join(',')).toBe('COOL,COOL,HOT,HOT,HOT,RAINY,RAINY,RAINY,RAINY,RAINY,COOL,COOL');
    for (const odds of Object.values(SEASON_WEATHER)) expect(Object.values(odds).reduce((a, b) => a + b, 0)).toBeCloseTo(1, 9);
  });

  it('weather is the same for everyone in a 3-hour block and changes between blocks', () => {
    const t = Date.UTC(2026, 9, 14, 6, 0, 0);
    const block = Math.floor(t / WEATHER_BLOCK_MS);
    expect(weatherAt(block * WEATHER_BLOCK_MS)).toBe(weatherAt((block + 1) * WEATHER_BLOCK_MS - 1));
    const seen = new Set(Array.from({ length: 200 }, (_, i) => weatherOfBlock(block + i)));
    expect(seen.size).toBe(WEATHERS.length);
  });

  it('rains much more in the rainy season than in the hot season', () => {
    const count = (startUtc: number, w: string) => {
      let n = 0;
      const first = Math.floor(startUtc / WEATHER_BLOCK_MS);
      for (let i = 0; i < 8 * 60; i++) if (weatherOfBlock(first + i) === w) n++; // 60 days
      return n / (8 * 60);
    };
    const rainy = count(Date.UTC(2026, 6, 1), 'RAIN'); // July
    const hot = count(Date.UTC(2026, 3, 1), 'RAIN'); // April
    expect(rainy).toBeGreaterThan(0.3);
    expect(hot).toBeLessThan(0.12);
    expect(count(Date.UTC(2026, 3, 1), 'HOT')).toBeGreaterThan(0.35);
  });

  it('keeps the daily rhythm close to the old average so the economy stays the same', () => {
    expect(HOURLY_CROWD).toHaveLength(24);
    const avg = HOURLY_CROWD.reduce((a, b) => a + b, 0) / 24;
    expect(avg).toBeGreaterThan(0.9);
    expect(avg).toBeLessThan(1.05);
    expect(crowdMix(8).STUDENT).toBeGreaterThan(1);
    expect(crowdMix(12).WORKER).toBeGreaterThan(1);
    expect(crowdMix(23).TOURIST).toBeGreaterThan(1);
    expect(crowdMix(10)).toEqual({});
  });
});

describe('city projects roadmap', () => {
  it('finishes projects one after another at cumulative totals', () => {
    const ends = cityProjectThresholds();
    expect(ends[0]).toBe(CITY_PROJECTS[0]!.cost);
    expect(ends[ends.length - 1]).toBe(CITY_PROJECTS.reduce((a, p) => a + p.cost, 0));
    expect(cityProjectIndexAt(0)).toBe(0);
    expect(cityProjectIndexAt(ends[0]! - 1)).toBe(0);
    expect(cityProjectIndexAt(ends[0]!)).toBe(1);
    expect(cityProjectIndexAt(ends[ends.length - 1]!)).toBe(CITY_PROJECTS.length);
    expect(new Set(CITY_PROJECTS.map((p) => p.code)).size).toBe(CITY_PROJECTS.length);
  });
});

describe('time, weather and city projects in the NPC customer simulation', () => {
  const stall = (): StallLine[] => [
    { productCode: 'BREAD', shelfQuantity: 100_000, avgUnitCost: 8, sellPrice: 12 },
    { productCode: 'LEMONADE', shelfQuantity: 100_000, avgUnitCost: 5, sellPrice: 8 },
    { productCode: 'MEATBALL', shelfQuantity: 100_000, avgUnitCost: 6, sellPrice: 10 },
  ];
  const run = (world: TickWorld, seed = 'shop-w') => simulateCustomers(seed, 3, 0, 3000, stall(), () => world);
  const share = (r: ReturnType<typeof run>, p: 'LEMONADE' | 'MEATBALL') => (r.perProduct[p]?.wanted ?? 0) / r.totals.customers;
  const typeShare = (r: ReturnType<typeof run>, t: string) => r.visits.filter((v) => v.customerType === t).length / r.visits.length;

  it('a neutral hour on a cloudy day is exactly the old market', () => {
    const old = simulateCustomers('shop-n', 3, 0, 2000, stall());
    expect(simulateCustomers('shop-n', 3, 0, 2000, stall(), () => ({ hour: 10, weather: 'CLOUDY', built: [] }))).toEqual(old);
  });

  it('the night market is quiet and the lunch rush is busy', () => {
    const night = run({ hour: 3, weather: 'CLOUDY' });
    const lunch = run({ hour: 12, weather: 'CLOUDY' });
    expect(lunch.totals.customers).toBeGreaterThan(night.totals.customers * 4);
    expect(typeShare(lunch, 'WORKER')).toBeGreaterThan(typeShare(run({ hour: 10, weather: 'CLOUDY' }), 'WORKER'));
    expect(typeShare(night, 'TOURIST')).toBeGreaterThan(typeShare(lunch, 'TOURIST'));
  });

  it('rain thins the crowd and makes warm food sell; heat sells lemonade', () => {
    const cloudy = run({ hour: 10, weather: 'CLOUDY' });
    const rain = run({ hour: 10, weather: 'RAIN' });
    const hot = run({ hour: 10, weather: 'HOT' });
    expect(rain.totals.customers).toBeLessThan(cloudy.totals.customers * 0.8);
    expect(share(rain, 'MEATBALL')).toBeGreaterThan(share(cloudy, 'MEATBALL') * 1.2);
    expect(share(rain, 'LEMONADE')).toBeLessThan(share(cloudy, 'LEMONADE') * 0.8);
    expect(share(hot, 'LEMONADE')).toBeGreaterThan(share(cloudy, 'LEMONADE') * 1.4);
  });

  it('finished city projects bring their crowd for good', () => {
    const before = run({ hour: 10, weather: 'CLOUDY', built: [] });
    const park = run({ hour: 10, weather: 'CLOUDY', built: [project('PARK')] });
    const bus = run({ hour: 10, weather: 'CLOUDY', built: [project('PARK'), project('BUS_STOP')] });
    expect(park.totals.customers).toBeGreaterThan(before.totals.customers);
    expect(typeShare(bus, 'TOURIST')).toBeGreaterThan(typeShare(park, 'TOURIST') * 1.15);
  });

  it('stays deterministic tick by tick across a change of weather', () => {
    const world = (tick: number): TickWorld => ({ hour: (tick / 60) % 24, weather: tick < 130 ? 'SUNNY' : 'RAIN' });
    const all = simulateCustomers('shop-d', 2, 100, 160, stall(), world);
    let current = stall();
    const visits = [];
    for (let t = 100; t < 160; t++) {
      const r = simulateCustomers('shop-d', 2, t, t + 1, current, world);
      visits.push(...r.visits);
      current = r.stall;
    }
    expect(visits).toEqual(all.visits);
  });
});

describe('CityLifeService.worldFor', () => {
  const make = (offsetMs: number) =>
    new CityLifeService(
      null as never,
      { clockOffsetMs: offsetMs, nowMs: () => Date.now() + offsetMs } as unknown as FestivalService,
      null as never,
      null as never,
      { get: () => undefined } as unknown as AppConfig,
    );

  it('uses the shifted city clock and only counts projects finished before the tick', () => {
    const svc = make(2 * HOUR);
    const tick = Math.floor(Date.UTC(2026, 9, 14, 3, 0, 0) / 60_000); // 10:00 Bangkok real time
    const built = [{ def: project('PARK'), completedAtMs: tick * 60_000 }];
    const w = svc.worldFor(built);
    expect(w(tick)!.hour).toBe(12); // +2 h offset
    expect(w(tick)!.built).toEqual([project('PARK')]);
    expect(w(tick - 1)!.built).toEqual([]);
    expect(w(tick)!.weather).toBe(weatherAt(tick * 60_000 + 2 * HOUR));
    svc.weatherOverride = 'RAIN';
    svc.hourOverride = 20;
    expect(w(tick)).toMatchObject({ hour: 20, weather: 'RAIN' });
  });

  it('describes now: clock text, forecast blocks and what sells', () => {
    const svc = make(0);
    svc.hourOverride = 13.5;
    svc.weatherOverride = 'RAIN';
    const n = svc.now();
    expect(n.clock).toBe('13:30');
    expect(n.phase).toBe('NOON');
    expect(n.weatherTh).toBe('ฝนตก');
    expect(n.demand[0]).toEqual({ productCode: 'MEATBALL', demand: 1.5 });
    expect(n.forecast).toHaveLength(4);
    expect(new Date(n.forecast[0]!.at).getTime()).toBe(new Date(n.nextChangeAt).getTime());
    expect(n.busyTypes).toContain('WORKER');
  });
});
