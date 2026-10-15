import { Body, Controller, Get, HttpCode, Patch, Post, Put, Query } from '@nestjs/common';
import { ApiBearerAuth, ApiHeader, ApiOperation, ApiQuery, ApiTags } from '@nestjs/swagger';
import { Throttle } from '@nestjs/throttler';
import {
  homeDistrictQuerySchema,
  SaveHomeLayoutRequest,
  saveHomeLayoutSchema,
  UpdateHomeSettingsRequest,
  updateHomeSettingsSchema,
} from '@hwo/shared-contracts';
import { z } from 'zod';
import { ApiZodBody } from '../common/api-zod';
import { AuthUser, CurrentUser, IdempotencyKey, UuidParam } from '../common/decorators';
import { ZodValidationPipe } from '../common/zod-validation.pipe';
import { HomeService } from './home.service';

const MINUTE = 60_000;
const envInt = (name: string, fallback: number) => () => Number(process.env[name] ?? fallback);

@ApiTags('homes')
@ApiBearerAuth()
@Controller('homes')
export class HomeController {
  constructor(private readonly homes: HomeService) {}

  @Get('me')
  @ApiOperation({ summary: 'My home: plot, visit setting, room size and the furniture standing in it' })
  mine(@CurrentUser() user: AuthUser) {
    return this.homes.mine(user.userId);
  }

  @Post('me/claim')
  @HttpCode(200)
  @Throttle({ default: { limit: envInt('RATE_LIMIT_HOME_WRITES_PER_MINUTE', 30), ttl: MINUTE } })
  @ApiOperation({ summary: 'Claim the starter home: the first free plot of my district plus welcome furniture (once only)' })
  claim(@CurrentUser() user: AuthUser) {
    return this.homes.claim(user.userId);
  }

  @Get('me/furniture')
  @ApiOperation({ summary: 'Furniture I own: how many are in my bag and how many stand in my home, with their size' })
  furniture(@CurrentUser() user: AuthUser) {
    return this.homes.furniture(user.userId);
  }

  @Put('me/layout')
  @HttpCode(200)
  @Throttle({ default: { limit: envInt('RATE_LIMIT_HOME_WRITES_PER_MINUTE', 30), ttl: MINUTE } })
  @ApiHeader({ name: 'Idempotency-Key', required: true })
  @ApiOperation({ summary: 'Save the whole layout: place (no id), move/rotate (with id), pieces left out go back to the bag' })
  @ApiZodBody(saveHomeLayoutSchema)
  saveLayout(@CurrentUser() user: AuthUser, @IdempotencyKey() key: string, @Body(new ZodValidationPipe(saveHomeLayoutSchema)) dto: SaveHomeLayoutRequest) {
    return this.homes.saveLayout(user.userId, dto, key);
  }

  @Patch('me/settings')
  @Throttle({ default: { limit: envInt('RATE_LIMIT_HOME_WRITES_PER_MINUTE', 30), ttl: MINUTE } })
  @ApiOperation({ summary: 'Who may visit: PRIVATE, FRIENDS or PUBLIC (same age band only, never blocked players)' })
  @ApiZodBody(updateHomeSettingsSchema)
  settings(@CurrentUser() user: AuthUser, @Body(new ZodValidationPipe(updateHomeSettingsSchema)) dto: UpdateHomeSettingsRequest) {
    return this.homes.updateSettings(user.userId, dto.visitPolicy);
  }

  @Get('district')
  @ApiQuery({ name: 'street', required: false, type: Number })
  @ApiOperation({ summary: 'One street of my age band district: plots, owners and whether I may visit' })
  district(@CurrentUser() user: AuthUser, @Query(new ZodValidationPipe(homeDistrictQuerySchema)) q: z.infer<typeof homeDistrictQuerySchema>) {
    return this.homes.district(user, q.street);
  }

  @Get(':ownerId')
  @Throttle({ default: { limit: envInt('RATE_LIMIT_HOME_VISITS_PER_MINUTE', 60), ttl: MINUTE } })
  @ApiOperation({ summary: "Visit a player's home (checked on the server: age band, blocks, visit setting, friendship)" })
  visit(@CurrentUser() user: AuthUser, @UuidParam('ownerId') ownerId: string) {
    return this.homes.visit(user, ownerId);
  }
}
