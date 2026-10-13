import { Body, Controller, HttpCode, Post, Req } from '@nestjs/common';
import { ApiBearerAuth, ApiTags } from '@nestjs/swagger';
import { Throttle } from '@nestjs/throttler';
import {
  GuestAuthRequest,
  guestAuthSchema,
  LinkAccountRequest,
  linkAccountSchema,
  LoginRequest,
  loginSchema,
  RefreshRequest,
  refreshSchema,
} from '@hwo/shared-contracts';
import type { Request } from 'express';
import { ApiZodBody } from '../common/api-zod';
import { sha256 } from '../common/crypto';
import { AuthUser, CurrentUser, Public } from '../common/decorators';
import { ZodValidationPipe } from '../common/zod-validation.pipe';
import { AuthService, ClientMeta } from './auth.service';

const envInt = (name: string, fallback: number) => () => Number(process.env[name] ?? fallback);
const HOUR = 3_600_000;
const QUARTER_HOUR = 900_000;

function meta(req: Request): ClientMeta {
  return { userAgent: req.headers['user-agent'], ipHash: req.ip ? sha256(req.ip) : undefined };
}

@ApiTags('auth')
@Controller('auth')
export class AuthController {
  constructor(private readonly auth: AuthService) {}

  @Public()
  @Throttle({ default: { limit: envInt('RATE_LIMIT_GUEST_PER_HOUR', 10), ttl: HOUR } })
  @Post('guest')
  @ApiZodBody(guestAuthSchema)
  guest(@Body(new ZodValidationPipe(guestAuthSchema)) dto: GuestAuthRequest, @Req() req: Request) {
    return this.auth.createGuest(dto, meta(req));
  }

  @ApiBearerAuth()
  @Throttle({ default: { limit: envInt('RATE_LIMIT_AUTH_PER_15_MIN', 20), ttl: QUARTER_HOUR } })
  @Post('link-account')
  @HttpCode(200)
  @ApiZodBody(linkAccountSchema)
  link(@CurrentUser() user: AuthUser, @Body(new ZodValidationPipe(linkAccountSchema)) dto: LinkAccountRequest, @Req() req: Request) {
    return this.auth.linkAccount(user.userId, dto, meta(req));
  }

  @Public()
  @Throttle({ default: { limit: envInt('RATE_LIMIT_AUTH_PER_15_MIN', 20), ttl: QUARTER_HOUR } })
  @Post('login')
  @HttpCode(200)
  @ApiZodBody(loginSchema)
  login(@Body(new ZodValidationPipe(loginSchema)) dto: LoginRequest, @Req() req: Request) {
    return this.auth.login(dto, meta(req));
  }

  @Public()
  @Post('refresh')
  @HttpCode(200)
  @ApiZodBody(refreshSchema)
  refresh(@Body(new ZodValidationPipe(refreshSchema)) dto: RefreshRequest, @Req() req: Request) {
    return this.auth.refresh(dto.refreshToken, meta(req));
  }

  @ApiBearerAuth()
  @Post('logout')
  @HttpCode(204)
  async logout(@CurrentUser() user: AuthUser): Promise<void> {
    await this.auth.logout(user.sessionId);
  }
}
