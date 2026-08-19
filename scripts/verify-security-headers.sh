#!/usr/bin/env bash
# Wave 10 slice 10.3 — runtime harness: spin up the slice-10.2 nginx container
# and assert the security headers are emitted correctly.
#
# USAGE
#   bash scripts/verify-security-headers.sh [<port>]
#   bash scripts/verify-security-headers.sh 18080
#
# WHAT IT DOES
#   1. Starts an ephemeral nginx:1.27-alpine container using the project's
#      infrastructure/nginx/nginx.conf + frontend/src/index.html as a static page.
#   2. Waits for nginx to accept connections (up to 10 s).
#   3. `curl -I` the root + asserts all 6 security headers are present with the
#      right shape.
#   4. Tears the container down (always, even on failure).
#
# WHY BOTH THIS AND verify-headers.py
#   - This script proves the headers are emitted by the REAL nginx binary
#     against a real socket (not just text-substring assertions on the conf file).
#   - verify-headers.py is the lightweight version for ad-hoc checks against
#     any URL (real prod, k8s ingress, dev `ng serve`, etc.).
#
# REQUIREMENTS
#   - docker (with the local user in the `docker` group, or run with sudo).
#   - curl.
#
# EXIT CODES
#   0 — all headers present + correctly shaped.
#   1 — at least one header missing or malformed.
#   2 — docker / curl not available, or container failed to start.

set -euo pipefail

PORT="${1:-18080}"
CONTAINER_NAME="jade-nginx-headers-test-$$"
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
NGINX_CONF="${REPO_ROOT}/infrastructure/nginx/nginx.conf"
INDEX_HTML="${REPO_ROOT}/frontend/src/index.html"

cleanup() {
    if docker ps -a --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
        docker rm -f "${CONTAINER_NAME}" >/dev/null 2>&1 || true
    fi
}
trap cleanup EXIT

if ! command -v docker >/dev/null 2>&1; then
    echo "ERROR: docker is required to run this verification." >&2
    exit 2
fi
if ! command -v curl >/dev/null 2>&1; then
    echo "ERROR: curl is required to run this verification." >&2
    exit 2
fi
if [[ ! -f "${NGINX_CONF}" ]]; then
    echo "ERROR: ${NGINX_CONF} not found. Run from the repo root." >&2
    exit 2
fi
if [[ ! -f "${INDEX_HTML}" ]]; then
    echo "ERROR: ${INDEX_HTML} not found. Run from the repo root." >&2
    exit 2
fi

echo "=== Starting nginx container (port=${PORT}) ==="
docker run --rm -d \
    --name "${CONTAINER_NAME}" \
    -p "${PORT}:80" \
    -v "${NGINX_CONF}:/etc/nginx/nginx.conf:ro" \
    -v "${INDEX_HTML}:/usr/share/nginx/html/index.html:ro" \
    nginx:1.27-alpine >/dev/null

# Wait for nginx to accept connections (up to 10 s).
for i in {1..20}; do
    if curl -sf -o /dev/null "http://localhost:${PORT}/"; then
        break
    fi
    sleep 0.5
done

if ! curl -sf -o /dev/null "http://localhost:${PORT}/"; then
    echo "ERROR: nginx did not become ready within 10 s." >&2
    exit 2
fi

echo "=== curl -I http://localhost:${PORT}/ ==="
HEADERS="$(curl -sI "http://localhost:${PORT}/")"
echo "${HEADERS}"
echo ""

FAIL=0

check() {
    local header="$1"
    local expected_substring="$2"
    if echo "${HEADERS}" | tr '[:upper:]' '[:lower:]' | grep -q "^${header}:"; then
        if echo "${HEADERS}" | tr '[:upper:]' '[:lower:]' | grep "^${header}:" | grep -q -- "${expected_substring}"; then
            echo "  OK   ${header} contains '${expected_substring}'"
        else
            echo "  FAIL ${header} missing expected substring '${expected_substring}'"
            FAIL=1
        fi
    else
        echo "  FAIL ${header} header missing entirely"
        FAIL=1
    fi
}

echo "=== Asserting headers ==="
check "content-security-policy"   "default-src 'self'"
check "strict-transport-security" "max-age=63072000"
check "permissions-policy"        "camera=()"
# Wave 9 baseline regression guards.
check "x-frame-options"           "DENY"
check "x-content-type-options"    "nosniff"
check "referrer-policy"           "strict-origin-when-cross-origin"

echo ""
if [[ "${FAIL}" -eq 0 ]]; then
    echo "OK — all 6 security headers present + correctly shaped."
    exit 0
fi
echo "FAILED — at least one header missing or malformed. See output above." >&2
exit 1