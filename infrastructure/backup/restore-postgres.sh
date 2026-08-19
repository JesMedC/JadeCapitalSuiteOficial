#!/usr/bin/env bash
# Wave 10.4 slice — Postgres restore from pg_dump backup.
#
# Usage: restore-postgres.sh <backup-file-name>
#
# Example: restore-postgres.sh postgres-20260819T020000Z.sql.gz
#
# Decrypts (if GPG-encrypted) and pipes into psql inside a single
# transaction so the restore is atomic. Stops the `api` service first
# to avoid write contention.
set -euo pipefail

if [ "$#" -lt 1 ]; then
    echo "Usage: $0 <backup-file-name>" >&2
    exit 2
fi

BACKUP_FILE="$1"
PG_HOST="${PG_HOST:-postgres}"
PG_USER="${PG_USER:-jade}"
PG_DB="${PG_DB:-jadecapital}"
MINIO_ENDPOINT="${MINIO_ENDPOINT:-http://minio:9000}"
WORK_DIR="${TMPDIR:-/tmp}"

echo "[$(date -Iseconds)] starting postgres restore from ${BACKUP_FILE}"

# Stop the API so it cannot write during restore.
if command -v docker >/dev/null 2>&1; then
    docker compose -f docker-compose.prod.yml stop api 2>/dev/null || true
fi

# Download from MinIO.
if command -v mc >/dev/null 2>&1; then
    mc alias set backups "$MINIO_ENDPOINT" \
        "${MINIO_ROOT_USER:-}" "${MINIO_ROOT_PASSWORD:-}" \
        2>/dev/null || true
    mc cp "backups/postgres/${BACKUP_FILE}" "${WORK_DIR}/${BACKUP_FILE}" \
        || { echo "[$(date -Iseconds)] mc download failed" >&2; exit 1; }
else
    echo "[$(date -Iseconds)] mc not installed — assuming ${BACKUP_FILE} already at ${WORK_DIR}/" >&2
fi

DUMP_PATH="${WORK_DIR}/${BACKUP_FILE}"
if [ ! -f "$DUMP_PATH" ]; then
    echo "[$(date -Iseconds)] backup file not found at ${DUMP_PATH}" >&2
    exit 1
fi

# Decrypt if GPG.
if [[ "$DUMP_PATH" == *.gpg ]] && command -v gpg >/dev/null 2>&1; then
    DECRYPTED="${DUMP_PATH%.gpg}"
    gpg --batch --yes --passphrase-file /run/secrets/backup_gpg_key \
        --decrypt "$DUMP_PATH" > "$DECRYPTED"
    DUMP_PATH="$DECRYPTED"
fi

# Decompress if gzipped.
if [[ "$DUMP_PATH" == *.gz ]]; then
    DECOMPRESSED="${DUMP_PATH%.gz}"
    gunzip -c "$DUMP_PATH" > "$DECOMPRESSED"
    DUMP_PATH="$DECOMPRESSED"
fi

# Apply.
PGPASSWORD="$(cat /run/secrets/postgres_password 2>/dev/null || echo "${POSTGRES_PASSWORD:-}")" \
    psql -h "$PG_HOST" -U "$PG_USER" -d "$PG_DB" \
        --single-transaction --variable ON_ERROR_STOP=1 \
        -f "$DUMP_PATH"

echo "[$(date -Iseconds)] postgres restore complete"

# Restart the API.
if command -v docker >/dev/null 2>&1; then
    docker compose -f docker-compose.prod.yml start api 2>/dev/null || true
fi