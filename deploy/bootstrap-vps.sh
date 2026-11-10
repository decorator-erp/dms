#!/usr/bin/env bash
# DEP-1: one-time setup of PERA on the shared Hostinger VPS, behind the edge Caddy that already serves
# the other sites (/srv/edge, the only container on ports 80/443). The repo is private, so copy the
# deploy/ folder to the VPS first, then run this as root:
#   scp -r deploy root@<VPS IP>:/root/pera-deploy
#   ssh -t root@<VPS IP> 'bash /root/pera-deploy/bootstrap-vps.sh'
#
# It changes nothing outside these three things:
#   - /srv/pera (compose file, scripts, a .env of freshly generated secrets) and the `pera` project;
#   - /srv/edge/conf/pera.caddy (plus an `import pera.caddy` line in the edge Caddyfile when it lists
#     sites one by one), followed by a validated `caddy reload` of the edge (never a restart);
#   - one crontab line for the nightly backup.
# No firewall, swap or Docker changes. Safe to run again: it keeps an existing .env.
set -euo pipefail

SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIR="${PERA_DIR:-/srv/pera}"
EDGE_DIR="${EDGE_DIR:-/srv/edge}"
API_PROBE_IMAGE="${API_PROBE_IMAGE:-ghcr.io/decorator-erp/pera-api:latest}"

fail() { echo "!! $*" >&2; exit 1; }
[ "$(id -u)" -eq 0 ] || [ -n "${PERA_ALLOW_NON_ROOT:-}" ] || fail "Run as root (sudo -i)."

