import { randomUUID } from 'node:crypto';
import type { NextFunction, Request, Response } from 'express';

/** Attaches a request id (accepts a safe incoming one) for log correlation. */
export function requestIdMiddleware(req: Request, res: Response, next: NextFunction): void {
  const incoming = req.headers['x-request-id'];
  const id = typeof incoming === 'string' && /^[A-Za-z0-9-]{8,64}$/.test(incoming) ? incoming : randomUUID();
  req.headers['x-request-id'] = id;
  res.setHeader('X-Request-Id', id);
  next();
}
