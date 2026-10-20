import { Module } from '@nestjs/common';
import { AdventureModule } from '../adventure/adventure.module';
import { QuestsModule } from '../quests/quests.module';
import { JobsController } from './jobs.controller';
import { JobsService } from './jobs.service';

/** Phase 12: part-time jobs at the city's workplaces (Worker skill, wages through the ledgers). */
@Module({
  imports: [AdventureModule, QuestsModule],
  controllers: [JobsController],
  providers: [JobsService],
})
export class JobsModule {}