echo "== 1/6 Preflight (the shared edge)"
command -v docker >/dev/null 2>&1 || fail "Docker is not installed. This script expects the VPS's existing Docker."
docker network inspect edge >/dev/null 2>&1 || fail "The external Docker network 'edge' does not exist (it belongs to $EDGE_DIR)."
[ -d "$EDGE_DIR/conf" ] || fail "$EDGE_DIR/conf not found."
edge_cid=$(docker ps -q --filter label=com.docker.compose.project=edge --filter label=com.docker.compose.service=caddy | head -1)
[ -n "$edge_cid" ] || fail "The edge Caddy container is not running."
caddyfile=$(grep -E '^CADDYFILE=' "$EDGE_DIR/.env" 2>/dev/null | cut -d= -f2 || true)
caddyfile=${caddyfile:-Caddyfile}
[ -f "$EDGE_DIR/conf/$caddyfile" ] || fail "$EDGE_DIR/conf/$caddyfile not found."
# How the edge pulls in per-site files: either a wildcard (`import <dir>/*.caddy`) or one explicit
# line per site (`import ta3lom.caddy`, as on the PERA VPS). With explicit lines, PERA's own
# `import pera.caddy` is appended in step 5 (validated, and undone if the edge rejects it).
cfg="$EDGE_DIR/conf/$caddyfile"
add_import=""
glob=$(grep -Eo '^[[:space:]]*import[[:space:]]+[^[:space:]]*\*[^[:space:]]*' "$cfg" | awk '{print $2}' | head -1 || true)
if [ -n "$glob" ]; then
  case "$glob" in
    /etc/caddy/*) rel="${glob#/etc/caddy/}" ;;
    /*) fail "The edge imports '$glob', outside /etc/caddy; add pera.caddy by hand." ;;
    *) rel="$glob" ;;
  esac
  site_dir="$EDGE_DIR/conf"; [ "$(dirname "$rel")" = "." ] || site_dir="$site_dir/$(dirname "$rel")"
  site_name="$(basename "$rel")"; site_name="pera${site_name#\*}"
  [ -d "$site_dir" ] || fail "$site_dir (from '$glob') not found."
elif grep -Eq '^[[:space:]]*import[[:space:]]+[^[:space:]/*]+\.caddy[[:space:]]*$' "$cfg"; then
  site_dir="$EDGE_DIR/conf"; site_name="pera.caddy"
  grep -Eq '^[[:space:]]*import[[:space:]]+pera\.caddy[[:space:]]*$' "$cfg" || add_import=1
else
  fail "$cfg imports no per-site files ('import <site>.caddy' or 'import *.caddy'); add pera.caddy by hand."
fi
echo "   edge Caddy $edge_cid, config $caddyfile, PERA route -> $site_dir/$site_name${add_import:+ (+ 'import $site_name' in $caddyfile)}"

echo "== 2/6 $DIR"
mkdir -p "$DIR"
for f in docker-compose.prod.yml deploy.sh backup.sh .env.example pera.caddy; do
  [ -f "$SRC/$f" ] || fail "missing $SRC/$f — copy the whole deploy/ folder to the VPS."
  [ "$SRC" = "$DIR" ] || cp "$SRC/$f" "$DIR/$f"
done
chmod +x "$DIR/deploy.sh" "$DIR/backup.sh"
cd "$DIR"

ip="${PUBLIC_HOST:-$(curl -fsS --max-time 5 https://api.ipify.org || hostname -I | awk '{print $1}')}"
if [ ! -f .env ]; then
  gen() { tr -dc 'A-Za-z0-9' </dev/urandom | head -c "$1" || true; }
  admin_pw="$(gen 14)Aa1!"
  cat > .env <<ENV
POSTGRES_PASSWORD=$(gen 32)
JWT_KEY=$(gen 64)
ADMIN_PASSWORD=$admin_pw
PUBLIC_ORIGIN=http://$ip
API_TAG=latest
WEB_TAG=latest
API_MEM_LIMIT=768m
POSTGRES_MEM_LIMIT=512m
ENV
  chmod 600 .env
  echo "   wrote .env — first admin: admin@system.dms / $admin_pw  (save it now; it is also in $DIR/.env)"
else
  echo "   keeping the existing .env"
fi

echo "== 3/6 Registry access (private images)"
if [ -n "${PERA_SKIP_PULL_CHECK:-}" ]; then
  echo "   skipped"
elif docker pull -q "$API_PROBE_IMAGE" >/dev/null 2>&1; then
  echo "   the existing Docker login can pull the PERA images"
else
  echo "   cannot pull $API_PROBE_IMAGE with the current login; log in to ghcr.io"
  read -r -p "   GitHub username: " gh_user
  read -r -s -p "   GitHub token with read:packages: " gh_token; echo
  echo "$gh_token" | docker login ghcr.io -u "$gh_user" --password-stdin
fi

echo "== 4/6 Start PERA"
./deploy.sh

echo "== 5/6 Route on the edge"
target="$site_dir/$site_name"
address="${PERA_ADDRESS:-http://$ip}"
sed "s|^PERA_ADDRESS {|$address {|" pera.caddy > pera.caddy.rendered
if [ -f "$target" ] && cmp -s pera.caddy.rendered "$target" && [ -z "$add_import" ]; then
  rm -f pera.caddy.rendered
  echo "   $target is up to date (edge not touched)"
else
  # Previous copies are kept in $DIR, not in the edge's conf folder.
  stamp=$(date +%Y%m%d%H%M%S)
  backup=""; cfg_backup=""
  if [ -f "$target" ]; then backup="$DIR/edge-route.bak.$stamp"; cp "$target" "$backup"; fi
  # cat > keeps the file's owner and mode (the edge files belong to the deploy user).
  cat pera.caddy.rendered > "$target"; rm -f pera.caddy.rendered
  if [ -n "$add_import" ]; then
    cfg_backup="$DIR/edge-$caddyfile.bak.$stamp"; cp -p "$cfg" "$cfg_backup"
    [ -z "$(tail -c1 "$cfg")" ] || echo >> "$cfg"
    echo "import $site_name" >> "$cfg"
  fi
  restore() {
    echo "$2" | tail -5 >&2
    if [ -n "$backup" ]; then cat "$backup" > "$target"; else rm -f "$target"; fi
    if [ -n "$cfg_backup" ]; then cat "$cfg_backup" > "$cfg"; fi
    fail "$1 — the edge config was put back as it was; the edge keeps serving the other sites unchanged."
  }
  out=$(docker exec "$edge_cid" caddy validate --config "/etc/caddy/$caddyfile" --adapter caddyfile 2>&1) \
    || restore "the edge config does not validate with the PERA route" "$out"
  out=$(docker exec "$edge_cid" caddy reload --config "/etc/caddy/$caddyfile" --adapter caddyfile 2>&1) \
    || restore "caddy reload failed" "$out"
  echo "   $target -> $address${add_import:+, 'import $site_name' added to $caddyfile} (edge validated and reloaded)"
fi

echo "== 6/6 Check + nightly backup"
host="${address#http://}"; host="${host#https://}"
ok=""; status=0
for _ in $(seq 1 30); do
  # The body must be PERA's own "Healthy": a 2xx from some other upstream does not count.
  if [ "$(curl -fsS --max-time 5 -H "Host: $host" "http://127.0.0.1/health" 2>/dev/null)" = "Healthy" ]; then ok=1; break; fi
  sleep 2
done
if [ -n "$ok" ]; then
  echo "   $address/health is healthy"
  # And /api must reach PERA's API: an unauthenticated call answers PERA's 401.
  api_code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 -H "Host: $host" "http://127.0.0.1/api/employees/me")
  if [ "$api_code" = "401" ]; then
    echo "   $address/api answers from PERA (401 without sign-in)"
  else
    echo "!! $address/api/employees/me answered $api_code, not PERA's 401: /api may be reaching another upstream."
    status=1
  fi
else
  echo "!! $address/health did not answer 'Healthy' within a minute. It answered:"
  curl -s --max-time 5 -H "Host: $host" "http://127.0.0.1/health" | head -c 300; echo
  echo "   Check: cd $DIR && docker compose -f docker-compose.prod.yml logs --tail 50"
  status=1
fi
if [ -z "${PERA_SKIP_CRON:-}" ]; then
  ( crontab -l 2>/dev/null | grep -v "$DIR/backup.sh"; echo "15 3 * * * $DIR/backup.sh >> /var/log/pera-backup.log 2>&1" ) | crontab -
fi
echo
echo "PERA: $address  —  logs: cd $DIR && docker compose -f docker-compose.prod.yml logs -f"
exit "${status:-0}"
