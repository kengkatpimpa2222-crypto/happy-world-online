import { Body, Controller, Get, HttpCode, Patch, Post } from '@nestjs/common';
import { ApiBearerAuth, ApiHeader, ApiTags } from '@nestjs/swagger';
import {
  RestockRequest,
  restockSchema,
  SetPriceRequest,
  setPriceSchema,
  UpgradeBusinessRequest,
  upgradeBusinessSchema,
} from '@hwo/shared-contracts';
import { ApiZodBody } from '../common/api-zod';
import { AuthUser, CurrentUser, IdempotencyKey, UuidParam } from '../common/decorators';
import { ZodValidationPipe } from '../common/zod-validation.pipe';
import { BusinessService } from './business.service';

@ApiTags('businesses')
@ApiBearerAuth()
@Controller('businesses')
export class BusinessController {
  constructor(private readonly business: BusinessService) {}

  @Get()
  list(@CurrentUser() user: AuthUser) {
    return this.business.list(user.userId);
  }

  @Get(':id')
  get(@CurrentUser() user: AuthUser, @UuidParam('id') id: string) {
    return this.business.get(user.userId, id);
  }

  @Patch(':id/pricing')
  @ApiZodBody(setPriceSchema)
  setPrice(@CurrentUser() user: AuthUser, @UuidParam('id') id: string, @Body(new ZodValidationPipe(setPriceSchema)) dto: SetPriceRequest) {
    return this.business.setPrice(user.userId, id, dto);
  }

  @Post(':id/restock')
  @HttpCode(200)
  @ApiHeader({ name: 'Idempotency-Key', required: true })
  @ApiZodBody(restockSchema)
  restock(
    @CurrentUser() user: AuthUser,
    @UuidParam('id') id: string,
    @IdempotencyKey() key: string,
    @Body(new ZodValidationPipe(restockSchema)) dto: RestockRequest,
  ) {
    return this.business.restock(user.userId, id, dto, key);
  }

  @Post(':id/sell')
  @HttpCode(200)
  sell(@CurrentUser() user: AuthUser, @UuidParam('id') id: string) {
    return this.business.sell(user.userId, id);
  }

  @Post(':id/upgrade')
  @HttpCode(200)
  @ApiZodBody(upgradeBusinessSchema)
  upgrade(@CurrentUser() user: AuthUser, @UuidParam('id') id: string, @Body(new ZodValidationPipe(upgradeBusinessSchema)) dto: UpgradeBusinessRequest) {
    return this.business.upgrade(user.userId, id, dto.expectedLevel);
  }
}
