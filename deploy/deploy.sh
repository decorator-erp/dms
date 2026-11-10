#!/usr/bin/env bash
# DEP-1: pulls the latest images and restarts what changed. Called by the GitHub deploy workflows over
# SSH; safe to run by hand: cd /opt/pera && ./deploy.sh
set -euo pipefail
cd "$(dirname "$0")"
docker compose -f docker-compose.prod.yml --env-file .env pull
docker compose -f docker-compose.prod.yml --env-file .env up -d --remove-orphans
docker image prune -f >/dev/null
docker compose -f docker-compose.prod.yml --env-file .env ps
