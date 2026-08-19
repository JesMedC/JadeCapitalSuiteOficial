# Backup Strategy Specification

**Change**: 2026-08-18-wave10-v1-readiness
**Wave**: 10 (v1 readiness)
**Slice**: 10.4 — Backups + migration-order fix
**Status**: NEW spec (no prior canonical)
**Strict TDD**: ACTIVE — every Requirement + Scenario here must be covered by tests in slice 10.4

## Purpose

Define the data-loss survival contract for Jade Capital Suite. The system MUST backup Postgres (via `pg_dump` daily + WAL archiving via `wal-g` for PITR with RPO ≤ 1 hour), Redis (via `BGSAVE` hourly, RPO ≤ 5 minutes), and MinIO (via `mc mirror` nightly, RPO ≤ 24 hours for trade attachments). Off-site target for v1.0 is self-hosted MinIO in a separate DR stack — AWS S3 / Backblaze B2 are v1.1+ candidates. Restore scripts MUST be tested against a Testcontainers Postgres + Redis stack, and the procedure MUST be documented in `docs/runbooks/backup-recovery.md` + `docs/runbooks/disaster-recovery.md`. The slice ALSO closes the long-standing Wave 4e.D1 migration-order bug: 38 SQL files use non-consecutive numbering (0009, 0011, 0012, ..., 0018, 0019, ..., 0026_NOT_NULL, 20260806_0001, ..., 20260814_0008) — Wave 10 renumbers them consecutively and rewrites `migrate.Dockerfile` to be order-agnostic.

## ADDED Requirements

### Requirement: Postgres backups — pg_dump daily + WAL archiving (RPO ≤ 1h)

The system MUST define `scripts/backup-postgres.sh` that runs `pg_dump --format=custom --compress=9` against the prod database, encrypts the dump with `gpg --symmetric --cipher-algo AES256` (key from `/run/secrets/backup_gpg_key`), and uploads to the MinIO `backups` bucket via `mc cp`. A cron MUST invoke the script daily at 02:00 UTC. The system MUST configure `wal-g` for continuous WAL archiving with a 1-hour RPO target (`archive_timeout = 3600` in `postgresql.conf`). Retention MUST be 30 days hot + 365 days cold (configurable via `BACKUP_RETENTION_DAYS` env var).

#### Scenario: pg_dump cron runs daily at 02:00 UTC + uploads to MinIO

- GIVEN the backup cron is installed via `/etc/cron.d/jade-backup` with `0 2 * * * root /opt/jade/scripts/backup-postgres.sh`
- WHEN 02:00 UTC elapses
- THEN `backup-postgres.sh` MUST run successfully
- AND the dump MUST appear in the MinIO `backups` bucket with the date-stamped key `postgres/{YYYY-MM-DD}.dump.gpg`

#### Scenario: WAL archiving captures continuous changes (RPO ≤ 1h)

- GIVEN `wal-g` is configured with `archive_timeout = 3600`
- WHEN a write commits to Postgres between two `pg_dump` cycles
- THEN the WAL segment MUST be shipped to MinIO within `archive_timeout` seconds
- AND a PITR restore to any second within the last 24h MUST succeed

### Requirement: Redis persistence — AOF + BGSAVE (RPO ≤ 5min)

The system MUST configure Redis with `--appendonly yes` + `--appendfsync everysec` (already in `docker-compose.yml:26`) and a `BGSAVE` cron. The `scripts/backup-redis.sh` script MUST trigger `BGSAVE`, wait for completion (`LASTSAVE` to change), copy `/data/dump.rdb` to the MinIO `backups` bucket, and rotate the previous 24 hourly snapshots.

#### Scenario: BGSAVE runs hourly + uploads RDB to MinIO

- GIVEN the `redis-backup` cron runs at `0 * * * *`
- WHEN the cron fires
- THEN `BGSAVE` MUST succeed (verified by `LASTSAVE` timestamp increment)
- AND `dump.rdb` MUST appear in `s3://jade-backups/redis/{YYYY-MM-DD-HH}.rdb.gpg`

### Requirement: MinIO backups — mc mirror nightly (RPO ≤ 24h for attachments)

The system MUST define `scripts/backup-minio.sh` that uses `mc mirror --remove` to replicate the `jade-data` bucket (trade attachments) to `jade-backups/minio/{date}/`. The cron MUST run nightly at 03:00 UTC. Retention MUST be 7 days local + 90 days cold.

#### Scenario: mc mirror cron replicates minio-data volume to MinIO

