import { Module } from '@nestjs/common';
import { DailyRewardsController } from './daily-rewards.controller';
import { DailyRewardsService } from './daily-rewards.service';
import { QuestsController } from './quests.controller';
import { QuestsService } from './quests.service';

@Module({
  controllers: [QuestsController, DailyRewardsController],
  providers: [QuestsService, DailyRewardsService],
  exports: [QuestsService, DailyRewardsService],
})
export class QuestsModule {}
