import { Controller, Get } from '@nestjs/common';
import { ApiBearerAuth, ApiOperation, ApiTags } from '@nestjs/swagger';
import { AuthUser, CurrentUser } from '../common/decorators';
import { CityLifeService } from './city-life.service';

@ApiTags('city')
@ApiBearerAuth()
@Controller('city')
export class CityLifeController {
  constructor(private readonly city: CityLifeService) {}

  @Get('now')
  @ApiOperation({ summary: 'Bangkok time of day, season, weather (with a short forecast) and what sells well right now' })
  now() {
    return this.city.now();
  }

  @Get('growth')
  @ApiOperation({ summary: "My city's public projects: built, being built (with progress and top builders) and still locked" })
  growth(@CurrentUser() user: AuthUser) {
    return this.city.growth(user);
  }
}
