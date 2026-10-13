import { Body, Controller, Get, Patch } from '@nestjs/common';
import { ApiBearerAuth, ApiTags } from '@nestjs/swagger';
import { UpdateProfileRequest, updateProfileSchema } from '@hwo/shared-contracts';
import { ApiZodBody } from '../common/api-zod';
import { AuthUser, CurrentUser } from '../common/decorators';
import { ZodValidationPipe } from '../common/zod-validation.pipe';
import { PlayerService } from './player.service';

/** All routes act on the player in the access token; there is deliberately no :playerId parameter. */
@ApiTags('player')
@ApiBearerAuth()
@Controller('player')
export class PlayerController {
  constructor(private readonly player: PlayerService) {}

  @Get('profile')
  profile(@CurrentUser() user: AuthUser) {
    return this.player.getProfile(user.userId);
  }

  @Patch('profile')
  @ApiZodBody(updateProfileSchema)
  update(@CurrentUser() user: AuthUser, @Body(new ZodValidationPipe(updateProfileSchema)) dto: UpdateProfileRequest) {
    return this.player.updateProfile(user.userId, dto);
  }

  @Get('inventory')
  inventory(@CurrentUser() user: AuthUser) {
    return this.player.getInventory(user.userId);
  }

  @Get('wallet')
  wallet(@CurrentUser() user: AuthUser) {
    return this.player.getWallet(user.userId);
  }
}
