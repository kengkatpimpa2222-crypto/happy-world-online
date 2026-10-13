import { createParamDecorator, ExecutionContext, Param, SetMetadata } from '@nestjs/common';
import { ErrorCode, IDEMPOTENCY_HEADER, idempotencyKeySchema, questIdParamSchema, uuidParamSchema } from '@hwo/shared-contracts';
import type { Request } from 'express';
import { AppException } from './app-exception';
import { ZodValidationPipe } from './zod-validation.pipe';

export const IS_PUBLIC_KEY = 'isPublic';
/** Route does not require an access token. */
export const Public = () => SetMetadata(IS_PUBLIC_KEY, true);

export interface AuthUser {
  userId: string;
  sessionId: string;
  ageBand: 'UNDER_13' | 'TEEN_13_17' | 'ADULT_18_PLUS';
}

/** The authenticated player. Player identity ALWAYS comes from the token, never from the request body. */
export const CurrentUser = createParamDecorator((_: unknown, ctx: ExecutionContext): AuthUser => {
  const req = ctx.switchToHttp().getRequest<Request & { user?: AuthUser }>();
  if (!req.user) throw AppException.unauthorized();
  return req.user;
});

/** Required `Idempotency-Key` header for money-moving requests. */
export const IdempotencyKey = createParamDecorator((_: unknown, ctx: ExecutionContext): string => {
  const req = ctx.switchToHttp().getRequest<Request>();
  const raw = req.headers[IDEMPOTENCY_HEADER];
  if (typeof raw !== 'string' || raw.length === 0) {
    throw AppException.badRequest(ErrorCode.IDEMPOTENCY_KEY_REQUIRED, 'Idempotency-Key header is required');
  }
  const parsed = idempotencyKeySchema.safeParse(raw);
  if (!parsed.success) {
    throw AppException.badRequest(ErrorCode.VALIDATION_FAILED, parsed.error.issues[0]?.message ?? 'Invalid Idempotency-Key');
  }
  return parsed.data;
});

export const UuidParam = (name: string) => Param(name, new ZodValidationPipe(uuidParamSchema));
export const QuestIdParam = (name: string) => Param(name, new ZodValidationPipe(questIdParamSchema));
