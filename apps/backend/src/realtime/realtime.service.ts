import { Injectable, Logger } from '@nestjs/common';
import type { Namespace } from 'socket.io';

export const userRoom = (userId: string) => `user:${userId}`;
export const BLOCKS_CHANGED = 'hwo:blocks-changed';

/**
 * Lets REST services push events to connected players without depending on the gateway class.
 * Works across instances when the Redis adapter is active.
 */
@Injectable()
export class RealtimeService {
  private readonly logger = new Logger('RealtimeService');
  private ns: Namespace | null = null;
  private blocksChangedHandler: ((ids: string[]) => Promise<void>) | null = null;

  attach(ns: Namespace, onBlocksChanged: (ids: string[]) => Promise<void>): void {
    this.ns = ns;
    this.blocksChangedHandler = onBlocksChanged;
  }

  notifyUser(userId: string, event: string, payload: unknown): void {
    this.ns?.to(userRoom(userId)).emit(event, payload);
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
