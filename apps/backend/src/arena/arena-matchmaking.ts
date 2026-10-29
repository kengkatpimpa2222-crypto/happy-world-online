import { ARENA_TEAM_SIZE, ArenaTeam } from '@hwo/shared-contracts';

export interface QueuedPlayer {
  userId: string;
  ageBand: string;
  rating: number;
  joinedAt: number;
  /** players blocked either way: never put in the same match */
  blocked: ReadonlySet<string>;
}

export interface PlannedMatch {
  ageBand: string;
  teams: Record<ArenaTeam, QueuedPlayer[]>;
  /** bots needed per team to reach ARENA_TEAM_SIZE */
  bots: Record<ArenaTeam, number>;
}

const conflicts = (a: QueuedPlayer, b: QueuedPlayer) => a.blocked.has(b.userId) || b.blocked.has(a.userId);

/**
 * Builds as many matches as the queue allows. Within one age band, the longest-waiting player starts a group
 * and takes the next players in queue order who haven't blocked anyone in it. A full group (2 × team size)
 * plays at once; a smaller group plays with bots once its first player has waited `botFillMs`.
 * Teams are dealt in snake order by rating (1-2-2-1...), so two strong players don't end up together and two
 * lone humans always face each other.
 */
export function planMatches(queue: readonly QueuedPlayer[], now: number, botFillMs: number): PlannedMatch[] {
  const full = ARENA_TEAM_SIZE * 2;
  const taken = new Set<string>();
  const out: PlannedMatch[] = [];
  const ordered = [...queue].sort((a, b) => a.joinedAt - b.joinedAt);
  for (const first of ordered) {
    if (taken.has(first.userId)) continue;
    const group = [first];
    for (const p of ordered) {
      if (group.length >= full) break;
      if (p === first || taken.has(p.userId) || p.ageBand !== first.ageBand) continue;
      if (group.some((g) => conflicts(g, p))) continue;
      group.push(p);
    }
    if (group.length < full && now - first.joinedAt < botFillMs) continue;
    group.forEach((p) => taken.add(p.userId));
    out.push(dealTeams(first.ageBand, group));
  }
  return out;
}

function dealTeams(ageBand: string, group: QueuedPlayer[]): PlannedMatch {
  const teams: Record<ArenaTeam, QueuedPlayer[]> = { RED: [], BLUE: [] };
  const byRating = [...group].sort((a, b) => b.rating - a.rating);
  byRating.forEach((p, i) => teams[i % 4 === 0 || i % 4 === 3 ? 'RED' : 'BLUE'].push(p));
  return {
    ageBand,
    teams,
    bots: { RED: ARENA_TEAM_SIZE - teams.RED.length, BLUE: ARENA_TEAM_SIZE - teams.BLUE.length },
  };
}
