import { Module } from '@nestjs/common';
import { QuestsModule } from '../quests/quests.module';
import { RealtimeModule } from '../realtime/realtime.module';
import { HomeController } from './home.controller';
import { HomeService } from './home.service';

/** Phase 10: the first home and the residential district. Access rules live in the global HomeAccessModule. */
@Module({
  imports: [QuestsModule, RealtimeModule],
  controllers: [HomeController],
  providers: [HomeService],
})
export class HomeModule {}
