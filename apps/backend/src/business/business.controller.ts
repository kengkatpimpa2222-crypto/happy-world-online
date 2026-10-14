import { Body, Controller, Get, HttpCode, Patch, Post, Put } from '@nestjs/common';
import { ApiBearerAuth, ApiHeader, ApiOperation, ApiTags } from '@nestjs/swagger';
import { ShopStatus } from '@prisma/client';
import {
  AckShopReportRequest,
  ackShopReportSchema,
  RenameBusinessRequest,
  renameBusinessSchema,
  SetShelfRequest,
  setShelfSchema,
  SetShopStatusRequest,
  setShopStatusSchema,
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
import { LivingShopService } from './living-shop.service';

@ApiTags('businesses')
@ApiBearerAuth()
@Controller('businesses')
export class BusinessController {
  constructor(
    private readonly business: BusinessService,
    private readonly shop: LivingShopService,
  ) {}

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

  /** Legacy one-tap sale round (Phase 2). Kept for compatibility; the game now uses the market stall. */
  @Post(':id/sell')
  @HttpCode(200)
  @ApiOperation({ deprecated: true, summary: 'Legacy instant sale round; use the market stall (status/shelf) instead' })
  sell(@CurrentUser() user: AuthUser, @UuidParam('id') id: string) {
    return this.business.sell(user.userId, id);
  }

  @Post(':id/upgrade')
  @HttpCode(200)
  @ApiZodBody(upgradeBusinessSchema)
  upgrade(@CurrentUser() user: AuthUser, @UuidParam('id') id: string, @Body(new ZodValidationPipe(upgradeBusinessSchema)) dto: UpgradeBusinessRequest) {
    return this.business.upgrade(user.userId, id, dto.expectedLevel);
  }

  // ---------- living shop (Phase 5) ----------

  @Patch(':id')
  @ApiZodBody(renameBusinessSchema)
  async rename(@CurrentUser() user: AuthUser, @UuidParam('id') id: string, @Body(new ZodValidationPipe(renameBusinessSchema)) dto: RenameBusinessRequest) {
    await this.shop.rename(user.userId, id, dto.name);
    return this.business.get(user.userId, id);
  }

  /** How many units of a product stand on the market stall (moved from/to the shop's storage). */
  @Put(':id/shelf')
  @ApiZodBody(setShelfSchema)
  async setShelf(@CurrentUser() user: AuthUser, @UuidParam('id') id: string, @Body(new ZodValidationPipe(setShelfSchema)) dto: SetShelfRequest) {
    await this.shop.setShelf(user.userId, id, dto);
    return this.business.get(user.userId, id);
  }

  @Post(':id/status')
  @HttpCode(200)
  @ApiZodBody(setShopStatusSchema)
  async setStatus(@CurrentUser() user: AuthUser, @UuidParam('id') id: string, @Body(new ZodValidationPipe(setShopStatusSchema)) dto: SetShopStatusRequest) {
    await this.shop.setStatus(user, id, dto.status as ShopStatus);
    return this.business.get(user.userId, id);
  }

  /** Moves the Gold NPC customers paid into the wallet. */
  @Post(':id/collect')
  @HttpCode(200)
  @ApiHeader({ name: 'Idempotency-Key', required: true })
  async collect(@CurrentUser() user: AuthUser, @UuidParam('id') id: string, @IdempotencyKey() key: string) {
    const res = await this.shop.collect(user.userId, id, key);
    return { ...res, business: await this.business.get(user.userId, id) };
  }

  /** "While you were away": customers, sales, profit and tips since the last acknowledged report. */
  @Get(':id/report')
  report(@CurrentUser() user: AuthUser, @UuidParam('id') id: string) {
    return this.shop.report(user.userId, id);
  }

  @Post(':id/report/ack')
  @HttpCode(204)
  @ApiZodBody(ackShopReportSchema)
  async ackReport(@CurrentUser() user: AuthUser, @UuidParam('id') id: string, @Body(new ZodValidationPipe(ackShopReportSchema)) dto: AckShopReportRequest) {
    await this.shop.ackReport(user.userId, id, dto?.until);
  }
}
