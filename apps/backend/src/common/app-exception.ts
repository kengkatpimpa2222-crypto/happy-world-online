import { HttpException, HttpStatus } from '@nestjs/common';
import { ErrorCode } from '@hwo/shared-contracts';

/** Domain error with a stable `code` the client can localize. */
export class AppException extends HttpException {
  constructor(
    public readonly code: ErrorCode,
    message: string,
    status: HttpStatus,
    public readonly details?: unknown,
  ) {
    super({ code, message, details }, status);
  }

  static notFound(message = 'Resource not found') {
    return new AppException(ErrorCode.NOT_FOUND, message, HttpStatus.NOT_FOUND);
  }
  static conflict(code: ErrorCode, message: string, details?: unknown) {
    return new AppException(code, message, HttpStatus.CONFLICT, details);
  }
  static badRequest(code: ErrorCode, message: string, details?: unknown) {
    return new AppException(code, message, HttpStatus.BAD_REQUEST, details);
  }
  static unauthorized(code: ErrorCode = ErrorCode.UNAUTHORIZED, message = 'Authentication required') {
    return new AppException(code, message, HttpStatus.UNAUTHORIZED);
  }
  static forbidden(code: ErrorCode = ErrorCode.FORBIDDEN, message = 'Forbidden') {
    return new AppException(code, message, HttpStatus.FORBIDDEN);
  }
}
