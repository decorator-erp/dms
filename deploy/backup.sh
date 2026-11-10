#!/usr/bin/env bash
# DEP-1: nightly backup (cron, 03:15) — a compressed pg_dump and the uploaded files, kept 7 days.
# Restore: see the runbook (dms-docs/backend/dms-modular/hosting/deploy-hostinger.md).
set -euo pipefail
cd "$(dirname "$0")"
dest=/var/backups/pera
stamp=$(date +%Y%m%d-%H%M%S)
mkdir -p "$dest"
docker compose -f docker-compose.prod.yml --env-file .env exec -T postgres \
  pg_dump -U pera -d dms_modular --format=custom | gzip > "$dest/db-$stamp.dump.gz"
docker run --rm -v pera_uploads:/data:ro -v "$dest":/backup alpine \
  tar czf "/backup/uploads-$stamp.tar.gz" -C /data .
find "$dest" -type f -mtime +7 -delete
echo "backup: $dest/db-$stamp.dump.gz, $dest/uploads-$stamp.tar.gz"
