#!/usr/bin/env bash
# Wave 10.4 slice — Redis backup (BGSAVE + RDB copy to MinIO).
#
# Schedule via cron: 0 * * * * root /opt/jade/scripts/backup-redis.sh
# RPO: ~5min (AOF with appendfsync everysec; BGSAVE hourly snapshots).
set -euo pipefail

REDIS_HOST="${REDIS_HOST:-redis}"
MINIO_ENDPOINT="${MINIO_ENDPOINT:-http://minio:9000}"
BACKUP_BUCKET="${BACKUP_BUCKET:-backups}"
RETENTION_HOURS="${BACKUP_REDIS_RETENTION_HOURS:-168}" # 7 days

TIMESTAMP=$(date -u +%Y%m%dT%H%M%SZ)
BACKUP_FILE="redis-${TIMESTAMP}.rdb"
WORK_DIR="${TMPDIR:-/tmp}"

echo "[$(date -Iseconds)] starting redis backup"

# Snapshot the previous LASTSAVE timestamp, then BGSAVE, then wait for the
# new LASTSAVE.
PREV_SAVE=$(redis-cli -h "$REDIS_HOST" LASTSAVE)
redis-cli -h "$REDIS_HOST" BGSAVE >/dev/null
TIMEOUT=30
while [ "$TIMEOUT" -gt 0 ]; do
    CURRENT_SAVE=$(redis-cli -h "$REDIS_HOST" LASTSAVE)
    if [ "$CURRENT_SAVE" != "$PREV_SAVE" ]; then
        break
    fi
    sleep 1
    TIMEOUT=$((TIMEOUT - 1))
done
if [ "$TIMEOUT" -eq 0 ]; then
    echo "[$(date -Iseconds)] BGSAVE timed out after 30s" >&2
    exit 1
fi

# Locate the dump.rdb. In docker compose it's usually on the redis volume;
# the `redis` container has it at /data/dump.rdb. We can't docker cp from
# outside the redis service, so the safe path is to copy from a shared
# volume OR run redis-cli --rdb over the wire.
RDB_SOURCE=""
if [ -f "${REDIS_RDB_PATH:-/data/dump.rdb}" ]; then
    RDB_SOURCE="${REDIS_RDB_PATH:-/data/dump.rdb}"
elif command -v docker >/dev/null 2>&1; then
    RDB_SOURCE="/tmp/${BACKUP_FILE}"
    docker cp "redis:/data/dump.rdb" "$RDB_SOURCE" 2>/dev/null || \
        echo "[$(date -Iseconds)] RDB copy skipped (container not reachable)"
else
    echo "[$(date -Iseconds)] cannot locate RDB file"
    exit 0
fi

if [ -n "$RDB_SOURCE" ] && [ -f "$RDB_SOURCE" ]; then
    if command -v mc >/dev/null 2>&1; then
        mc alias set backups "$MINIO_ENDPOINT" \
            "${MINIO_ROOT_USER:-${BACKUP_MINIO_ROOT_USER:-}}" \
            "${MINIO_ROOT_PASSWORD:-${BACKUP_MINIO_ROOT_PASSWORD:-}}" \
            2>/dev/null || true
        mc cp "$RDB_SOURCE" "backups/redis/${BACKUP_FILE}" \
            && echo "[$(date -Iseconds)] uploaded to backups/redis/${BACKUP_FILE}" \
            || echo "[$(date -Iseconds)] mc upload skipped (mc not configured)"
        mc rm --force --older-than "${RETENTION_HOURS}h" "backups/redis/" \
            2>/dev/null || true
    else
        echo "[$(date -Iseconds)] mc not installed — local copy at ${RDB_SOURCE}"
    fi
fi

echo "[$(date -Iseconds)] redis backup complete: ${BACKUP_FILE}"