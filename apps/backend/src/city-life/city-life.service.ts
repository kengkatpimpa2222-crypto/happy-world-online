import { Injectable } from '@nestjs/common';
import { AgeBand } from '@prisma/client';
import {
  activeFestival,
  bangkokHour,
  bangkokMonth,
  BusinessProductCode,
  CITY_PROJECTS,
  cityProjectIndexAt,
  cityProjectThresholds,
  CityProjectDef,
  crowdMix,
  DAY_PHASE_TH,
  dayPhase,
  HOURLY_CROWD,
  SEASON_TH,
  seasonOf,
  SHOP_TICK_SECONDS,
  Weather,
  WEATHER_BLOCK_MS,
  WEATHER_EFFECTS,
  WEATHER_TH,
  WEATHER_TIP_TH,
  weatherAt,
  WsEvent,
} from '@hwo/shared-contracts';
import { AuthUser } from '../common/decorators';
import { AppConfig } from '../config/app-config.service';
import { FestivalService } from '../festival/festival.service';
import { PrismaService, Tx } from '../prisma/prisma.service';
import { RealtimeService } from '../realtime/realtime.service';
import { SocialGraphService } from '../social/social-graph.service';
import type { WorldAt } from '../business/living-shop-sim';

/** A city project that was just finished; published after the transaction commits. */
export interface CityProjectBuilt {
  ageBand: AgeBand;
  projectCode: string;
  index: number;
  points: number;
}

export interface CityGrowthResult {
  added: number;
  built: CityProjectBuilt[];
}

const TOP_BUILDERS = 5;
const FORECAST_BLOCKS = 4;
const PROJECTS = new Map(CITY_PROJECTS.map((p, i) => [p.code, { def: p, index: i }]));

/**
 * Phase 7 — the living city: real Bangkok time, weather, and the city growing from what all its
 * players sell. Time and weather are pure functions of the clock (shared-contracts); this service
 * only adds the dev/test overrides, the city growth tables and the reads.
 */
@Injectable()
export class CityLifeService {
  /** Tests / QA: force the weather or the hour. null = the real thing. */
  weatherOverride: Weather | null;
  hourOverride: number | null = null;

  constructor(
    private readonly prisma: PrismaService,
    private readonly festival: FestivalService,
    private readonly realtime: RealtimeService,
    private readonly graph: SocialGraphService,
    config: AppConfig,
  ) {
    this.weatherOverride = config.get('CITY_WEATHER_OVERRIDE') ?? null;
  }

  /** City clock = the festival clock (one offset moves both for QA). */
  nowMs(): number {
    return this.festival.nowMs();
  }

  hourAt(ms: number): number {
    return this.hourOverride ?? bangkokHour(ms);
  }

  weatherAt(ms: number): Weather {
    return this.weatherOverride ?? weatherAt(ms);
  }

  /** What the market is like in each tick of a shop's settlement, given its city's finished projects. */
  worldFor(built: { def: CityProjectDef; completedAtMs: number }[]): WorldAt {
    return (tick) => {
      const real = tick * SHOP_TICK_SECONDS * 1000;
      const ms = real + this.festival.clockOffsetMs;
      return {
        festival: activeFestival(ms),
        hour: this.hourAt(ms),
        weather: this.weatherAt(ms),
        built: built.filter((b) => real >= b.completedAtMs).map((b) => b.def),
      };
    };
  }

  /** The finished projects of a player's city (inside the settlement transaction). */
  async builtFor(tx: Tx, userId: string): Promise<{ def: CityProjectDef; completedAtMs: number }[]> {
    const rows = await tx.$queryRaw<{ project_code: string; completed_at: Date }[]>`
      SELECT c."project_code", c."completed_at" FROM "city_project_completions" c
        JOIN "users" u ON u."age_band" = c."age_band"
       WHERE u."id" = ${userId}::uuid`;
    return rows.flatMap((r) => {
      const p = PROJECTS.get(r.project_code);
      return p ? [{ def: p.def, completedAtMs: r.completed_at.getTime() }] : [];
    });
  }

