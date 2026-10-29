import { Module } from '@nestjs/common';
import { ArenaController } from './arena.controller';
import { ArenaGateway } from './arena.gateway';
import { ArenaService } from './arena.service';

/** Phase 13: Splash Arena, an opt-in team mode with seasonal ranks (realtime on the city namespace). */
@Module({
  controllers: [ArenaController],
  providers: [ArenaService, ArenaGateway],
})
export class ArenaModule {}
