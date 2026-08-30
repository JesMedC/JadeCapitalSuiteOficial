#!/usr/bin/env bash
# Wave 10 slice 10.2 — one-shot certbot helper for legacy nginx.
#
# USAGE
#   ./infrastructure/certbot/setup-certs.sh <domain> <email>
#
# WHAT
#   1. Issues a Let's Encrypt cert via the running `nginx` + `certbot`
#      compose sidecars (webroot challenge).
#   2. Installs a cron job that renews every 12h (covered by the in-container
#      certbot loop in docker-compose.prod.yml, but the host cron is the
#      belt-and-braces fallback).
#
# WHEN TO RUN
#   - First-time prod deploy (after `docker compose up -d nginx`).
#   - After adding a new domain that needs to sit behind the same nginx.
#
# NOTE
#   Slice 10.3 ships a Caddyfile that handles TLS automatically; this
#   script becomes the fallback path if Caddy can't be used (some
#   legacy DNS providers don't support the ACME DNS-01 challenge).

set -euo pipefail

if [[ $# -lt 2 ]]; then
    echo "Usage: $0 <domain> <email>"
    echo "  e.g. $0 jadecapital.example.com ops@jadecapital.example.com"
    exit 1
fi

DOMAIN="$1"
EMAIL="$2"
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
COMPOSE_FILE="${REPO_ROOT}/docker-compose.prod.yml"

echo ">>> Issuing Let's Encrypt cert for ${DOMAIN} (email=${EMAIL})"
docker compose -f "${COMPOSE_FILE}" run --rm certbot certonly \
    --webroot -w /var/www/certbot \
    -d "${DOMAIN}" \
    --email "${EMAIL}" \
    --agree-tos --no-eff-email

echo ">>> Reloading nginx so the cert is picked up"
docker compose -f "${COMPOSE_FILE}" exec nginx nginx -s reload

echo ">>> Installing host fallback cron (renew every 12h)"
CRON_LINE="0 */12 * * * root cd ${REPO_ROOT} && docker compose -f ${COMPOSE_FILE} run --rm certbot renew --quiet >> /var/log/jadecapital-certbot.log 2>&1"

cat > /etc/cron.d/jadecapital-certbot <<EOF
# Wave 10 slice 10.2 — fallback Let's Encrypt renewal loop.
SHELL=/bin/bash
PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin

${CRON_LINE}
EOF
chmod 0644 /etc/cron.d/jadecapital-certbot

echo ""
echo "✅ Cert issued for ${DOMAIN}."
echo "   cron scheduled at /etc/cron.d/jadecapital-certbot"
echo "   nginx reloaded; smoke-test: curl -I https://${DOMAIN}/health"
