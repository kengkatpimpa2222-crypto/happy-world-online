import { Module } from '@nestjs/common';
import { QuestsModule } from '../quests/quests.module';
import { NewPlayerService } from './new-player.service';
import { PlayerController } from './player.controller';
import { PlayerService } from './player.service';

@Module({
  imports: [QuestsModule],
  controllers: [PlayerController],
  providers: [PlayerService, NewPlayerService],
  exports: [NewPlayerService],
})
export class PlayerModule {}
