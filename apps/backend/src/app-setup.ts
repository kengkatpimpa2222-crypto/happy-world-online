import { INestApplication, VersioningType } from '@nestjs/common';
import { NestExpressApplication } from '@nestjs/platform-express';
import { DocumentBuilder, SwaggerModule } from '@nestjs/swagger';
import helmet from 'helmet';
import { HttpExceptionFilter } from './common/http-exception.filter';
import { requestIdMiddleware } from './common/request-id.middleware';
import { AppConfig } from './config/app-config.service';

/** Shared by main.ts and the integration tests so tests exercise the real HTTP pipeline. */
export function configureApp(app: INestApplication): void {
  const config = app.get(AppConfig);
  const express = app as NestExpressApplication;

  if (config.get('TRUST_PROXY')) express.set('trust proxy', 1);
  express.disable('x-powered-by');
  app.use(requestIdMiddleware);
  app.use(helmet());
  express.useBodyParser('json', { limit: '64kb' });

  const origins = config
    .get('CORS_ORIGINS')
    .split(',')
    .map((o) => o.trim())
    .filter(Boolean);
  // Mobile clients do not send Origin; browsers are limited to the explicit allow-list.
  app.enableCors({ origin: origins, credentials: false, methods: ['GET', 'POST', 'PATCH', 'DELETE'] });

  app.setGlobalPrefix('api');
  app.enableVersioning({ type: VersioningType.URI, defaultVersion: '1' });
  app.useGlobalFilters(new HttpExceptionFilter());
  app.enableShutdownHooks();

  // API docs are a development tool: never exposed in production.
  if (config.get('SWAGGER_ENABLED') && !config.isProduction) {
    const doc = SwaggerModule.createDocument(
      app,
      new DocumentBuilder()
        .setTitle('Happy World Online API')
        .setVersion('0.1.0')
        .addBearerAuth()
        .build(),
    );
    SwaggerModule.setup('api/docs', app, doc);
  }
}
