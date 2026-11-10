#!/usr/bin/env bash
# DEP-1: one-time setup of a fresh Ubuntu/Debian VPS for PERA. Run as root:
#   curl -fsSL https://raw.githubusercontent.com/decorator-erp/dms/master/deploy/bootstrap-vps.sh -o bootstrap-vps.sh
#   bash bootstrap-vps.sh
# It is safe to run again: it keeps an existing /opt/pera/.env and only adds what is missing.
#
# What it does:
#   1. installs Docker Engine + the compose plugin (if missing) and a 2 GB swap file on small VPSes;
#   2. opens the firewall for SSH (22), HTTP (80) and HTTPS (443, for when a domain is added);
#   3. creates /opt/pera with the compose file, scripts and a .env of freshly generated secrets;
#   4. logs Docker in to GHCR (the images are private) with a read-only token you paste;
#   5. starts PERA and installs the nightly backup.
set -euo pipefail

REPO_RAW="https://raw.githubusercontent.com/decorator-erp/dms/master/deploy"
DIR=/opt/pera

[ "$(id -u)" -eq 0 ] || { echo "Run as root (sudo -i)."; exit 1; }

echo "== 1/5 Docker"
if ! command -v docker >/dev/null 2>&1; then
  curl -fsSL https://get.docker.com | sh
fi
systemctl enable --now docker

mem_mb=$(awk '/MemTotal/ {print int($2/1024)}' /proc/meminfo)
if [ "$mem_mb" -le 2100 ] && ! swapon --show | grep -q /swapfile; then
  echo "   adding a 2 GB swap file (RAM is ${mem_mb} MB)"
  fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
  grep -q '^/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi

echo "== 2/5 Firewall"
if command -v ufw >/dev/null 2>&1; then
  ufw allow OpenSSH >/dev/null
  ufw allow 80/tcp >/dev/null
  ufw allow 443/tcp >/dev/null
  ufw --force enable >/dev/null
  ufw status | head -8
else
  echo "   ufw not installed; open ports 22, 80 and 443 in the Hostinger panel firewall."
fi

echo "== 3/5 /opt/pera"
mkdir -p "$DIR"
cd "$DIR"
for f in docker-compose.prod.yml deploy.sh backup.sh .env.example; do
  curl -fsSL "$REPO_RAW/$f" -o "$f"
done
chmod +x deploy.sh backup.sh

if [ ! -f .env ]; then
  ip=$(curl -fsS https://api.ipify.org || hostname -I | awk '{print $1}')
  gen() { tr -dc 'A-Za-z0-9' </dev/urandom | head -c "$1"; }
  admin_pw="$(gen 14)Aa1!"
  cat > .env <<ENV
POSTGRES_PASSWORD=$(gen 32)
JWT_KEY=$(gen 64)
ADMIN_PASSWORD=$admin_pw
PUBLIC_ORIGIN=http://$ip
SITE_ADDRESS=:80
API_TAG=latest
WEB_TAG=latest
API_MEM_LIMIT=768m
POSTGRES_MEM_LIMIT=768m
ENV
  chmod 600 .env
  echo "   wrote .env — first admin: admin@system.dms / $admin_pw  (save it now; it is also in $DIR/.env)"
else
  echo "   keeping the existing .env"
fi

echo "== 4/5 GHCR login (private images)"
if ! grep -q ghcr.io ~/.docker/config.json 2>/dev/null; then
  read -r -p "   GitHub username: " gh_user
  read -r -s -p "   GitHub token with read:packages: " gh_token; echo
  echo "$gh_token" | docker login ghcr.io -u "$gh_user" --password-stdin
fi

echo "== 5/5 Start PERA + nightly backup"
./deploy.sh
( crontab -l 2>/dev/null | grep -v '/opt/pera/backup.sh'; echo "15 3 * * * /opt/pera/backup.sh >> /var/log/pera-backup.log 2>&1" ) | crontab -
echo
echo "PERA is starting at $(grep PUBLIC_ORIGIN .env | cut -d= -f2)  —  logs: cd $DIR && docker compose -f docker-compose.prod.yml logs -f"
