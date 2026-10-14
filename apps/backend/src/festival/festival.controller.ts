import { Body, Controller, Get, HttpCode, Post } from '@nestjs/common';
import { ApiBearerAuth, ApiOperation, ApiTags } from '@nestjs/swagger';
import { ClaimFestivalRequest, claimFestivalSchema } from '@hwo/shared-contracts';
import { ApiZodBody } from '../common/api-zod';
import { AuthUser, CurrentUser } from '../common/decorators';
import { ZodValidationPipe } from '../common/zod-validation.pipe';
import { FestivalService } from './festival.service';

@ApiTags('festival')
@ApiBearerAuth()
@Controller('festival')
export class FestivalController {
  constructor(private readonly festival: FestivalService) {}

  @Get()
  @ApiOperation({ summary: "This week's city festival (upcoming or running), the city's progress, my share and rewards" })
  state(@CurrentUser() user: AuthUser) {
    return this.festival.state(user);
  }

  @Post('claim')
  @HttpCode(200)
  @ApiOperation({ summary: 'Claim the reward of a tier the city reached (once per tier; needs a minimum share)' })
  @ApiZodBody(claimFestivalSchema)
  claim(@CurrentUser() user: AuthUser, @Body(new ZodValidationPipe(claimFestivalSchema)) dto: ClaimFestivalRequest) {
    return this.festival.claim(user, dto.festivalKey, dto.tier);
  }
}
