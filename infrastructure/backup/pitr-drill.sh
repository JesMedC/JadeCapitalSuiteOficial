#!/usr/bin/env bash
set -euo pipefail
umask 077
source /usr/local/lib/pitr-contract.sh

readonly ARCHIVE_DEADLINE_SECONDS="${ARCHIVE_DEADLINE_SECONDS:-60}"
readonly MAX_RPO_SECONDS=3600
readonly SOURCE_PGDATA=/var/lib/postgresql/data/pgdata
readonly RESTORE_PGDATA=/restore-data
readonly RESTORE_PORT=55432

fail() { printf 'PITR drill failed: %s\n' "$*" >&2; exit 1; }
validate_archive_deadline "$ARCHIVE_DEADLINE_SECONDS" || fail 'archive deadline must be between 1 and 3600 seconds'
validate_restore_isolation "$SOURCE_PGDATA" "$RESTORE_PGDATA" || fail 'in-place restore is forbidden'
[[ -r "${PGPASSWORD_FILE:?PGPASSWORD_FILE is required}" ]] || fail 'PostgreSQL password secret is unreadable'
export PGPASSWORD="$(<"$PGPASSWORD_FILE")"
export PGHOST=source PGPORT=5432 PGUSER=pitr PGDATABASE=pitr

source_sql() { psql -v ON_ERROR_STOP=1 -Atqc "$1"; }
for _ in $(seq 1 60); do
    source_sql 'SELECT 1' >/dev/null 2>&1 && break
    sleep 1
done
source_sql 'SELECT 1' >/dev/null 2>&1 || fail 'source PostgreSQL did not become ready'

source_sql "CREATE TABLE IF NOT EXISTS public.pitr_markers(label text PRIMARY KEY, created_at timestamptz NOT NULL); TRUNCATE public.pitr_markers;"
/usr/local/bin/walg-env backup-push "$SOURCE_PGDATA"
BACKUPS=$(/usr/local/bin/walg-env backup-list --json --detail)
jq -e 'type == "array" and length > 0 and all(.[];
    (.backup_name | type == "string")
    and ((.start_lsn | type) == "number" or (.start_lsn | type) == "string")
    and ((.finish_lsn | type) == "number" or (.finish_lsn | type) == "string"))' <<<"$BACKUPS" >/dev/null \
    || fail 'base backup catalog is missing or malformed'

A_TIME=$(source_sql "INSERT INTO public.pitr_markers VALUES ('A', clock_timestamp()) RETURNING created_at;")
IFS='|' read -r A_LSN A_SEGMENT <<<"$(source_sql "SELECT pg_current_wal_lsn(), pg_walfile_name(pg_current_wal_lsn())")"
A_TIMELINE=$((16#${A_SEGMENT:0:8}))
source_sql 'SELECT pg_switch_wal()' >/dev/null

wait_for_segment() {
    local segment=$1 timeline=$2 started=$SECONDS catalog
    while (( SECONDS - started < ARCHIVE_DEADLINE_SECONDS )); do
        if catalog=$(/usr/local/bin/walg-env wal-show --detailed-json 2>/dev/null) \
            && validate_archive_catalog "$catalog" "$timeline" "$segment"; then
            printf '%s' "$catalog"
            return 0
        fi
        sleep 1
    done
    fail "WAL catalog did not prove continuity through $segment before deadline: ${catalog:-unavailable}"
}

A_CATALOG=$(wait_for_segment "$A_SEGMENT" "$A_TIMELINE")
jq -e --arg segment "$A_SEGMENT" 'any(.[]; .start_segment <= $segment and .end_segment >= $segment)' <<<"$A_CATALOG" >/dev/null \
    || fail 'catalog does not cover marker A'
printf 'PROOF base_wal_coverage=passed base_count=%s a_lsn=%s a_segment=%s timeline=%s\n' \
    "$(jq 'length' <<<"$BACKUPS")" "$A_LSN" "$A_SEGMENT" "$A_TIMELINE"

TARGET_TIME=$(source_sql 'SELECT clock_timestamp()')
sleep 1
B_TIME=$(source_sql "INSERT INTO public.pitr_markers VALUES ('B', clock_timestamp()) RETURNING created_at;")
B_SEGMENT=$(source_sql "SELECT pg_walfile_name(pg_current_wal_lsn())")
source_sql 'SELECT pg_switch_wal()' >/dev/null
wait_for_segment "$B_SEGMENT" "$A_TIMELINE" >/dev/null
SOURCE_BEFORE=$(source_sql "SELECT md5(string_agg(label || ':' || created_at::text, ',' ORDER BY label)) FROM public.pitr_markers")

[[ -z "$(ls -A "$RESTORE_PGDATA")" ]] || fail 'restore target is not an empty PostgreSQL volume'
/usr/local/bin/walg-env backup-fetch "$RESTORE_PGDATA" LATEST
chown -R postgres:postgres "$RESTORE_PGDATA"
cat >> "$RESTORE_PGDATA/postgresql.auto.conf" <<EOF
restore_command = '/usr/local/bin/walg-env wal-fetch "%f" "%p"'
recovery_target_time = '$TARGET_TIME'
recovery_target_action = 'promote'
EOF
touch "$RESTORE_PGDATA/recovery.signal"
chown postgres:postgres "$RESTORE_PGDATA/postgresql.auto.conf" "$RESTORE_PGDATA/recovery.signal"
gosu postgres pg_ctl -D "$RESTORE_PGDATA" -o "-p $RESTORE_PORT -k /tmp -c listen_addresses=''" -w start >/dev/null
trap 'gosu postgres pg_ctl -D "$RESTORE_PGDATA" -m fast stop >/dev/null 2>&1 || true' EXIT

RESTORED_LABELS=$(gosu postgres psql -h /tmp -p "$RESTORE_PORT" -d pitr -Atqc "SELECT string_agg(label, ',' ORDER BY label) FROM public.pitr_markers")
[[ "$RESTORED_LABELS" == 'A' ]] || fail "expected only marker A, got: $RESTORED_LABELS"
SOURCE_AFTER=$(source_sql "SELECT md5(string_agg(label || ':' || created_at::text, ',' ORDER BY label)) FROM public.pitr_markers")
validate_source_unchanged "$SOURCE_BEFORE" "$SOURCE_AFTER" || fail 'source data changed during isolated restore'
RPO_SECONDS=$(gosu postgres psql -h /tmp -p "$RESTORE_PORT" -d pitr -Atqc \
    "SELECT floor(extract(epoch FROM ('$TARGET_TIME'::timestamptz - max(created_at))))::int FROM public.pitr_markers")
[[ "$RPO_SECONDS" =~ ^[0-9]+$ ]] && (( RPO_SECONDS <= MAX_RPO_SECONDS )) || fail "RPO exceeds $MAX_RPO_SECONDS seconds"

printf 'PROOF isolated_target=passed a_present=true b_absent=true source_unchanged=true target=%s b_time=%s\n' "$TARGET_TIME" "$B_TIME"
printf 'PROOF rpo=passed seconds=%s max_seconds=%s latest_recovered=%s cutoff=%s\n' "$RPO_SECONDS" "$MAX_RPO_SECONDS" "$A_TIME" "$TARGET_TIME"
printf 'PROOF archive_deadline=passed max_seconds=%s\n' "$MAX_RPO_SECONDS"
