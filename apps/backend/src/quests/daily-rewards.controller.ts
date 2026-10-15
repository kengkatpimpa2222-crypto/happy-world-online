import { Body, Controller, Get, HttpCode, Post } from '@nestjs/common';
import { ApiBearerAuth, ApiOperation, ApiTags } from '@nestjs/swagger';
import { ClaimMissionRequest, claimMissionSchema } from '@hwo/shared-contracts';
import { ApiZodBody } from '../common/api-zod';
import { AuthUser, CurrentUser } from '../common/decorators';
import { ZodValidationPipe } from '../common/zod-validation.pipe';
import { DailyRewardsService } from './daily-rewards.service';

@ApiTags('daily')
@ApiBearerAuth()
@Controller('daily')
export class DailyRewardsController {
  constructor(private readonly daily: DailyRewardsService) {}

  @Get()
  @ApiOperation({ summary: "Today's check-in step on the 7-day track, today's 3 daily missions and this week's missions (server time, Bangkok)" })
  state(@CurrentUser() user: AuthUser) {
    return this.daily.state(user.userId);
  }

  @Post('checkin')
  @HttpCode(200)
  @ApiOperation({ summary: "Check in for today (once per Bangkok day). A missed day never resets the track." })
  checkin(@CurrentUser() user: AuthUser) {
    return this.daily.checkin(user.userId);
  }

  @Post('missions/claim')
  @HttpCode(200)
  @ApiOperation({ summary: 'Claim a finished daily or weekly mission (current or previous period, once)' })
  @ApiZodBody(claimMissionSchema)
  claimMission(@CurrentUser() user: AuthUser, @Body(new ZodValidationPipe(claimMissionSchema)) dto: ClaimMissionRequest) {
    return this.daily.claimMission(user.userId, dto.periodKey, dto.missionId);
  }
}
