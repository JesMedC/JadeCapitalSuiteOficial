#!/usr/bin/env bash
set -euo pipefail
umask 077

if [[ ! -r "${PGPASSWORD_FILE:?PGPASSWORD_FILE is required}" ]]; then
    printf 'PostgreSQL password secret is unreadable\n' >&2
    exit 1
fi
export PGPASSWORD="$(<"$PGPASSWORD_FILE")"
exec /usr/local/bin/walg-env backup-push "${PGDATA:?PGDATA is required}"
