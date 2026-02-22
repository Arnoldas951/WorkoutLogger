#!/usr/bin/env bash
set -e

host="${DB_HOST:-db}"
port="${DB_PORT:-5432}"

echo "Waiting for Postgres at $host:$port..."

for i in {1..30}; do
  if nc -z "$host" "$port"; then
    echo "Postgres is available"
    break
  fi
  echo "Waiting for Postgres... ($i)"
  sleep 1
done

# apply EF migrations then run the app
dotnet WorkoutLogger.dll
