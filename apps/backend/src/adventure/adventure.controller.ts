import { Body, Controller, Get, HttpCode, Post } from '@nestjs/common';
import { ApiBearerAuth, ApiHeader, ApiOperation, ApiTags } from '@nestjs/swagger';
import { CraftRequest, craftSchema, StartExpeditionRequest, startExpeditionSchema } from '@hwo/shared-contracts';
import { ApiZodBody } from '../common/api-zod';
import { AuthUser, CurrentUser, IdempotencyKey, UuidParam } from '../common/decorators';
import { ZodValidationPipe } from '../common/zod-validation.pipe';
import { CraftingService } from './crafting.service';
import { ExpeditionService } from './expedition.service';

@ApiTags('expeditions')
@ApiBearerAuth()
@Controller('expeditions')
export class ExpeditionController {
  constructor(private readonly expeditions: ExpeditionService) {}

  @Get()
  @ApiOperation({ summary: 'Places to explore, plans, my current trip, my Explorer/Crafter skills and materials' })
  overview(@CurrentUser() user: AuthUser) {
    return this.expeditions.overview(user.userId);
  }

  @Post('start')
  @HttpCode(200)
  @ApiOperation({ summary: 'Set off on a trip (one at a time); it keeps going while the app is closed' })
  @ApiZodBody(startExpeditionSchema)
  start(@CurrentUser() user: AuthUser, @Body(new ZodValidationPipe(startExpeditionSchema)) dto: StartExpeditionRequest) {
    return this.expeditions.start(user.userId, dto.site, dto.plan);
  }

  @Post(':id/claim')
  @HttpCode(200)
  @ApiOperation({ summary: 'Come home: the server rolls the story and adds the materials (repeat calls return the same result)' })
  claim(@CurrentUser() user: AuthUser, @UuidParam('id') id: string) {
    return this.expeditions.claim(user.userId, id);
  }
}

@ApiTags('crafting')
@ApiBearerAuth()
@Controller('crafting')
export class CraftingController {
  constructor(private readonly crafting: CraftingService) {}

  @Get()
  @ApiOperation({ summary: 'Recipes, what I can make with my materials, and my Crafter skill' })
  overview(@CurrentUser() user: AuthUser) {
    return this.crafting.overview(user.userId);
  }

  @Post('craft')
  @HttpCode(200)
  @ApiHeader({ name: 'Idempotency-Key', required: true })
  @ApiOperation({ summary: "Craft materials into goods in my shop's storage (pays a small Gold workshop fee)" })
  @ApiZodBody(craftSchema)
  craft(@CurrentUser() user: AuthUser, @IdempotencyKey() key: string, @Body(new ZodValidationPipe(craftSchema)) dto: CraftRequest) {
    return this.crafting.craft(user.userId, dto.businessId, dto.recipe, dto.times, key);
  }
}
