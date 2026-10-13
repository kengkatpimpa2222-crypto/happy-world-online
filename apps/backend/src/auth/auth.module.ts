import { Module } from '@nestjs/common';
import { APP_GUARD } from '@nestjs/core';
import { JwtModule } from '@nestjs/jwt';
import { AppConfig } from '../config/app-config.service';
import { PlayerModule } from '../player/player.module';
import { AuthController } from './auth.controller';
import { AuthService } from './auth.service';
import { JwtAuthGuard } from './jwt-auth.guard';
import { SessionVerifier } from './session-verifier.service';

@Module({
  imports: [
    PlayerModule,
    JwtModule.registerAsync({
      global: true,
      inject: [AppConfig],
      useFactory: (config: AppConfig) => ({
        secret: config.get('JWT_ACCESS_SECRET'),
        signOptions: { algorithm: 'HS256', issuer: 'happy-world-online' },
        verifyOptions: { algorithms: ['HS256'], issuer: 'happy-world-online' },
      }),
    }),
  ],
  controllers: [AuthController],
  providers: [AuthService, SessionVerifier, { provide: APP_GUARD, useClass: JwtAuthGuard }],
  exports: [SessionVerifier],
})
export class AuthModule {}
