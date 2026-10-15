import { Module } from '@nestjs/common';
import { BusinessModule } from '../business/business.module';
import { QuestsModule } from '../quests/quests.module';
import { CraftingController, ExpeditionController } from './adventure.controller';
import { CraftingService } from './crafting.service';
import { ExpeditionService } from './expedition.service';
import { SkillsService } from './skills.service';

/** Phase 9: exploring for materials and crafting them into goods. */
@Module({
  imports: [BusinessModule, QuestsModule],
  controllers: [ExpeditionController, CraftingController],
  providers: [ExpeditionService, CraftingService, SkillsService],
})
export class AdventureModule {}
