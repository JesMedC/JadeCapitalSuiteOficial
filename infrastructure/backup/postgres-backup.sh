#!/usr/bin/env bash
# Wave 10.4 slice — Postgres backup (pg_dump + MinIO upload).
#
# Schedule via cron: 0 2 * * * root /opt/jade/scripts/backup-postgres.sh
# RPO: 24h (full dump). Compressed with gzip; encrypted with GPG if
# /run/secrets/backup_gpg_key is present; uploaded to the MinIO `backups`
# bucket via `mc cp`.
set -euo pipefail

PG_HOST="${PG_HOST:-postgres}"
PG_USER="${PG_USER:-jade}"
PG_DB="${PG_DB:-jadecapital}"
MINIO_ENDPOINT="${MINIO_ENDPOINT:-http://minio:9000}"
BACKUP_BUCKET="${BACKUP_BUCKET:-backups}"
RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-7}"

TIMESTAMP=$(date -u +%Y%m%dT%H%M%SZ)
BACKUP_FILE="postgres-${TIMESTAMP}.sql.gz"
WORK_DIR="${TMPDIR:-/tmp}"

echo "[$(date -Iseconds)] starting postgres backup"

# Run pg_dump and compress. --no-owner + --clean + --if-exists make the dump
# portable and re-applicable.
PGPASSWORD="$(cat /run/secrets/postgres_password 2>/dev/null || echo "${POSTGRES_PASSWORD:-}")" \
    pg_dump -h "$PG_HOST" -U "$PG_USER" -d "$PG_DB" \
        --no-owner --clean --if-exists \
    | gzip > "${WORK_DIR}/${BACKUP_FILE}"

# Optional GPG encryption.
if [ -f /run/secrets/backup_gpg_key ]; then
    echo "[$(date -Iseconds)] encrypting with GPG"
    GPG_TMP="${WORK_DIR}/${BACKUP_FILE}.gpg"
    gpg --batch --yes --symmetric --cipher-algo AES256 \
        --passphrase-file /run/secrets/backup_gpg_key \
        --output "$GPG_TMP" "${WORK_DIR}/${BACKUP_FILE}"
    mv "$GPG_TMP" "${WORK_DIR}/${BACKUP_FILE}"
fi

# Upload to MinIO (mc).
if command -v mc >/dev/null 2>&1; then
    mc alias set backups "$MINIO_ENDPOINT" \
        "${MINIO_ROOT_USER:-${BACKUP_MINIO_ROOT_USER:-}}" \
        "${MINIO_ROOT_PASSWORD:-${BACKUP_MINIO_ROOT_PASSWORD:-}}" \
        2>/dev/null || true
    mc cp "${WORK_DIR}/${BACKUP_FILE}" "backups/postgres/${BACKUP_FILE}" \
        && echo "[$(date -Iseconds)] uploaded to backups/postgres/${BACKUP_FILE}" \
        || echo "[$(date -Iseconds)] mc upload skipped (mc not configured)"
    # Retention prune.
    mc rm --force --older-than "${RETENTION_DAYS}d" "backups/postgres/" \
        2>/dev/null || true
else
    echo "[$(date -Iseconds)] mc not installed — skipping upload"
fi

echo "[$(date -Iseconds)] backup complete: ${BACKUP_FILE}"