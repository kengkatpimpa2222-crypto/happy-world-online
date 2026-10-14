import { Body, Controller, Get, HttpCode, Post } from '@nestjs/common';
import { ApiBearerAuth, ApiOperation, ApiTags } from '@nestjs/swagger';
import { BusinessProductCode, FinishMinigameRequest, finishMinigameSchema, StartMinigameRequest, startMinigameSchema } from '@hwo/shared-contracts';
import { ApiZodBody } from '../common/api-zod';
import { AuthUser, CurrentUser, UuidParam } from '../common/decorators';
import { ZodValidationPipe } from '../common/zod-validation.pipe';
import { MinigameService } from './minigame.service';

@ApiTags('minigames')
@ApiBearerAuth()
@Controller('minigames')
export class MinigameController {
  constructor(private readonly minigames: MinigameService) {}

  @Get()
  @ApiOperation({ summary: "Mini-game rules, today's remaining rounds that make goods, and my best scores" })
  overview(@CurrentUser() user: AuthUser) {
    return this.minigames.overview(user.userId);
  }

  @Post('start')
  @HttpCode(200)
  @ApiOperation({ summary: 'Start a round that makes a product for my shop; returns the seed the round is built from' })
  @ApiZodBody(startMinigameSchema)
  start(@CurrentUser() user: AuthUser, @Body(new ZodValidationPipe(startMinigameSchema)) dto: StartMinigameRequest) {
    return this.minigames.start(user.userId, dto.businessId, dto.productCode as BusinessProductCode);
  }

  @Post(':id/finish')
  @HttpCode(200)
  @ApiOperation({ summary: 'Report the score; the server decides stars and adds the goods (repeat calls return the same result)' })
  @ApiZodBody(finishMinigameSchema)
  finish(@CurrentUser() user: AuthUser, @UuidParam('id') id: string, @Body(new ZodValidationPipe(finishMinigameSchema)) dto: FinishMinigameRequest) {
    return this.minigames.finish(user.userId, id, dto.score);
  }
}
