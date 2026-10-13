import { Body, Controller, HttpCode, Post } from '@nestjs/common';
import { ApiBearerAuth, ApiTags } from '@nestjs/swagger';
import { VerifyPurchaseRequest, verifyPurchaseSchema } from '@hwo/shared-contracts';
import { ApiZodBody } from '../common/api-zod';
import { AuthUser, CurrentUser } from '../common/decorators';
import { ZodValidationPipe } from '../common/zod-validation.pipe';
import { PurchasesService } from './purchases.service';

@ApiTags('purchases')
@ApiBearerAuth()
@Controller('purchases')
export class PurchasesController {
  constructor(private readonly purchases: PurchasesService) {}

  @Post('verify')
  @HttpCode(200)
  @ApiZodBody(verifyPurchaseSchema)
  verify(@CurrentUser() user: AuthUser, @Body(new ZodValidationPipe(verifyPurchaseSchema)) dto: VerifyPurchaseRequest) {
    return this.purchases.verify(user.userId, dto);
  }
}
