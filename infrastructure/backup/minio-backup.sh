#!/usr/bin/env bash
# Wave 10.4 slice — MinIO backup (mc mirror nightly).
#
# Schedule via cron: 0 3 * * * root /opt/jade/scripts/backup-minio.sh
# RPO: 24h (full nightly mirror). Mirrors every bucket from the source
# MinIO to the backup MinIO. Uses --remove to prune keys deleted from
# the source since the last run.
set -euo pipefail

SOURCE_ENDPOINT="${SOURCE_ENDPOINT:-http://minio:9000}"
BACKUP_ENDPOINT="${BACKUP_ENDPOINT:-${BACKUP_MINIO_ENDPOINT:-http://backup-minio:9000}}"
BACKUP_DIR="${BACKUP_DIR:-/tmp/minio-mirror}"
TIMESTAMP=$(date -u +%Y%m%dT%H%M%SZ)

echo "[$(date -Iseconds)] starting minio mirror backup"

if ! command -v mc >/dev/null 2>&1; then
    echo "[$(date -Iseconds)] mc not installed — aborting" >&2
    exit 1
fi

mc alias set source "$SOURCE_ENDPOINT" \
    "${MINIO_ROOT_USER:-}" "${MINIO_ROOT_PASSWORD:-}" \
    2>/dev/null

if mc alias set backup "$BACKUP_ENDPOINT" \
        "${BACKUP_MINIO_ROOT_USER:-}" "${BACKUP_MINIO_ROOT_PASSWORD:-}" \
        2>/dev/null; then
    echo "[$(date -Iseconds)] configured backup alias"
else
    echo "[$(date -Iseconds)] backup alias unreachable — falling back to local target"
    mkdir -p "$BACKUP_DIR"
fi

# Mirror every source bucket.
buckets=$(mc ls source/ 2>/dev/null | awk '{print $NF}')
if [ -z "$buckets" ]; then
    echo "[$(date -Iseconds)] no buckets found in source"
    exit 0
fi

for bucket in $buckets; do
    bucket_name=$(echo "$bucket" | tr -d '/')
    if [ -z "$bucket_name" ]; then continue; fi
    echo "[$(date -Iseconds)] mirroring bucket: $bucket_name"
    if mc alias ls 2>/dev/null | grep -q "^backup\b"; then
        mc mirror --remove --overwrite \
            "source/$bucket_name" "backup/$bucket_name" 2>/dev/null || \
            echo "[$(date -Iseconds)] mirror of $bucket_name failed"
    else
        mkdir -p "${BACKUP_DIR}/${bucket_name}"
        mc mirror --remove --overwrite \
            "source/$bucket_name" "${BACKUP_DIR}/${bucket_name}" 2>/dev/null || \
            echo "[$(date -Iseconds)] local mirror of $bucket_name failed"
    fi
done

echo "[$(date -Iseconds)] minio mirror complete (${TIMESTAMP})"