  // ---------- growth (called inside the shop settlement transaction) ----------

  /**
   * Every unit sold adds a build point to the seller's city. Points go to the project being built
   * (split across two projects when a sale finishes one), and finishing a project records it for good.
   */
  async recordSales(tx: Tx, userId: string, units: number): Promise<CityGrowthResult> {
    const result: CityGrowthResult = { added: 0, built: [] };
    if (units <= 0) return result;
    const rows = await tx.$queryRaw<{ age_band: AgeBand; points: number }[]>`
      INSERT INTO "city_growth" ("age_band", "points", "updated_at")
      SELECT u."age_band", ${units}, now() FROM "users" u WHERE u."id" = ${userId}::uuid
      ON CONFLICT ("age_band") DO UPDATE SET "points" = "city_growth"."points" + EXCLUDED."points", "updated_at" = now()
      RETURNING "age_band", "points"`;
    const row = rows[0];
    if (!row) return result;
    const after = row.points;
    const before = after - units;
    const ends = cityProjectThresholds();
    for (let i = 0; i < CITY_PROJECTS.length; i++) {
      const start = i === 0 ? 0 : ends[i - 1]!;
      const share = Math.min(after, ends[i]!) - Math.max(before, start);
      if (share <= 0) continue;
      const code = CITY_PROJECTS[i]!.code;
      await tx.$executeRaw`
        INSERT INTO "city_builder_contributions" ("project_code", "user_id", "age_band", "points", "updated_at")
        VALUES (${code}, ${userId}::uuid, ${row.age_band}::"AgeBand", ${share}, now())
        ON CONFLICT ("project_code", "user_id")
        DO UPDATE SET "points" = "city_builder_contributions"."points" + EXCLUDED."points", "updated_at" = now()`;
      if (before < ends[i]! && after >= ends[i]!) {
        const done = await tx.$executeRaw`
          INSERT INTO "city_project_completions" ("age_band", "project_code") VALUES (${row.age_band}::"AgeBand", ${code})
          ON CONFLICT DO NOTHING`;
        if (done === 1) result.built.push({ ageBand: row.age_band, projectCode: code, index: i, points: after });
      }
    }
    result.added = units;
    return result;
  }

  /** Tells the whole city (same age band only) that a project was finished. */
  publishBuilt(built: CityProjectBuilt[]): void {
    for (const b of built) {
      const p = CITY_PROJECTS[b.index]!;
      this.realtime.toCity(b.ageBand, WsEvent.CITY_GROWTH, {
        projectCode: b.projectCode,
        nameTh: p.nameTh,
        effectTh: p.effectTh,
        index: b.index,
        points: b.points,
        next: CITY_PROJECTS[b.index + 1]?.code ?? null,
      });
    }
  }

  // ---------- reads ----------

  /** The city's clock, light and weather right now, a short forecast and what sells well. */
  now() {
    const ms = this.nowMs();
    const hour = this.hourAt(ms);
    const weather = this.weatherAt(ms);
    const season = seasonOf(bangkokMonth(ms));
    const phase = dayPhase(hour);
    const block = Math.floor(ms / WEATHER_BLOCK_MS);
    const effect = WEATHER_EFFECTS[weather];
    const mix = crowdMix(hour);
    const h = Math.floor(hour);
    const m = Math.floor((hour - h) * 60);
    return {
      now: new Date(ms).toISOString(),
      hour: Math.round(hour * 1000) / 1000,
      clock: `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`,
      phase,
      phaseTh: DAY_PHASE_TH[phase],
      season,
      seasonTh: SEASON_TH[season],
      weather,
      weatherTh: WEATHER_TH[weather],
      tipTh: WEATHER_TIP_TH[weather],
      crowd: Math.round(HOURLY_CROWD[h % 24]! * effect.crowd * 100) / 100,
      busyTypes: Object.entries(mix)
        .filter(([, v]) => (v ?? 1) >= 1.3)
        .map(([k]) => k),
      demand: Object.entries(effect.demand)
        .map(([productCode, demand]) => ({ productCode: productCode as BusinessProductCode, demand: demand! }))
        .sort((a, b) => b.demand - a.demand),
      nextChangeAt: new Date((block + 1) * WEATHER_BLOCK_MS).toISOString(),
      forecast: Array.from({ length: FORECAST_BLOCKS }, (_, i) => {
        const at = (block + 1 + i) * WEATHER_BLOCK_MS;
        const w = this.weatherAt(at);
        return { at: new Date(at).toISOString(), weather: w, weatherTh: WEATHER_TH[w] };
      }),
    };
  }

