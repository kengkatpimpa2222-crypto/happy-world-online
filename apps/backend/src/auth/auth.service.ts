import { HttpStatus, Injectable } from '@nestjs/common';
import { JwtService } from '@nestjs/jwt';
import { ErrorCode, GuestAuthRequest, LinkAccountRequest, LoginRequest } from '@hwo/shared-contracts';
import { AppException } from '../common/app-exception';
import { DUMMY_PASSWORD_HASH, hashPassword, randomToken, sha256, verifyPassword } from '../common/crypto';
import { AppConfig } from '../config/app-config.service';
import { AuditService } from '../audit/audit.service';
import { NewPlayerService } from '../player/new-player.service';
import { PrismaService, Tx } from '../prisma/prisma.service';

export interface ClientMeta {
  userAgent?: string;
  ipHash?: string;
}

export interface TokenPair {
  accessToken: string;
  accessTokenExpiresIn: number;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

@Injectable()
export class AuthService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly jwt: JwtService,
    private readonly config: AppConfig,
    private readonly audit: AuditService,
    private readonly newPlayer: NewPlayerService,
  ) {}

  /** Creates a brand-new guest account. The returned refresh token IS the guest's credential. */
  async createGuest(dto: GuestAuthRequest, meta: ClientMeta) {
    return this.prisma.tx(async (tx) => {
      const user = await this.newPlayer.create(tx, {
        ageBand: dto.ageBand,
        locale: dto.locale,
        deviceIdHash: sha256(dto.deviceId),
      });
      const tokens = await this.issueSession(tx, user.id, meta);
      await this.audit.log({ actorUserId: user.id, action: 'auth.guest_created', targetType: 'user', targetId: user.id, ipHash: meta.ipHash }, tx);
      return { userId: user.id, isGuest: true, ...tokens };
    });
  }

  /** Attaches e-mail + password to the current guest account so it can be recovered on a new device. */
  async linkAccount(userId: string, dto: LinkAccountRequest, meta: ClientMeta) {
    const email = dto.email.trim().toLowerCase();
    const passwordHash = await hashPassword(dto.password);
    return this.prisma.tx(async (tx) => {
      const user = await tx.user.findUniqueOrThrow({ where: { id: userId } });
      if (user.email) throw AppException.conflict(ErrorCode.ACCOUNT_ALREADY_LINKED, 'Account is already linked');
      if (user.ageBand === 'UNDER_13' && !user.parentalConsentAt) {
        throw new AppException(
          ErrorCode.PARENTAL_CONSENT_REQUIRED,
          'A parent or guardian must approve linking for players under 13',
          HttpStatus.FORBIDDEN,
        );
      }
      const taken = await tx.user.findUnique({ where: { email }, select: { id: true } });
      if (taken) throw AppException.conflict(ErrorCode.EMAIL_IN_USE, 'E-mail is already used by another account');
      await tx.user.update({ where: { id: userId }, data: { email, passwordHash, linkedAt: new Date() } });
      await this.audit.log({ actorUserId: userId, action: 'auth.account_linked', targetType: 'user', targetId: userId, ipHash: meta.ipHash }, tx);
      return { userId, isGuest: false, email };
    });
  }

  /** Account recovery: sign in on a new device with the linked e-mail. */
  async login(dto: LoginRequest, meta: ClientMeta) {
    const email = dto.email.trim().toLowerCase();
    const user = await this.prisma.user.findUnique({ where: { email } });
    const ok = await verifyPassword(dto.password, user?.passwordHash ?? DUMMY_PASSWORD_HASH);
    if (!user || !ok || user.status !== 'ACTIVE') {
      await this.audit.log({ actorUserId: user?.id, action: 'auth.login_failed', metadata: { emailHash: sha256(email) }, ipHash: meta.ipHash });
      throw AppException.unauthorized(ErrorCode.INVALID_CREDENTIALS, 'E-mail or password is incorrect');
    }
    return this.prisma.tx(async (tx) => {
      const tokens = await this.issueSession(tx, user.id, meta);
      await this.audit.log({ actorUserId: user.id, action: 'auth.login', ipHash: meta.ipHash }, tx);
      return { userId: user.id, isGuest: false, ...tokens };
    });
  }

  /**
   * Refresh-token rotation. Each refresh token works once. Presenting an already-rotated token
   * means it was copied (stolen): every session of that player is revoked.
   */
  async refresh(refreshToken: string, meta: ClientMeta) {
    const hash = sha256(refreshToken);
    const session = await this.prisma.gameSession.findUnique({ where: { refreshTokenHash: hash }, include: { user: true } });
    const invalid = () => AppException.unauthorized(ErrorCode.REFRESH_TOKEN_INVALID, 'Refresh token is invalid or expired');
    if (!session) throw invalid();

    if (session.revokedAt) {
      await this.prisma.gameSession.updateMany({ where: { userId: session.userId, revokedAt: null }, data: { revokedAt: new Date() } });
      await this.audit.log({ actorUserId: session.userId, action: 'auth.refresh_token_reuse', targetType: 'session', targetId: session.id, ipHash: meta.ipHash });
      throw invalid();
    }
    if (session.expiresAt < new Date() || session.user.status !== 'ACTIVE') throw invalid();

    return this.prisma.tx(async (tx) => {
      const revoked = await tx.gameSession.updateMany({ where: { id: session.id, revokedAt: null }, data: { revokedAt: new Date() } });
      if (revoked.count !== 1) throw invalid(); // lost a race with a parallel refresh
      const tokens = await this.issueSession(tx, session.userId, meta);
      return { userId: session.userId, isGuest: session.user.email === null, ...tokens };
    });
  }

  async logout(sessionId: string): Promise<void> {
    await this.prisma.gameSession.updateMany({ where: { id: sessionId, revokedAt: null }, data: { revokedAt: new Date() } });
  }

  private async issueSession(tx: Tx, userId: string, meta: ClientMeta): Promise<TokenPair> {
    const refreshToken = randomToken(32);
    const ttlDays = this.config.get('REFRESH_TOKEN_TTL_DAYS');
    const expiresAt = new Date(Date.now() + ttlDays * 86_400_000);
    const session = await tx.gameSession.create({
      data: { userId, refreshTokenHash: sha256(refreshToken), userAgent: meta.userAgent?.slice(0, 255), expiresAt },
    });
    const ttl = this.config.get('JWT_ACCESS_TTL_SECONDS');
    const accessToken = await this.jwt.signAsync({ sub: userId, sid: session.id }, { expiresIn: ttl, algorithm: 'HS256' });
    return { accessToken, accessTokenExpiresIn: ttl, refreshToken, refreshTokenExpiresAt: expiresAt.toISOString() };
  }
}
