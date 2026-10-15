import { Module } from '@nestjs/common';
import { CityLifeModule } from '../city-life/city-life.module';
import { FestivalModule } from '../festival/festival.module';
import { QuestsModule } from '../quests/quests.module';
import { RealtimeModule } from '../realtime/realtime.module';
import { BusinessController } from './business.controller';
import { BusinessService } from './business.service';
import { LivingShopService } from './living-shop.service';
import { MarketController } from './market.controller';
import { MinigameController } from './minigame.controller';
import { MinigameService } from './minigame.service';
import { ShopSettlerService } from './shop-settler.service';

@Module({
  imports: [QuestsModule, RealtimeModule, FestivalModule, CityLifeModule],
  controllers: [BusinessController, MarketController, MinigameController],
  providers: [BusinessService, LivingShopService, ShopSettlerService, MinigameService],
  exports: [LivingShopService, ShopSettlerService, BusinessService],
})
export class BusinessModule {}
