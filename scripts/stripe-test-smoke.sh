#!/usr/bin/env bash
# Smoke test the Stripe checkout flow with test keys.
# Usage: Stripe__SecretKey=sk_test_xxx bash scripts/stripe-test-smoke.sh
set -euo pipefail

: "${Stripe__SecretKey:?Stripe__SecretKey env var required (sk_test_*)}"

echo "Stripe test-mode smoke test"
echo "Secret key prefix: ${Stripe__SecretKey:0:7}"

if [[ ! "$Stripe__SecretKey" =~ ^sk_test_ ]]; then
    echo "FAIL: secret key does not start with sk_test_"
    exit 1
fi

# Call Stripe API with test key (no auth required for this read)
response=$(curl -sS "https://api.stripe.com/v1/balance" -u "${Stripe__SecretKey}:" 2>&1)
if echo "$response" | grep -q "livemode.*false"; then
    echo "PASS: Stripe test-mode balance endpoint responded correctly"
else
    echo "FAIL: Stripe API did not respond as expected"
    echo "$response"
    exit 1
fi
