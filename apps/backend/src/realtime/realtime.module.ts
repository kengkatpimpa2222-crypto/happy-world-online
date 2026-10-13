import { Module } from '@nestjs/common';
import { AuthModule } from '../auth/auth.module';
import { CityController } from './city.controller';
import { CityGateway } from './city.gateway';
import { CityService } from './city.service';
import { RealtimeService } from './realtime.service';

@Module({
  imports: [AuthModule],
  controllers: [CityController],
  providers: [CityService, CityGateway, RealtimeService],
  exports: [RealtimeService],
})
export class RealtimeModule {}
