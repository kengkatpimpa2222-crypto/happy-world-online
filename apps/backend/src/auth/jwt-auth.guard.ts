import { CanActivate, ExecutionContext, Injectable } from '@nestjs/common';
import { Reflector } from '@nestjs/core';
import type { Request } from 'express';
import { AppException } from '../common/app-exception';
import { AuthUser, IS_PUBLIC_KEY } from '../common/decorators';
import { SessionVerifier } from './session-verifier.service';

/**
 * Global guard: every route needs a valid access token unless marked @Public().
 * The session row is checked on each request so logout / revocation take effect immediately.
 */
@Injectable()
export class JwtAuthGuard implements CanActivate {
  constructor(
    private readonly reflector: Reflector,
    private readonly sessions: SessionVerifier,
  ) {}

  async canActivate(context: ExecutionContext): Promise<boolean> {
    const isPublic = this.reflector.getAllAndOverride<boolean>(IS_PUBLIC_KEY, [context.getHandler(), context.getClass()]);
    if (isPublic) return true;

    const req = context.switchToHttp().getRequest<Request & { user?: AuthUser }>();
    const header = req.headers.authorization;
    if (!header?.startsWith('Bearer ')) throw AppException.unauthorized();
    req.user = await this.sessions.verify(header.slice(7));
    return true;
  }
}