- GIVEN the `minio-backup` cron runs at `0 3 * * *`
- WHEN 03:00 UTC elapses
- THEN `mc mirror --remove` MUST replicate every object newer than the last run
- AND the backup bucket MUST contain the same key prefixes as the source
- AND `--remove` MUST prune keys deleted from the source since the last backup

### Requirement: Restore procedure documented + tested annually

The system MUST define `scripts/restore-postgres.sh` that accepts a date argument, downloads the matching dump + WAL segments from MinIO, decrypts, runs `pg_restore --clean --if-exists` against a clean DB, and applies WAL up to the target PITR timestamp. The procedure MUST be exercised annually as a DR drill with results captured in `docs/runbooks/backup-recovery.md` §"DR drill history".

#### Scenario: restore script restores Postgres from latest pg_dump + replays WAL

- GIVEN the latest dump is `postgres/2026-08-19.dump.gpg` and WAL segments are in `postgres/wal/`
- WHEN `scripts/restore-postgres.sh --target "2026-08-19T12:00:00Z"` runs against an empty Postgres
- THEN the script MUST decrypt + pg_restore + replay WAL
- AND a SELECT against the restored DB MUST return data committed at `12:00:00Z`

#### Scenario: restore procedure tested in DR drill (annually)

- GIVEN the calendar hits the anniversary of the last DR drill
- WHEN `docs/runbooks/backup-recovery.md` §"DR drill" is followed
- THEN a Testcontainers Postgres MUST be restored from the prod dump
- AND the drill results MUST be logged to `docs/runbooks/backup-recovery.md` §"Drill history"

### Requirement: Wave 4e.D1 migration-order fix

The system MUST renumber the 38 SQL files in `infrastructure/postgres/migrations/` to use consecutive integers (e.g., `0001_*.sql` ... `0038_*.sql`) regardless of the original semantic ordering. The `migrate.Dockerfile` MUST be rewritten to apply migrations in `ls *.sql | sort` order AND verify dependency order via a runtime check (each migration declares a `requires:` comment header listing prior migration IDs). A verifier script `scripts/verify-migration-order.sh` MUST run Testcontainers Postgres + `docker-entrypoint-initdb.d` against a fresh DB and assert all 38 migrations apply without errors in dependency order.

#### Scenario: migrations 0030+ renumbered consecutively

- GIVEN the renumbering has landed
- WHEN `ls infrastructure/postgres/migrations/` runs
- THEN the list MUST be `0001_*.sql` through `0038_*.sql` with no gaps
- AND no file MUST be named `2026MMDD_*` or `0026_NOT_NULL_*` (the legacy formats are gone)

#### Scenario: migrate.Dockerfile works regardless of order applied

- GIVEN a fresh Postgres volume (empty `PG_DATA`)
- WHEN `migrate.Dockerfile` runs against it
- THEN all 38 migrations MUST apply in `ls | sort` order
- AND the `requires:` comment header check MUST pass (or log a warning if a dependency is missing)
- AND `psql -c '\dt' MUST list all tables (no missing migrations)

#### Scenario: Testcontainers fresh-DB verifier passes

- GIVEN `scripts/verify-migration-order.sh` runs in CI (slice 10.1)
- WHEN the script spins up a fresh Testcontainers Postgres + applies all 38 migrations
- THEN the script MUST exit 0
- AND `psql -c 'SELECT COUNT(*) FROM information_schema.tables'` MUST match the expected count

## Cross-references

- Closes gaps A6 (no DB backup story), B26 (migration-order fix), B16 partial (DR runbooks)
- Related Wave 9 spec: `openspec/specs/audit-retention-policy/spec.md` (90-day `audit.events` retention runs independently from these backups)
- Related Wave 10 spec: `openspec/changes/2026-08-18-wave10-v1-readiness/specs/deployment-automation/spec.md` (prod compose defines the volumes these scripts read)
- Source: `openspec/changes/2026-08-18-wave10-v1-readiness/explore.md` §A6, §B26

## Out of scope

- Off-site to AWS S3 / Backblaze B2 — v1.0 uses self-hosted MinIO in a separate DR stack (gap G-B7/G-B8 deferred to Wave 11+ for full automation)
- Postgres TDE / encryption at rest (Wave 11+, gap G-C6)
- Audit log partitioning by month (Wave 11+, gap G-C7)
- Backup monitoring + alerting (PagerDuty for missed backups — ops decision, Wave 11+)
- Continuous WAL streaming to a second region (multi-region, gap G-C1)
