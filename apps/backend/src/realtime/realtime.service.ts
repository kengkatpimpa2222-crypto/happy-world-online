import { Injectable, Logger } from '@nestjs/common';
import type { Namespace } from 'socket.io';
import { MAX_SHARDS, zoneRoomName } from './city.service';
import { CITY_ZONES, homeRoomName } from '@hwo/shared-contracts';

export const userRoom = (userId: string) => `user:${userId}`;
export const BLOCKS_CHANGED = 'hwo:blocks-changed';
export const HOME_ACCESS_CHANGED = 'hwo:home-access-changed';

/**
 * Lets REST services push events to connected players without depending on the gateway class.
 * Works across instances when the Redis adapter is active.
 */
@Injectable()
export class RealtimeService {
  private readonly logger = new Logger('RealtimeService');
  private ns: Namespace | null = null;
  private blocksChangedHandler: ((ids: string[]) => Promise<void>) | null = null;
  private homeAccessHandler: ((ownerIds: string[]) => Promise<void>) | null = null;

  attach(ns: Namespace, onBlocksChanged: (ids: string[]) => Promise<void>, onHomeAccessChanged?: (ownerIds: string[]) => Promise<void>): void {
    this.ns = ns;
    this.blocksChangedHandler = onBlocksChanged;
    this.homeAccessHandler = onHomeAccessChanged ?? null;
  }

  /** Phase 10: sends to everyone inside one player's home (owner and visitors), nobody else. */
  toHome(ownerId: string, event: string, payload: unknown): void {
    this.ns?.to(homeRoomName(ownerId)).emit(event, payload);
  }

  /**
   * Phase 10: these owners' visit rules may have changed (setting, unfriend): re-check every visitor in their
   * homes on every instance and walk out the ones who may no longer be there.
   */
  async homeAccessChanged(ownerIds: string[]): Promise<void> {
    if (!this.ns || !this.homeAccessHandler) return;
    await this.homeAccessHandler(ownerIds);
    try {
      this.ns.serverSideEmit(HOME_ACCESS_CHANGED, ownerIds);
    } catch {
      // single instance (in-memory adapter): nothing else to tell
    }
  }

  notifyUser(userId: string, event: string, payload: unknown): void {
    this.ns?.to(userRoom(userId)).emit(event, payload);
  }

  /** Sends to everyone in every shard of `zone` for one age band (other age bands never receive it). */
  toZone(zone: string, ageBand: string, event: string, payload: unknown): void {
    if (!this.ns) return;
    const rooms = Array.from({ length: MAX_SHARDS }, (_, shard) => zoneRoomName(zone, ageBand, shard));
    this.ns.to(rooms).emit(event, payload);
  }

  /** Sends to everyone in the city (every zone and shard) of one age band. */
  toCity(ageBand: string, event: string, payload: unknown): void {
    if (!this.ns) return;
    const rooms = CITY_ZONES.flatMap((z) => Array.from({ length: MAX_SHARDS }, (_, shard) => zoneRoomName(z.id, ageBand, shard)));
    this.ns.to(rooms).emit(event, payload);
  }

  /** Re-load block lists of both players' live connections on every instance. */
  async blocksChanged(a: string, b: string): Promise<void> {
    if (!this.ns || !this.blocksChangedHandler) return;
    await this.blocksChangedHandler([a, b]);
    try {
      this.ns.serverSideEmit(BLOCKS_CHANGED, [a, b]);
    } catch {
      // single instance (in-memory adapter): nothing else to tell
    }
  }

  disconnectUser(userId: string): void {
    this.ns?.in(userRoom(userId)).disconnectSockets(true);
    this.logger.debug(`Disconnected sockets of ${userId}`);
  }
}
