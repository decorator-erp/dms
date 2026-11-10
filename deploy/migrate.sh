#!/bin/sh
# DEP-1: applies every EF Core migration bundle in order. Each bundle applies only what is missing, so
# running this on every deploy is safe; the API container starts only after it exits successfully.
set -eu
: "${ConnectionStrings__DefaultConnection:?ConnectionStrings__DefaultConnection is not set}"
for bundle in /app/migrations/*; do
  echo "migrate: $(basename "$bundle")"
  "$bundle" --connection "$ConnectionStrings__DefaultConnection"
done
echo "migrate: done"
