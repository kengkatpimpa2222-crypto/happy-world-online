# syntax=docker/dockerfile:1
# Happy World Online backend image (build context = repository root)
# Behind a TLS-inspecting corporate proxy, pass its CA with:
#   docker build --secret id=ca_bundle,src=/path/to/ca.crt --build-arg HTTPS_PROXY=... ...
FROM node:22-bookworm-slim AS build
RUN apt-get update && apt-get install -y --no-install-recommends openssl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY package.json package-lock.json ./
COPY packages/shared-contracts/package.json packages/shared-contracts/
COPY apps/backend/package.json apps/backend/
RUN --mount=type=secret,id=ca_bundle,required=false \
    if [ -f /run/secrets/ca_bundle ]; then export NODE_EXTRA_CA_CERTS=/run/secrets/ca_bundle; fi; \
    npm ci --ignore-scripts
COPY packages/shared-contracts packages/shared-contracts
COPY apps/backend apps/backend
RUN npm run build -w @hwo/shared-contracts && npm run build -w @hwo/backend

FROM node:22-bookworm-slim AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends openssl tini && rm -rf /var/lib/apt/lists/*
ENV NODE_ENV=production
WORKDIR /app
COPY --from=build /app /app
USER node
WORKDIR /app/apps/backend
EXPOSE 3000
ENTRYPOINT ["/usr/bin/tini", "--"]
# Migrations are idempotent (`migrate deploy` only applies pending ones); seed only upserts reference data.
CMD ["sh", "-c", "npx prisma migrate deploy && node dist/database/seed.js && node dist/main.js"]
