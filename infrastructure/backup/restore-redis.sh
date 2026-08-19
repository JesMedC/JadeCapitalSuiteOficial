#!/usr/bin/env bash
# Wave 10.4 slice — Redis restore from RDB backup.
#
# Usage: restore-redis.sh <backup-file-name>
#
# Example: restore-redis.sh redis-20260819T120000Z.rdb
#
# Stops redis, copies the RDB into the data directory, then starts redis.
set -euo pipefail

if [ "$#" -lt 1 ]; then
    echo "Usage: $0 <backup-file-name>" >&2
    exit 2
fi

BACKUP_FILE="$1"
REDIS_HOST="${REDIS_HOST:-redis}"
MINIO_ENDPOINT="${MINIO_ENDPOINT:-http://minio:9000}"
WORK_DIR="${TMPDIR:-/tmp}"

echo "[$(date -Iseconds)] starting redis restore from ${BACKUP_FILE}"

# Download from MinIO.
if command -v mc >/dev/null 2>&1; then
    mc alias set backups "$MINIO_ENDPOINT" \
        "${MINIO_ROOT_USER:-}" "${MINIO_ROOT_PASSWORD:-}" \
        2>/dev/null || true
    mc cp "backups/redis/${BACKUP_FILE}" "${WORK_DIR}/${BACKUP_FILE}" \
        || { echo "[$(date -Iseconds)] mc download failed" >&2; exit 1; }
fi

LOCAL_PATH="${WORK_DIR}/${BACKUP_FILE}"
if [ ! -f "$LOCAL_PATH" ]; then
    echo "[$(date -Iseconds)] backup file not found at ${LOCAL_PATH}" >&2
    exit 1
fi

# Copy RDB into the redis container.
if command -v docker >/dev/null 2>&1; then
    docker compose -f docker-compose.prod.yml stop redis 2>/dev/null || true
    docker cp "$LOCAL_PATH" "redis:/data/dump.rdb" \
        || { echo "[$(date -Iseconds)] docker cp failed" >&2; exit 1; }
    docker compose -f docker-compose.prod.yml start redis 2>/dev/null || true
else
    echo "[$(date -Iseconds)] docker not available — manual restore required" >&2
    echo "Copy $LOCAL_PATH to the redis container's /data/dump.rdb" >&2
    exit 1
fi

echo "[$(date -Iseconds)] redis restore complete"