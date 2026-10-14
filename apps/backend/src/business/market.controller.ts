import { Controller, Get } from '@nestjs/common';
import { ApiBearerAuth, ApiTags } from '@nestjs/swagger';
import { AuthUser, CurrentUser } from '../common/decorators';
import { LivingShopService } from './living-shop.service';

@ApiTags('market')
@ApiBearerAuth()
@Controller('market')
export class MarketController {
  constructor(private readonly shop: LivingShopService) {}

  /** Market stalls this player may see (same age band, no blocked players) and where their own stall is. */
  @Get()
  stalls(@CurrentUser() user: AuthUser) {
    return this.shop.market(user);
  }
}
