#!/usr/bin/env bash
# ============================================================================
#  Wave 5 — Slice 5c.2 — 5 manual E2E probes for the Wave 5 surface.
# ============================================================================
#  Uso:
#    chmod +x scripts/wave5-smoke.sh
#    ./scripts/wave5-smoke.sh
#
#  Requisitos:
#    - docker compose up -d (api + postgres + redis + minio + minio-init)
#    - jq + curl
#    - Optional: ollama serve on http://localhost:11434 (probe 5.2.3 + 5.2.4)
#
#  Probes per design.md 5c.2 / tasks.md 5c.2:
#    5.2.1  POST /api/imports/csv     (CSV upload → 202 + importJobId)
#    5.2.2  GET  /api/imports/{id}    (status poll → 200)
#    5.2.3  GET  /api/ai/health       (Ollama reachability — skip if not up)
#    5.2.4  POST /api/ai/risk-advice  (advisory round-trip — skip if not up)
#    5.2.5  GET  /api/coaching/prompts (rule-based prompts list)
#
#  Idempotent: registers a fresh user per run, creates a new account,
#  uploads a unique-named file. No shared state between runs.
#  Each probe prints PASS / SKIP / FAIL with a non-zero exit on FAIL.
# ============================================================================

set -u
API="${API:-http://localhost:18080}"
EMAIL="wave5+$(date +%s%N)@jade.test"
PASSWORD="TestPassword123!"
TOKEN=""
PASS=0
FAIL=0
SKIP=0
RESULTS=()
JOB_ID=""
ACCOUNT_ID=""

step() { echo; echo "── $1 ──"; }
pass() { PASS=$((PASS+1)); RESULTS+=("PASS  $1"); echo "  ✓ PASS  $1"; }
fail() { FAIL=$((FAIL+1)); RESULTS+=("FAIL  $1 ($2)"); echo "  ✗ FAIL  $1 — $2"; }
skip() { SKIP=$((SKIP+1)); RESULTS+=("SKIP  $1 ($2)"); echo "  ~ SKIP  $1 — $2"; }

require_jq() {
  if ! command -v jq >/dev/null 2>&1; then
    echo "jq is required (apt: sudo apt install jq)"
    exit 2
  fi
}

require_jq

# --------------------------------------------------------------------------
#  0. Bootstrap — register + open account
# --------------------------------------------------------------------------
step "0. Bootstrap — register + open account"
REG=$(curl -s -X POST "$API/api/auth/register" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$EMAIL\",\"displayName\":\"Wave5 Smoke\",\"password\":\"$PASSWORD\"}")
TOKEN=$(echo "$REG" | jq -r '.accessToken // empty')
if [ -z "$TOKEN" ]; then
  fail "0 bootstrap register" "no accessToken in response"
  exit 3
fi
echo "  email : $EMAIL"
echo "  token : ${TOKEN:0:20}..."
AUTH=(-H "Authorization: Bearer $TOKEN")

ACCT=$(curl -s -X POST "${AUTH[@]}" -H "Content-Type: application/json" \
  -d '{"name":"Wave5 Smoke Acct","broker":"IC Markets","marketType":1,"currency":"USD","initialBalance":10000,"leverage":100}' \
  "$API/api/accounts")
ACCOUNT_ID=$(echo "$ACCT" | jq -r '.id // empty')
if [ -z "$ACCOUNT_ID" ]; then
  fail "0 bootstrap open account" "no id in response: $ACCT"
  exit 3
fi
echo "  acct  : $ACCOUNT_ID"

# --------------------------------------------------------------------------
#  5.2.1 — POST /api/imports/csv
# --------------------------------------------------------------------------
step "5.2.1 POST /api/imports/csv (CSV upload)"
TMP_CSV="/tmp/wave5_521_$(date +%s%N).csv"
cat > "$TMP_CSV" <<'CSV'
Ticket,OpenTime,Symbol,Type,Volume,Price,SL,TP,Commission
T1,2026-01-02T09:00:00Z,EURUSD,buy,0.10,1.0800,1.0700,1.0900,0.0
T2,2026-01-02T10:00:00Z,EURUSD,buy,0.10,1.0810,1.0710,1.0910,0.0
T3,2026-01-02T11:00:00Z,GBPUSD,sell,0.20,1.2700,1.2800,1.2600,0.0
T4,2026-01-02T12:00:00Z,USDJPY,buy,0.05,150.10,149.50,151.00,0.0
T5,2026-01-02T13:00:00Z,AUDUSD,buy,0.15,0.6500,0.6400,0.6600,0.0
T6,2026-01-02T14:00:00Z,USDCAD,sell,0.10,1.3500,1.3600,1.3400,0.0
T7,2026-01-02T15:00:00Z,NZDJPY,buy,0.20,90.50,89.50,91.50,0.0
T8,2026-01-02T16:00:00Z,USDCHF,buy,0.10,0.8800,0.8700,0.8900,0.0
T9,2026-01-02T17:00:00Z,EURJPY,buy,0.05,160.20,159.20,161.20,0.0
T1,2026-01-02T09:00:00Z,EURUSD,buy,0.10,1.0800,1.0700,1.0900,0.0
CSV
RESP=$(curl -s -o /tmp/wave5_521.body -w "%{http_code}" \
  -X POST "${AUTH[@]}" \
  -F "file=@$TMP_CSV;type=text/csv" \
  -F "accountId=$ACCOUNT_ID" \
  "$API/api/imports/csv")
