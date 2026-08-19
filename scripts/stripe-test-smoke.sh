#!/usr/bin/env bash
# Smoke test the Stripe checkout flow with test keys.
# Usage: STRIPE__ApiKey=sk_test_xxx bash scripts/stripe-test-smoke.sh
set -euo pipefail

: "${STRIPE__ApiKey:?STRIPE__ApiKey env var required (sk_test_*)}"

echo "Stripe test-mode smoke test"
echo "API key prefix: ${STRIPE__ApiKey:0:7}"

if [[ ! "$STRIPE__ApiKey" =~ ^sk_test_ ]]; then
    echo "FAIL: API key does not start with sk_test_"
    exit 1
fi

# Call Stripe API with test key (no auth required for this read)
response=$(curl -sS "https://api.stripe.com/v1/balance" -u "${STRIPE__ApiKey}:" 2>&1)
if echo "$response" | grep -q "livemode.*false"; then
    echo "PASS: Stripe test-mode balance endpoint responded correctly"
else
    echo "FAIL: Stripe API did not respond as expected"
    echo "$response"
    exit 1
fi
