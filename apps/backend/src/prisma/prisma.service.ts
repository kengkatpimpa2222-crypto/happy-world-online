import { Injectable, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
import { Prisma, PrismaClient } from '@prisma/client';

export type Tx = Prisma.TransactionClient;

const RETRYABLE = new Set(['P2002', 'P2034']);

@Injectable()
export class PrismaService extends PrismaClient implements OnModuleInit, OnModuleDestroy {
  async onModuleInit(): Promise<void> {
    await this.$connect();
  }

  async onModuleDestroy(): Promise<void> {
    await this.$disconnect();
  }

  /**
   * Runs `fn` in a database transaction. Retries when a concurrent request won a unique-key
   * race (P2002) or a serialization conflict (P2034); the retry then sees the committed row
   * and takes the idempotent "already done" path.
   */
  async tx<T>(fn: (tx: Tx) => Promise<T>, attempts = 3): Promise<T> {
    for (let i = 1; ; i++) {
      try {
        return await this.$transaction(fn, { timeout: 10_000, maxWait: 5_000 });
      } catch (err) {
        const retryable = err instanceof Prisma.PrismaClientKnownRequestError && RETRYABLE.has(err.code);
        if (!retryable || i >= attempts) throw err;
      }
    }
  }
}
