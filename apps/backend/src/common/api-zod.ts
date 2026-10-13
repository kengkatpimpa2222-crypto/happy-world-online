import { applyDecorators } from '@nestjs/common';
import { ApiBody } from '@nestjs/swagger';
import { z } from 'zod';

/** Publishes a zod schema as the OpenAPI request body. */
export function ApiZodBody(schema: z.ZodType, description?: string) {
  const json = z.toJSONSchema(schema, { io: 'input', unrepresentable: 'any' }) as Record<string, unknown>;
  delete json.$schema;
  return applyDecorators(ApiBody({ schema: json, description }));
}
