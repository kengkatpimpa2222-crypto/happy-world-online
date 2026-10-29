import { Controller, Get, Query } from '@nestjs/common';
import { ApiBearerAuth, ApiOperation, ApiTags } from '@nestjs/swagger';
import { arenaLeaderboardQuerySchema } from '@hwo/shared-contracts';
import { AuthUser, CurrentUser } from '../common/decorators';
import { ZodValidationPipe } from '../common/zod-validation.pipe';
import { ArenaService } from './arena.service';

@ApiTags('arena')
@ApiBearerAuth()
@Controller('arena')
export class ArenaController {
  constructor(private readonly arena: ArenaService) {}

  @Get('me')
  @ApiOperation({ summary: "My Splash Arena rank this season: rating, tier, position, today's rewarded matches and my last matches" })
  me(@CurrentUser() user: AuthUser) {
    return this.arena.me(user);
  }

  @Get('leaderboard')
  @ApiOperation({ summary: "This season's top players of my age band" })
  leaderboard(@CurrentUser() user: AuthUser, @Query(new ZodValidationPipe(arenaLeaderboardQuerySchema)) q: { limit: number }) {
    return this.arena.leaderboard(user, q.limit);
  }
}
