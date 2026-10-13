import { PipeTransform } from '@nestjs/common';
import { ErrorCode } from '@hwo/shared-contracts';
import { z } from 'zod';
import { AppException } from './app-exception';

/** Validates and strips input with a zod schema; rejects unknown keys when the schema is strict. */
export class ZodValidationPipe<T extends z.ZodType> implements PipeTransform<unknown, z.infer<T>> {
  constructor(private readonly schema: T) {}

  transform(value: unknown): z.infer<T> {
    const result = this.schema.safeParse(value);
    if (!result.success) {
      throw AppException.badRequest(
        ErrorCode.VALIDATION_FAILED,
        'Request validation failed',
        result.error.issues.map((i) => ({ path: i.path.join('.'), message: i.message })),
      );
    }
    return result.data;
  }
}
