import { Module } from '@nestjs/common';
import { QuestsModule } from '../quests/quests.module';
import { RealtimeModule } from '../realtime/realtime.module';
import { FestivalController } from './festival.controller';
import { FestivalService } from './festival.service';

@Module({
  imports: [QuestsModule, RealtimeModule],
  controllers: [FestivalController],
  providers: [FestivalService],
  exports: [FestivalService],
})
export class FestivalModule {}
