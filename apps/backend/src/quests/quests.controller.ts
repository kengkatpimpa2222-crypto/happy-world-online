import { Controller, Get, HttpCode, Post } from '@nestjs/common';
import { ApiBearerAuth, ApiTags } from '@nestjs/swagger';
import { AuthUser, CurrentUser, QuestIdParam } from '../common/decorators';
import { QuestsService } from './quests.service';

@ApiTags('quests')
@ApiBearerAuth()
@Controller('quests')
export class QuestsController {
  constructor(private readonly quests: QuestsService) {}

  @Get()
  list(@CurrentUser() user: AuthUser) {
    return this.quests.list(user.userId);
  }

  @Post(':questId/start')
  start(@CurrentUser() user: AuthUser, @QuestIdParam('questId') questId: string) {
    return this.quests.start(user.userId, questId);
  }

  @Post(':questId/claim')
  @HttpCode(200)
  claim(@CurrentUser() user: AuthUser, @QuestIdParam('questId') questId: string) {
    return this.quests.claim(user.userId, questId);
  }
}
