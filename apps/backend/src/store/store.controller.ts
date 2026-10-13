import { Body, Controller, Get, Post } from '@nestjs/common';
import { ApiBearerAuth, ApiHeader, ApiTags } from '@nestjs/swagger';
import { StorePurchaseRequest, storePurchaseSchema } from '@hwo/shared-contracts';
import { ApiZodBody } from '../common/api-zod';
import { AuthUser, CurrentUser, IdempotencyKey } from '../common/decorators';
import { ZodValidationPipe } from '../common/zod-validation.pipe';
import { StoreService } from './store.service';

@ApiTags('store')
@ApiBearerAuth()
@Controller('store')
export class StoreController {
  constructor(private readonly store: StoreService) {}

  @Get('catalog')
  catalog(@CurrentUser() user: AuthUser) {
    return this.store.catalog(user.userId);
  }

  @Post('purchases')
  @ApiHeader({ name: 'Idempotency-Key', required: true, description: 'Unique per purchase attempt (8-64 chars)' })
  @ApiZodBody(storePurchaseSchema)
  purchase(
    @CurrentUser() user: AuthUser,
    @IdempotencyKey() key: string,
    @Body(new ZodValidationPipe(storePurchaseSchema)) dto: StorePurchaseRequest,
  ) {
    return this.store.purchase(user.userId, dto, key);
  }
}
