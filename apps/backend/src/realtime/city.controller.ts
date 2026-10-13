import { Controller, Get, Query } from '@nestjs/common';
import { ApiBearerAuth, ApiQuery, ApiTags } from '@nestjs/swagger';
import { PRESET_MESSAGES, PRESET_MESSAGE_TEXT_TH, WS_NAMESPACE, WS_PATH, ZONE_IDS } from '@hwo/shared-contracts';
import { z } from 'zod';
import { AuthUser, CurrentUser } from '../common/decorators';
import { ZodValidationPipe } from '../common/zod-validation.pipe';
import { CityService } from './city.service';

const zoneQuery = z.enum(ZONE_IDS).optional();

@ApiTags('city')
@ApiBearerAuth()
@Controller('city')
export class CityController {
  constructor(private readonly city: CityService) {}

  /** Static city layout + how to connect to the realtime channel. */
  @Get()
  layout() {
    return {
      zones: this.city.zones(),
      presetMessages: PRESET_MESSAGES.map((id) => ({ id, textTh: PRESET_MESSAGE_TEXT_TH[id] })),
      realtime: { namespace: WS_NAMESPACE, path: WS_PATH, transport: 'socket.io', auth: 'handshake.auth.token = accessToken' },
    };
  }

  /** Online players in a zone that this player is allowed to see (same age band, no blocks). */
  @Get('players')
  @ApiQuery({ name: 'zone', required: false, enum: ZONE_IDS })
  players(@CurrentUser() user: AuthUser, @Query('zone', new ZodValidationPipe(zoneQuery)) zone?: string) {
    return this.city.playersForRest(user, zone);
  }
}
