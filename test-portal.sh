#!/usr/bin/env bash
# ============================================================================
#  Jade Capital — End-to-End smoke test (paste-and-go)
# ============================================================================
#  Uso:
#    chmod +x test-portal.sh
#    ./test-portal.sh
#
#  Requisitos:
#    - Portal levantado (docker compose up -d)
#    - jq instalado (sudo apt install jq) para parsear JSON
#    - curl
# ============================================================================

set -euo pipefail

API="http://localhost:18080"
EMAIL="trader+$(date +%s)@jade.test"
PASSWORD="TestPassword123!"

step() { echo; echo "── $1 ──"; }

step "0. Healthchecks"
echo "  live  : $(curl -s "$API/health/live")"
echo "  ready : $(curl -s "$API/health/ready")"

step "1. Register (POST /api/auth/register)"
REG=$(curl -s -X POST "$API/api/auth/register" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$EMAIL\",\"displayName\":\"Trader Test\",\"password\":\"$PASSWORD\"}")
echo "$REG" | jq .
USER_ID=$(echo "$REG" | jq -r '.userId')
ACCESS=$(echo "$REG"  | jq -r '.accessToken')

step "2. Login (POST /api/auth/login) — fresh tokens"
LOGIN=$(curl -s -X POST "$API/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}")
echo "$LOGIN" | jq '{userId, email, role, accessTokenExpiresAt}'

step "3. Refresh (POST /api/auth/refresh)"
REFRESH=$(echo "$LOGIN" | jq -r '.refreshToken')
NEW=$(curl -s -X POST "$API/api/auth/refresh" \
  -H "Content-Type: application/json" \
  -d "{\"refreshToken\":\"$REFRESH\"}")
echo "$NEW" | jq '{accessTokenExpiresAt, refreshTokenExpiresAt}'

step "4. Forgot password (POST /api/auth/forgot-password)"
curl -s -X POST "$API/api/auth/forgot-password" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$EMAIL\"}" | jq .

step "5. Check Mailpit: http://localhost:8025 → ver email capturado"
sleep 2
curl -s "http://localhost:8025/api/v1/messages" \
  | jq "{total, unread, latest: .messages[-1] | {Subject, To: .To[0].Address, Snippet}}"

step "6. Auth-required endpoint sanity (GET /api/trades con Bearer)"
LIST=$(curl -s "$API/api/trades?page=1&pageSize=5" \
  -H "Authorization: Bearer $ACCESS")
echo "$LIST" | jq '{total, page, pageSize, count: (.items|length)}'

step "7. Listo. User ID: $USER_ID"
echo "  email    : $EMAIL"
echo "  pass     : $PASSWORD"
echo "  podés navegar a:"
echo "    Portal   → http://localhost:4200"
echo "    API      → http://localhost:18080"
echo "    Swagger  → http://localhost:18080/swagger"
echo "    Scalar   → http://localhost:18080/scalar"
echo "    Mailpit  → http://localhost:8025"
echo "    MinIO    → http://localhost:9001  (user: jade-minio)"
