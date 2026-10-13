import { Injectable } from '@nestjs/common';
import { JwtService } from '@nestjs/jwt';
import { AppException } from '../common/app-exception';
import { AuthUser } from '../common/decorators';
import { PrismaService } from '../prisma/prisma.service';

interface AccessPayload {
  sub: string;
  sid: string;
}

/**
 * Turns an access token into the authenticated player. Shared by the HTTP guard and the
 * WebSocket handshake so both enforce the same rules (signature, live session, active account).
 */
@Injectable()
export class SessionVerifier {
  constructor(
    private readonly jwt: JwtService,
    private readonly prisma: PrismaService,
  ) {}

  async verify(token: string | undefined): Promise<AuthUser> {
    if (!token) throw AppException.unauthorized();
    let payload: AccessPayload;
    try {
      payload = await this.jwt.verifyAsync<AccessPayload>(token, { algorithms: ['HS256'] });
    } catch {
      throw AppException.unauthorized(undefined, 'Invalid or expired access token');
    }

    const session = await this.prisma.gameSession.findUnique({
      where: { id: payload.sid },
      select: { userId: true, revokedAt: true, expiresAt: true, user: { select: { status: true, ageBand: true } } },
    });
    if (
      !session ||
      session.userId !== payload.sub ||
      session.revokedAt ||
      session.expiresAt < new Date() ||
      session.user.status !== 'ACTIVE'
    ) {
      throw AppException.unauthorized(undefined, 'Session is no longer valid');
    }
    return { userId: payload.sub, sessionId: payload.sid, ageBand: session.user.ageBand };
  }
}
