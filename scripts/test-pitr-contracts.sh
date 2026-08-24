#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)
source "$ROOT_DIR/infrastructure/backup/pitr-contract.sh"

expect_failure() {
    local name=$1
    shift
    if "$@" >/dev/null 2>&1; then
        printf 'expected fail-closed rejection: %s\n' "$name" >&2
        exit 1
    fi
}

GOOD='[{"id":1,"start_segment":"000000010000000000000003","end_segment":"000000010000000000000005","missing_segments":[],"status":"OK"}]'
GAP='[{"id":1,"start_segment":"000000010000000000000003","end_segment":"000000010000000000000005","missing_segments":["000000010000000000000004"],"status":"WARNING"}]'

validate_archive_catalog "$GOOD" 1 000000010000000000000004
expect_failure catalog-gap validate_archive_catalog "$GAP" 1 000000010000000000000004
expect_failure malformed-catalog validate_archive_catalog 'not-json' 1 000000010000000000000004
validate_archive_deadline 3600
expect_failure excessive-deadline validate_archive_deadline 3601
validate_restore_isolation /source /restore
expect_failure in-place-restore validate_restore_isolation /source /source
validate_source_unchanged abc abc
expect_failure source-mutation validate_source_unchanged abc def
expect_failure missing-storage env -u WALG_FILE_PREFIX -u WALG_S3_PREFIX \
    bash "$ROOT_DIR/infrastructure/backup/walg-env.sh" backup-list
expect_failure unreadable-operator-secret env WALG_FILE_PREFIX=/tmp/archive \
    AWS_ACCESS_KEY_ID_FILE=/does/not/exist bash "$ROOT_DIR/infrastructure/backup/walg-env.sh" backup-list

printf 'PITR fail-closed contracts: 9 passed\n'
