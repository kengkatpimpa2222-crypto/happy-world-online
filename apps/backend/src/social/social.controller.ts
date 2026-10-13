import { Body, Controller, Delete, Get, HttpCode, Post } from '@nestjs/common';
import { ApiBearerAuth, ApiTags } from '@nestjs/swagger';
import { Throttle } from '@nestjs/throttler';
import { CreateReportRequest, createReportSchema, SendFriendRequest, sendFriendRequestSchema } from '@hwo/shared-contracts';
import { ApiZodBody } from '../common/api-zod';
import { AuthUser, CurrentUser, UuidParam } from '../common/decorators';
import { ZodValidationPipe } from '../common/zod-validation.pipe';
import { BlocksService } from './blocks.service';
import { FriendsService } from './friends.service';
import { ModerationService } from './moderation.service';

const HOUR = 3_600_000;
const envInt = (name: string, fallback: number) => () => Number(process.env[name] ?? fallback);

@ApiTags('friends')
@ApiBearerAuth()
@Controller('friends')
export class FriendsController {
  constructor(private readonly friends: FriendsService) {}

  @Post('requests')
  @Throttle({ default: { limit: envInt('RATE_LIMIT_FRIEND_REQUESTS_PER_HOUR', 30), ttl: HOUR } })
  @ApiZodBody(sendFriendRequestSchema)
  send(@CurrentUser() user: AuthUser, @Body(new ZodValidationPipe(sendFriendRequestSchema)) dto: SendFriendRequest) {
    return this.friends.sendRequest(user, dto);
  }

  @Get('requests/incoming')
  incoming(@CurrentUser() user: AuthUser) {
    return this.friends.incoming(user.userId);
  }

  @Get('requests/outgoing')
  outgoing(@CurrentUser() user: AuthUser) {
    return this.friends.outgoing(user.userId);
  }

  @Post('requests/:requestId/accept')
  @HttpCode(200)
  accept(@CurrentUser() user: AuthUser, @UuidParam('requestId') requestId: string) {
    return this.friends.accept(user.userId, requestId);
  }

  @Post('requests/:requestId/decline')
  @HttpCode(200)
  decline(@CurrentUser() user: AuthUser, @UuidParam('requestId') requestId: string) {
    return this.friends.decline(user.userId, requestId);
  }

  @Delete('requests/:requestId')
  @HttpCode(204)
  async cancel(@CurrentUser() user: AuthUser, @UuidParam('requestId') requestId: string): Promise<void> {
    await this.friends.cancel(user.userId, requestId);
  }

  @Get()
  list(@CurrentUser() user: AuthUser) {
    return this.friends.list(user.userId);
  }

  @Delete(':friendId')
  @HttpCode(204)
  async remove(@CurrentUser() user: AuthUser, @UuidParam('friendId') friendId: string): Promise<void> {
    await this.friends.remove(user.userId, friendId);
  }
}

@ApiTags('players')
@ApiBearerAuth()
@Controller('players')
export class PlayersController {
  constructor(private readonly blocks: BlocksService) {}

  @Get('blocked')
  blocked(@CurrentUser() user: AuthUser) {
    return this.blocks.list(user.userId);
  }

  @Post(':playerId/block')
  @HttpCode(200)
  block(@CurrentUser() user: AuthUser, @UuidParam('playerId') playerId: string) {
    return this.blocks.block(user.userId, playerId);
  }

  @Delete(':playerId/block')
  @HttpCode(204)
  async unblock(@CurrentUser() user: AuthUser, @UuidParam('playerId') playerId: string): Promise<void> {
    await this.blocks.unblock(user.userId, playerId);
  }
}

@ApiTags('moderation')
@ApiBearerAuth()
@Controller('moderation')
export class ModerationController {
  constructor(private readonly moderation: ModerationService) {}

  @Post('reports')
  @Throttle({ default: { limit: envInt('RATE_LIMIT_REPORTS_PER_HOUR', 20), ttl: HOUR } })
  @ApiZodBody(createReportSchema)
  report(@CurrentUser() user: AuthUser, @Body(new ZodValidationPipe(createReportSchema)) dto: CreateReportRequest) {
    return this.moderation.report(user.userId, dto);
  }

  @Get('reports/me')
  mine(@CurrentUser() user: AuthUser) {
    return this.moderation.mine(user.userId);
  }
}
