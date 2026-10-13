#!/bin/sh
# Separate database for automated tests (never share with real player data).
set -e
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres -c "CREATE DATABASE happy_world_test OWNER \"$POSTGRES_USER\";"
