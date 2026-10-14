import { Module } from '@nestjs/common';
import { QuestsModule } from '../quests/quests.module';
import { RealtimeModule } from '../realtime/realtime.module';
import { BusinessController } from './business.controller';
import { BusinessService } from './business.service';
import { LivingShopService } from './living-shop.service';
import { MarketController } from './market.controller';
import { ShopSettlerService } from './shop-settler.service';

@Module({
  imports: [QuestsModule, RealtimeModule],
  controllers: [BusinessController, MarketController],
  providers: [BusinessService, LivingShopService, ShopSettlerService],
  exports: [LivingShopService, ShopSettlerService],
})
export class BusinessModule {}
