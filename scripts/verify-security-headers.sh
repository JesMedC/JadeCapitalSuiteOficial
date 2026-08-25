#!/usr/bin/env bash

set -euo pipefail

PORT="${1:-18080}"
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
IMAGE="jade-frontend-csp-test:local"
NETWORK="jade-csp-test-$$"
FRONTEND_CONTAINER="jade-csp-frontend-$$"
API_CONTAINER="jade-csp-api-$$"
EDGE_CONTAINER="jade-csp-edge-$$"
TMP_DIR="$(mktemp -d)"

cleanup() {
    docker rm -f "${EDGE_CONTAINER}" "${FRONTEND_CONTAINER}" "${API_CONTAINER}" >/dev/null 2>&1 || true
    docker network rm "${NETWORK}" >/dev/null 2>&1 || true
    rm -rf "${TMP_DIR}"
}
trap cleanup EXIT

for command in docker curl python3; do
    if ! command -v "${command}" >/dev/null 2>&1; then
        echo "ERROR: ${command} is required." >&2
        exit 2
    fi
done

echo "=== Building the production frontend image ==="
docker build \
    --file "${REPO_ROOT}/infrastructure/Dockerfile.frontend.prod" \
    --build-arg FRONTEND_SENTRY_DSN= \
    --build-arg SENTRY_RELEASE=jade-csp-verification \
    --build-arg ALLOW_FRONTEND_SENTRY_DISABLED=true \
    --tag "${IMAGE}" \
    "${REPO_ROOT}"

docker network create "${NETWORK}" >/dev/null
docker run --rm -d --name "${FRONTEND_CONTAINER}" --network "${NETWORK}" --network-alias frontend "${IMAGE}" >/dev/null
docker run --rm -d --name "${API_CONTAINER}" --network "${NETWORK}" --network-alias api nginx:1.27-alpine >/dev/null

NGINX_ARGS=(
    --network "${NETWORK}"
    -v "${REPO_ROOT}/infrastructure/nginx/nginx.conf:/etc/nginx/nginx.conf:ro"
    -v "${REPO_ROOT}/infrastructure/nginx/conf.d/jade.conf:/etc/nginx/conf.d/default.conf:ro"
)

echo "=== Validating the deployed nginx configuration ==="
docker run --rm "${NGINX_ARGS[@]}" nginx:1.27-alpine nginx -t

docker run --rm -d \
    --name "${EDGE_CONTAINER}" \
    --network "${NETWORK}" \
    -p "${PORT}:80" \
    -v "${REPO_ROOT}/infrastructure/nginx/nginx.conf:/etc/nginx/nginx.conf:ro" \
    -v "${REPO_ROOT}/infrastructure/nginx/conf.d/jade.conf:/etc/nginx/conf.d/default.conf:ro" \
    nginx:1.27-alpine >/dev/null

for _ in {1..20}; do
    if curl -sf -o /dev/null "http://localhost:${PORT}/"; then
        break
    fi
    sleep 0.5
done

if ! curl -sf -o /dev/null "http://localhost:${PORT}/"; then
    echo "ERROR: nginx did not become ready within 10 seconds." >&2
    exit 2
fi

for response in 1 2; do
    curl -sf \
        --dump-header "${TMP_DIR}/headers-${response}" \
        --output "${TMP_DIR}/body-${response}" \
        "http://localhost:${PORT}/"
done

python3 - "${TMP_DIR}" <<'PY'
from html.parser import HTMLParser
from pathlib import Path
import re
import sys


class ScriptNonceParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.nonces = []

    def handle_starttag(self, tag, attrs):
        if tag.lower() == "script":
            self.nonces.append(dict(attrs).get("nonce"))


root = Path(sys.argv[1])
response_nonces = []

for response in (1, 2):
    headers = (root / f"headers-{response}").read_text()
    body = (root / f"body-{response}").read_text()
    csp_match = re.search(r"^Content-Security-Policy:\s*(.+?)\r?$", headers, re.I | re.M)
    if not csp_match:
        raise AssertionError(f"response {response}: Content-Security-Policy header missing")

    csp = csp_match.group(1)
    nonce_match = re.search(r"(?:^|;)\s*script-src\s+[^;]*'nonce-([0-9a-f]{32})'", csp)
    if not nonce_match:
        raise AssertionError(f"response {response}: script-src lacks a 128-bit hexadecimal nonce: {csp}")
    nonce = nonce_match.group(1)
    response_nonces.append(nonce)

    parser = ScriptNonceParser()
    parser.feed(body)
    if not parser.nonces:
        raise AssertionError(f"response {response}: no executable script tags found")
    if any(script_nonce != nonce for script_nonce in parser.nonces):
        raise AssertionError(
            f"response {response}: every script nonce must equal header nonce {nonce}; got {parser.nonces}"
        )

    combined = headers + body
    for marker in ("{request_nonce}", "__CSP_NONCE__"):
        if marker in combined:
            raise AssertionError(f"response {response}: literal marker {marker} leaked")
    if "style-src 'self' 'unsafe-inline'" not in csp:
        raise AssertionError(f"response {response}: style unsafe-inline was not preserved")
    if "frame-ancestors 'none'" not in csp:
        raise AssertionError(f"response {response}: frame-ancestors 'none' was not preserved")

    script_src = re.search(r"(?:^|;)\s*script-src\s+([^;]+)", csp)
    if script_src and "'unsafe-inline'" in script_src.group(1):
        raise AssertionError(f"response {response}: script unsafe-inline must remain absent")

for header, expected in {
    "Strict-Transport-Security": "max-age=63072000",
    "Permissions-Policy": "camera=()",
    "X-Frame-Options": "DENY",
    "X-Content-Type-Options": "nosniff",
    "Referrer-Policy": "strict-origin-when-cross-origin",
}.items():
    headers = (root / "headers-1").read_text()
    match = re.search(rf"^{re.escape(header)}:\s*(.+?)\r?$", headers, re.I | re.M)
    if not match or expected.lower() not in match.group(1).lower():
        raise AssertionError(f"{header} missing expected value {expected}")

if response_nonces[0] == response_nonces[1]:
    raise AssertionError("consecutive responses reused the same CSP nonce")

print(
    "OK — production image and nginx config passed; "
    f"response nonces are distinct ({response_nonces[0]}, {response_nonces[1]}) and match every script tag."
)
PY