  /** The city's roadmap: what stands already, what is being built and who is helping. */
  async growth(user: AuthUser) {
    const [city, done] = await Promise.all([
      this.prisma.cityGrowth.findUnique({ where: { ageBand: user.ageBand } }),
      this.prisma.cityProjectCompletion.findMany({ where: { ageBand: user.ageBand } }),
    ]);
    const points = city?.points ?? 0;
    const completedAt = new Map(done.map((d) => [d.projectCode, d.completedAt]));
    const ends = cityProjectThresholds();
    const current = cityProjectIndexAt(points);
    const currentCode = CITY_PROJECTS[current]?.code ?? null;
    const [mine, myTotal] = await Promise.all([
      currentCode ? this.prisma.cityBuilderContribution.findUnique({ where: { projectCode_userId: { projectCode: currentCode, userId: user.userId } } }) : null,
      this.prisma.cityBuilderContribution.aggregate({ where: { userId: user.userId }, _sum: { points: true } }),
    ]);
    return {
      points,
      builtCount: Math.min(current, CITY_PROJECTS.length),
      current: currentCode,
      projects: CITY_PROJECTS.map((p, i) => {
        const start = i === 0 ? 0 : ends[i - 1]!;
        const status = i < current ? 'DONE' : i === current ? 'BUILDING' : 'LOCKED';
        return {
          code: p.code,
          nameKey: p.nameKey,
          nameTh: p.nameTh,
          effectTh: p.effectTh,
          cost: p.cost,
          zone: p.zone,
          x: p.x,
          y: p.y,
          status,
          progress: status === 'DONE' ? p.cost : status === 'BUILDING' ? points - start : 0,
          completedAt: status === 'DONE' ? (completedAt.get(p.code)?.toISOString() ?? null) : null,
        };
      }),
      me: { points: mine?.points ?? 0, total: myTotal._sum.points ?? 0 },
      top: currentCode ? await this.topBuilders(user, currentCode) : [],
    };
  }

  /** Same rules as the festival helpers: same age band, no blocked players, no names for under-13s. */
  private async topBuilders(user: AuthUser, projectCode: string) {
    const blocked = await this.graph.blockedEitherWay(user.userId);
    const rows = await this.prisma.cityBuilderContribution.findMany({
      where: { projectCode, ageBand: user.ageBand, points: { gt: 0 } },
      orderBy: [{ points: 'desc' }, { createdAt: 'asc' }],
      take: TOP_BUILDERS + blocked.size,
      select: { userId: true, points: true, user: { select: { profile: { select: { displayName: true } } } } },
    });
    const hideNames = user.ageBand === 'UNDER_13';
    return rows
      .filter((r) => r.userId === user.userId || !blocked.has(r.userId))
      .slice(0, TOP_BUILDERS)
      .map((r, i) => ({
        rank: i + 1,
        displayName: r.userId === user.userId || !hideNames ? (r.user.profile?.displayName ?? '') : null,
        points: r.points,
        isMe: r.userId === user.userId,
      }));
  }
}
