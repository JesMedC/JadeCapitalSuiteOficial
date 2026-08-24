#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)
COMPOSE_FILE="$ROOT_DIR/docker-compose.pitr.yml"
PROJECT_NAME="${COMPOSE_PROJECT_NAME:-wave13-pitr-$$}"
LOG_FILE=$(mktemp)
SECRET_FILE=$(mktemp)

cleanup() {
    docker compose -p "$PROJECT_NAME" -f "$COMPOSE_FILE" down -v --remove-orphans >/dev/null 2>&1 || true
    rm -f "$LOG_FILE" "$SECRET_FILE"
}
trap cleanup EXIT

chmod 600 "$SECRET_FILE"
printf 'pitr-%s\n' "$(openssl rand -hex 16)" > "$SECRET_FILE"
export PITR_POSTGRES_PASSWORD_FILE="$SECRET_FILE"

bash "$ROOT_DIR/scripts/test-pitr-contracts.sh"
docker compose -p "$PROJECT_NAME" -f "$COMPOSE_FILE" config --quiet
docker compose -p "$PROJECT_NAME" -f "$COMPOSE_FILE" \
    up --build --abort-on-container-exit --exit-code-from drill drill 2>&1 | tee "$LOG_FILE"

assert_proof() {
    local proof=$1
    if ! grep -Fq "$proof" "$LOG_FILE"; then
        printf 'missing PITR proof: %s\n' "$proof" >&2
        return 1
    fi
}

assert_proof 'PROOF base_wal_coverage=passed'
assert_proof 'PROOF isolated_target=passed a_present=true b_absent=true source_unchanged=true'
assert_proof 'PROOF rpo=passed seconds='
assert_proof 'PROOF archive_deadline=passed max_seconds=3600'

printf 'PITR scenarios: 3 passed\n'
