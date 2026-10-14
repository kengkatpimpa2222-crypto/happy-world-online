import { Module } from '@nestjs/common';
import { APP_GUARD } from '@nestjs/core';
import { ThrottlerGuard, ThrottlerModule } from '@nestjs/throttler';
import { AuditModule } from './audit/audit.module';
import { AuthModule } from './auth/auth.module';
import { BusinessModule } from './business/business.module';
import { FestivalModule } from './festival/festival.module';
import { AppConfig } from './config/app-config.service';
import { AppConfigModule } from './config/config.module';
import { HealthController } from './health/health.controller';
import { PlayerModule } from './player/player.module';
import { PresenceModule } from './presence/presence.module';
import { PrismaModule } from './prisma/prisma.module';
import { PurchasesModule } from './purchases/purchases.module';
import { QuestsModule } from './quests/quests.module';
import { RealtimeModule } from './realtime/realtime.module';
import { RedisThrottlerStorage } from './redis/redis-throttler.storage';
import { RedisModule } from './redis/redis.module';
import { RedisService } from './redis/redis.service';
import { SocialGraphModule } from './social/social-graph.module';
import { SocialModule } from './social/social.module';
import { StoreModule } from './store/store.module';
import { WalletModule } from './wallet/wallet.module';

@Module({
  imports: [
    AppConfigModule,
    PrismaModule,
    RedisModule,
    AuditModule,
    WalletModule,
    PresenceModule,
    SocialGraphModule,
    ThrottlerModule.forRootAsync({
      inject: [AppConfig, RedisService],
      useFactory: (config: AppConfig, redis: RedisService) => ({
        throttlers: [{ name: 'default', ttl: 60_000, limit: config.get('RATE_LIMIT_DEFAULT_PER_MINUTE') }],
        storage: redis.client ? new RedisThrottlerStorage(redis.client) : undefined,
      }),
    }),
    AuthModule,
    PlayerModule,
    QuestsModule,
    StoreModule,
    PurchasesModule,
    BusinessModule,
    FestivalModule,
    RealtimeModule,
    SocialModule,
  ],
  controllers: [HealthController],
  providers: [{ provide: APP_GUARD, useClass: ThrottlerGuard }],
})
export class AppModule {}
