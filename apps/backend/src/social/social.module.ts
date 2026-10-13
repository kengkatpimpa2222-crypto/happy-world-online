import { Module } from '@nestjs/common';
import { QuestsModule } from '../quests/quests.module';
import { RealtimeModule } from '../realtime/realtime.module';
import { BlocksService } from './blocks.service';
import { FriendsService } from './friends.service';
import { ModerationService } from './moderation.service';
import { FriendsController, ModerationController, PlayersController } from './social.controller';

@Module({
  imports: [QuestsModule, RealtimeModule],
  controllers: [FriendsController, PlayersController, ModerationController],
  providers: [FriendsService, BlocksService, ModerationService],
})
export class SocialModule {}
