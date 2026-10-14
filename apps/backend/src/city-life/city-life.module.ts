import { Module } from '@nestjs/common';
import { FestivalModule } from '../festival/festival.module';
import { RealtimeModule } from '../realtime/realtime.module';
import { CityLifeController } from './city-life.controller';
import { CityLifeService } from './city-life.service';

@Module({
  imports: [FestivalModule, RealtimeModule],
  controllers: [CityLifeController],
  providers: [CityLifeService],
  exports: [CityLifeService],
})
export class CityLifeModule {}
