#!/usr/bin/env bash
# Lint, type-check, unit tests and integration tests (uses the separate happy_world_test DB).
set -euo pipefail
cd "$(dirname "$0")/.."
npm run lint
npm run typecheck
npm test
npm run test:e2e
