#!/usr/bin/env bash
# Compiles and tests the engine-independent C# Core of the Unity client with the .NET 8 SDK.
# Needs either `dotnet` (8.x) on PATH or Docker. Integration tests run only when HWO_API_URL is set:
#   HWO_API_URL=http://localhost:3000 ./scripts/test-game-client.sh
# They create throwaway guest accounts, so point them at a dev backend, never production.
# HWO_DOCKER_ARGS adds extra `docker run` flags (e.g. a corporate CA certificate).
# Tip: start that backend with RATE_LIMIT_GUEST_PER_HOUR=1000 (the default 10/hour trips after a couple of runs).
set -euo pipefail
cd "$(dirname "$0")/../apps/game-client"
if command -v dotnet >/dev/null 2>&1; then
  (cd Tests/HappyWorld.Core.Tests && dotnet test)
else
  docker run --rm --network host \
    -e HWO_API_URL="${HWO_API_URL:-}" -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    ${HTTPS_PROXY:+-e HTTPS_PROXY=$HTTPS_PROXY -e HTTP_PROXY=$HTTPS_PROXY -e NO_PROXY=localhost,127.0.0.1} \
    ${HWO_DOCKER_ARGS:-} \
    -v "$PWD":/w -w /w/Tests/HappyWorld.Core.Tests \
    mcr.microsoft.com/dotnet/sdk:8.0 dotnet test
fi