if [ "$RESP" = "202" ]; then
  JOB_ID=$(jq -r '.importJobId // empty' < /tmp/wave5_521.body 2>/dev/null)
  if [ -n "$JOB_ID" ]; then
    pass "5.2.1 CSV upload (jobId=$JOB_ID)"
  else
    fail "5.2.1 CSV upload" "202 but no importJobId in body"
  fi
else
  fail "5.2.1 CSV upload" "HTTP $RESP"
fi

# --------------------------------------------------------------------------
#  5.2.2 — GET /api/imports/{id}
# --------------------------------------------------------------------------
step "5.2.2 GET /api/imports/{id} (status poll)"
if [ -z "$JOB_ID" ]; then
  skip "5.2.2 status poll" "no jobId from 5.2.1"
else
  RESP=$(curl -s -o /tmp/wave5_522.body -w "%{http_code}" \
    "${AUTH[@]}" "$API/api/imports/$JOB_ID")
  if [ "$RESP" = "200" ]; then
    STATUS=$(jq -r '.status // empty' < /tmp/wave5_522.body 2>/dev/null)
    if [ -n "$STATUS" ]; then
      pass "5.2.2 status poll (status=$STATUS)"
    else
      fail "5.2.2 status poll" "200 but no status field"
    fi
  else
    fail "5.2.2 status poll" "HTTP $RESP"
  fi
fi

# --------------------------------------------------------------------------
#  5.2.3 — GET /api/ai/health (Ollama reachability)
# --------------------------------------------------------------------------
step "5.2.3 GET /api/ai/health (Ollama reachable?)"
if ! curl -sf http://localhost:11434/api/tags >/dev/null 2>&1; then
  skip "5.2.3 ai/health" "Ollama not running on :11434 (expected in CI)"
else
  RESP=$(curl -s -o /tmp/wave5_523.body -w "%{http_code}" \
    "${AUTH[@]}" "$API/api/ai/health")
  if [ "$RESP" = "200" ]; then
    STATUS=$(jq -r '.status // empty' < /tmp/wave5_523.body 2>/dev/null)
    if [ "$STATUS" = "ok" ]; then
      pass "5.2.3 ai/health (Ollama up)"
    else
      fail "5.2.3 ai/health" "200 but status != ok ($STATUS)"
    fi
  else
    fail "5.2.3 ai/health" "HTTP $RESP"
  fi
fi

# --------------------------------------------------------------------------
#  5.2.4 — POST /api/ai/risk-advice (advisory round-trip)
# --------------------------------------------------------------------------
step "5.2.4 POST /api/ai/risk-advice (advisory)"
if ! curl -sf http://localhost:11434/api/tags >/dev/null 2>&1; then
  skip "5.2.4 risk-advice" "Ollama not running on :11434 (expected in CI)"
else
  RESP=$(curl -s -o /tmp/wave5_524.body -w "%{http_code}" \
    -X POST "${AUTH[@]}" -H "Content-Type: application/json" \
    -d '{"symbol":"EURUSD","direction":"buy","volume":0.1,"volumeCurrency":"USD","entryPrice":1.08,"stopLoss":1.07,"riskRewardAtEntry":1.0,"setupQuality":"low"}' \
    "$API/api/ai/risk-advice")
  if [ "$RESP" = "200" ]; then
    ACTION=$(jq -r '.action // empty' < /tmp/wave5_524.body 2>/dev/null)
    if [ "$ACTION" = "warning" ] || [ "$ACTION" = "block" ] || [ "$ACTION" = "allow" ]; then
      pass "5.2.4 risk-advice (action=$ACTION)"
    else
      fail "5.2.4 risk-advice" "200 but action invalid: $ACTION"
    fi
  else
    fail "5.2.4 risk-advice" "HTTP $RESP"
  fi
fi

# --------------------------------------------------------------------------
#  5.2.5 — GET /api/coaching/prompts (rule-based prompts)
# --------------------------------------------------------------------------
step "5.2.5 GET /api/coaching/prompts (rule-based list)"
RESP=$(curl -s -o /tmp/wave5_525.body -w "%{http_code}" \
  "${AUTH[@]}" "$API/api/coaching/prompts")
if [ "$RESP" = "200" ]; then
  # Prompts may be an empty list for a brand-new user. Both [] and [...] are valid.
  if jq -e 'type == "array"' < /tmp/wave5_525.body >/dev/null 2>&1; then
    pass "5.2.5 coaching/prompts (array, count=$(jq 'length' < /tmp/wave5_525.body))"
  else
    fail "5.2.5 coaching/prompts" "200 but body is not an array"
  fi
else
  fail "5.2.5 coaching/prompts" "HTTP $RESP"
fi

# --------------------------------------------------------------------------
#  Summary
# --------------------------------------------------------------------------
echo
echo "── Summary ──"
TOTAL=$((PASS + FAIL + SKIP))
echo "  Total: $TOTAL  |  Pass: $PASS  |  Fail: $FAIL  |  Skip: $SKIP"
echo
if [ "$FAIL" -gt 0 ]; then
  echo "FAILURES:"
  printf '  %s\n' "${RESULTS[@]}" | grep '^FAIL' || true
  exit 1
fi
echo "All non-skipped probes passed."
exit 0
