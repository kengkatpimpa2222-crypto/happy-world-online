import { ArgumentsHost, Catch, ExceptionFilter, HttpException, HttpStatus, Logger } from '@nestjs/common';
import { ThrottlerException } from '@nestjs/throttler';
import { Prisma } from '@prisma/client';
import { ApiErrorBody, ErrorCode } from '@hwo/shared-contracts';
import type { Request, Response } from 'express';
import { AppException } from './app-exception';

const STATUS_CODES: Partial<Record<number, ErrorCode>> = {
  400: ErrorCode.VALIDATION_FAILED,
  401: ErrorCode.UNAUTHORIZED,
  403: ErrorCode.FORBIDDEN,
  404: ErrorCode.NOT_FOUND,
  429: ErrorCode.RATE_LIMITED,
};

/**
 * Converts every error to `{ error: { code, message, details?, requestId } }`.
 * Stack traces are logged server-side only and never sent to clients.
 */
@Catch()
export class HttpExceptionFilter implements ExceptionFilter {
  private readonly logger = new Logger('HttpExceptionFilter');

  catch(exception: unknown, host: ArgumentsHost): void {
    const ctx = host.switchToHttp();
    const res = ctx.getResponse<Response>();
    const req = ctx.getRequest<Request>();
    const requestId = (req.headers['x-request-id'] as string | undefined) ?? undefined;

    let status = HttpStatus.INTERNAL_SERVER_ERROR;
    let body: ApiErrorBody['error'] = { code: ErrorCode.INTERNAL_ERROR, message: 'Internal server error' };

    if (exception instanceof AppException) {
      status = exception.getStatus();
      body = { code: exception.code, message: exception.message, details: exception.details };
    } else if (exception instanceof ThrottlerException) {
      status = HttpStatus.TOO_MANY_REQUESTS;
      body = { code: ErrorCode.RATE_LIMITED, message: 'Too many requests, please slow down' };
    } else if (exception instanceof HttpException) {
      status = exception.getStatus();
      const r = exception.getResponse();
      const message = typeof r === 'string' ? r : ((r as { message?: string }).message ?? exception.message);
      body = { code: STATUS_CODES[status] ?? ErrorCode.INTERNAL_ERROR, message: String(message) };
    } else if (exception instanceof Prisma.PrismaClientKnownRequestError && exception.code === 'P2002') {
      status = HttpStatus.CONFLICT;
      body = { code: ErrorCode.IDEMPOTENCY_KEY_REUSED, message: 'Conflicting concurrent request, please retry' };
    }

    if (status >= 500) {
      this.logger.error(
        `${req.method} ${req.url} -> ${status} [${requestId ?? '-'}]`,
        exception instanceof Error ? exception.stack : String(exception),
      );
    }

    if (exception instanceof ThrottlerException) res.setHeader('Retry-After', '60');
    res.status(status).json({ error: { ...body, requestId } } satisfies ApiErrorBody);
  }
}
