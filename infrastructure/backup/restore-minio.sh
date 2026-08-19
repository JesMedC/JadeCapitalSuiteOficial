#!/usr/bin/env bash
# Wave 10.4 slice — MinIO restore via mc mirror.
#
# Usage: restore-minio.sh <bucket-name>
#
# Example: restore-minio.sh jade-data
#
# Mirrors the named bucket from the backup MinIO back into the source.
set -euo pipefail

if [ "$#" -lt 1 ]; then
    echo "Usage: $0 <bucket-name>" >&2
    exit 2
fi

BUCKET_NAME="$1"
SOURCE_ENDPOINT="${SOURCE_ENDPOINT:-http://minio:9000}"
BACKUP_ENDPOINT="${BACKUP_ENDPOINT:-${BACKUP_MINIO_ENDPOINT:-http://backup-minio:9000}}"

echo "[$(date -Iseconds)] starting minio restore of bucket ${BUCKET_NAME}"

if ! command -v mc >/dev/null 2>&1; then
    echo "[$(date -Iseconds)] mc not installed — aborting" >&2
    exit 1
fi

mc alias set source "$SOURCE_ENDPOINT" \
    "${MINIO_ROOT_USER:-}" "${MINIO_ROOT_PASSWORD:-}" \
    2>/dev/null
mc alias set backup "$BACKUP_ENDPOINT" \
    "${BACKUP_MINIO_ROOT_USER:-}" "${BACKUP_MINIO_ROOT_PASSWORD:-}" \
    2>/dev/null

# Mirror backup -> source. --overwrite ensures fresh data wins; --remove
# would prune keys not in the backup, which we deliberately AVOID for
# restore (we never want restore to delete data that's still in source).
mc mirror --overwrite "backup/${BUCKET_NAME}" "source/${BUCKET_NAME}" \
    || { echo "[$(date -Iseconds)] mc mirror failed" >&2; exit 1; }

echo "[$(date -Iseconds)] minio restore complete for ${BUCKET_NAME}"