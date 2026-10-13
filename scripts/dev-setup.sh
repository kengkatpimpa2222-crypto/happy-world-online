#!/usr/bin/env bash
# One-shot local setup: install deps, start Postgres/Redis in Docker, migrate, seed.
set -euo pipefail
cd "$(dirname "$0")/.."

npm install
docker compose -f infrastructure/docker/docker-compose.yml up -d --wait postgres redis

if [ ! -f apps/backend/.env ]; then
  cp apps/backend/.env.example apps/backend/.env
  secret=$(node -e "console.log(require('crypto').randomBytes(48).toString('base64url'))")
  sed -i.bak "s#^JWT_ACCESS_SECRET=.*#JWT_ACCESS_SECRET=${secret}#" apps/backend/.env && rm -f apps/backend/.env.bak
  echo "Created apps/backend/.env with a random JWT secret"
fi

npm run build -w @hwo/shared-contracts
(cd apps/backend && npx prisma migrate deploy && npm run db:seed)
echo "Done. Start the API with: npm run dev"
