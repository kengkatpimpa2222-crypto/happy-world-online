import { Injectable } from '@nestjs/common';
import { CITY_ZONES, ErrorCode, ZoneConfig, zoneById } from '@hwo/shared-contracts';
import { AppException } from '../common/app-exception';
import { AuthUser } from '../common/decorators';
import { PresenceEntry } from '../presence/presence.store';
import { PresenceService } from '../presence/presence.service';
import { SocialGraphService } from '../social/social-graph.service';

const MAX_SHARDS = 50;

export interface PublicPlayer {
  userId: string;
  displayName: string;
  level: number;
  avatar: unknown;
  x: number;
  y: number;
  dir: number;
  isFriend?: boolean;
}

/**
 * City rooms are split by zone AND age band (players only see their own age band),
 * and each split is sharded so a crowded zone never exceeds its capacity.
 */
@Injectable()
export class CityService {
  constructor(
    private readonly presence: PresenceService,
    private readonly graph: SocialGraphService,
  ) {}

  zones(): readonly ZoneConfig[] {
    return CITY_ZONES;
  }

  requireZone(id: string): ZoneConfig {
    const zone = zoneById(id);
    if (!zone) throw AppException.badRequest(ErrorCode.ZONE_NOT_FOUND, 'Unknown zone');
    return zone;
  }

  roomName(zone: string, ageBand: string, shard: number): string {
    return `zone:${zone}:${ageBand}:${shard}`;
  }

  /** First shard of this zone (for this age band) that still has space. */
  async pickRoom(zone: ZoneConfig, ageBand: string, currentRoom: string | null): Promise<string> {
    for (let shard = 0; shard < MAX_SHARDS; shard++) {
      const room = this.roomName(zone.id, ageBand, shard);
      if (room === currentRoom) return room;
      const count = (await this.presence.listRoom(room)).length;
      if (count < zone.capacity) return room;
    }
    throw AppException.conflict(ErrorCode.ZONE_NOT_FOUND, 'Zone is full, try again later');
  }

  toPublic(e: PresenceEntry, friends?: Set<string>): PublicPlayer {
    return {
      userId: e.userId,
      displayName: e.displayName,
      level: e.level,
      avatar: e.avatar,
      x: e.x,
      y: e.y,
      dir: e.dir,
      ...(friends ? { isFriend: friends.has(e.userId) } : {}),
    };
  }

  /** Players a viewer may see in a room: never themselves, never anyone blocked either way. */
  async visibleInRoom(viewerId: string, room: string, blocked: Set<string>, friends?: Set<string>): Promise<PublicPlayer[]> {
    const entries = await this.presence.listRoom(room);
    return entries.filter((e) => e.userId !== viewerId && !blocked.has(e.userId)).map((e) => this.toPublic(e, friends));
  }

  /** REST view of a zone: the viewer's own shard if they are in it, otherwise the first shard. */
  async playersForRest(user: AuthUser, zoneId: string | undefined) {
    const me = await this.presence.get(user.userId);
    const zone = this.requireZone(zoneId ?? me?.zone ?? 'PLAZA');
    const room = me?.zone === zone.id && me.room ? me.room : this.roomName(zone.id, user.ageBand, 0);
    const [blocked, friendIds] = await Promise.all([this.graph.blockedEitherWay(user.userId), this.graph.friendIds(user.userId)]);
    const players = await this.visibleInRoom(user.userId, room, blocked, new Set(friendIds));
    return { zone: zone.id, inZone: me?.room === room, players };
  }
}
