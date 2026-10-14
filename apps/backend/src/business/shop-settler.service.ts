import { Injectable, Logger, OnApplicationBootstrap, OnApplicationShutdown } from '@nestjs/common';
import { AppConfig } from '../config/app-config.service';
import { LivingShopService } from './living-shop.service';

/**
 * Settles open market stalls in the background so players standing in the market see NPC
 * customers arrive, and owners get live cash-box updates. Correctness never depends on it:
 * every read and every owner action settles on demand too. Safe with several backend
 * instances (row locks with SKIP LOCKED + `settled_tick` only moves forward).
 */
@Injectable()
export class ShopSettlerService implements OnApplicationBootstrap, OnApplicationShutdown {
  private readonly logger = new Logger('ShopSettler');
  private timer: NodeJS.Timeout | null = null;
  private running = false;

  constructor(
    private readonly shop: LivingShopService,
    private readonly config: AppConfig,
  ) {}

  onApplicationBootstrap(): void {
    const seconds = this.config.get('LIVING_SHOP_SETTLE_INTERVAL_SECONDS');
    if (seconds <= 0) return;
    this.timer = setInterval(() => void this.runOnce(), seconds * 1000);
    this.timer.unref();
  }

  onApplicationShutdown(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
  }

  /** One pass over due shops; overlapping passes are skipped. */
  async runOnce(): Promise<number> {
    if (this.running) return 0;
    this.running = true;
    try {
      let total = 0;
      for (let batch = 0; batch < 10; batch++) {
        const n = await this.shop.settleDue(100);
        total += n;
        if (n < 100) break;
      }
      return total;
    } catch (e) {
      this.logger.warn(`Settler pass failed: ${e instanceof Error ? e.message : String(e)}`);
      return 0;
    } finally {
      this.running = false;
    }
  }
}
