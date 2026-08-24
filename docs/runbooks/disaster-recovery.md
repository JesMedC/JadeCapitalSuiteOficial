# Disaster recovery runbook

**Status**: Wave 10 slice 10.4 (operational hardening)
**Owner**: ops + on-call
**RPO target**: ≤ 1 hour (Postgres WAL archiving); ≤ 24 hours (Redis, MinIO)
**RTO target**: ≤ 4 hours (database corruption); ≤ 8 hours (full VM loss)

## PostgreSQL WAL-G policy

Production PostgreSQL runs WAL-G `wal-push` continuously with
`archive_timeout=3600s`. A one-hour archive timeout is the maximum permitted
RPO boundary, not a reason to wait before investigating a failed archive.
Daily logical dumps remain an independent fallback.

Create a physical base backup after configuring the off-host S3-compatible
target:

```bash
docker compose -f docker-compose.prod.yml --profile operations \
  run --rm postgres-base-backup
```

The operator MUST provision these mode-`0600` files outside source control:

- `infrastructure/secrets/backup_s3_access_key.txt`
- `infrastructure/secrets/backup_s3_secret_key.txt`
- `infrastructure/secrets/postgres_password.txt`

Set only non-secret routing values (`WALG_S3_PREFIX`, `WALG_S3_ENDPOINT`,
`WALG_S3_REGION`) in the environment. The wrappers read credentials from
Docker secrets and never print them. Verify every base backup and WAL range:

```bash
docker compose -f docker-compose.prod.yml --profile operations run --rm \
  --entrypoint /usr/local/bin/walg-env postgres-base-backup backup-list --json --detail
docker compose -f docker-compose.prod.yml --profile operations run --rm \
  --entrypoint /usr/local/bin/walg-env postgres-base-backup wal-show --detailed-json
```

Treat missing/malformed catalogs, a non-`OK` timeline, missing segments, or a
range that does not include the incident marker as **no usable backup**.

## Isolated PITR drill

Run the deterministic local rehearsal before production recovery changes:

```bash
bash scripts/test-pitr.sh
```

The drill creates isolated source, archive, and empty restore volumes. It takes
a WAL-G base backup, writes marker A, proves an unbroken same-timeline catalog
through A, records target T, writes B, and restores into the separate cluster.
Success requires A present, B absent, an unchanged source-data fingerprint,
and measured RPO ≤3600 seconds. The generated password is held in a temporary
mode-`0600` file and deleted during teardown.

For an incident, NEVER run `backup-fetch` over the source `PGDATA`. Provision a
new empty volume/network, fetch the selected base there, configure
`restore_command` with `wal-g wal-fetch`, set an approved
`recovery_target_time`, add `recovery.signal`, and start only the isolated
cluster. Promote or cut over only after application-level validation.

### WAL-G rollback rehearsal

1. Run `bash scripts/test-pitr.sh` and retain its proof output.
2. Confirm teardown removed only the isolated drill volumes; production backup
   objects and daily dumps remain retained.
3. To roll back WAL-G configuration, stop writers, preserve the archive and
   latest verified base backup, restore the prior Postgres image/settings, and
   restart logical dumps before resuming writes.
4. Never delete WAL-G objects or restore into the source as part of rollback.

## Scenarios

### Database corruption (single-service failure)

1. **Stop the API** to prevent concurrent writes during restore:
   ```bash
   docker compose -f docker-compose.prod.yml stop api
   ```
2. **List available Postgres backups**:
   ```bash
   mc ls backups/postgres/
   ```
3. **Restore the latest dump**:
   ```bash
   bash infrastructure/backup/restore-postgres.sh postgres-<TIMESTAMP>.sql.gz
   ```
   The script decrypts (if GPG), decompresses, and applies via
   `psql --single-transaction --variable ON_ERROR_STOP=1`.
4. **Verify data integrity**:
   ```bash
   docker compose -f docker-compose.prod.yml exec postgres \
       psql -U jade -d jadecapital -c "SELECT count(*) FROM trading.trades;"
   docker compose -f docker-compose.prod.yml exec postgres \
       psql -U jade -d jadecapital -c "SELECT count(*) FROM identity.users;"
   ```
5. **Restart the API**:
   ```bash
   docker compose -f docker-compose.prod.yml start api
   ```

**RTO**: ≤ 4 hours (most of the time is download + apply; large DBs ≈ 1 GB / minute)

### Redis cache loss

1. Redis is treated as **ephemeral cache** — loss is acceptable; downstream
   services rebuild their caches on demand.
2. If a recent snapshot is required for warmup:
   ```bash
   bash infrastructure/backup/restore-redis.sh redis-<TIMESTAMP>.rdb
   ```

**RTO**: ≤ 30 minutes (rebuild from Postgres)
**RPO**: up to 1 hour of cache state

### MinIO attachment loss

1. List the most recent mirror in the backup MinIO.
2. Restore the affected bucket:
   ```bash
   bash infrastructure/backup/restore-minio.sh jade-data
   ```
3. Restart any service that reads from MinIO so it re-resolves pre-signed URLs:
   ```bash
   docker compose -f docker-compose.prod.yml restart api
   ```

**RTO**: ≤ 2 hours (mirror download + API restart)
**RPO**: ≤ 24 hours (nightly mirror)

### Full VM loss (regional disaster)

1. **Provision a new VM** in a healthy region (same specs; minimum 8 vCPU +
   16 GB RAM + 200 GB SSD).
2. **Clone the repository**:
   ```bash
   git clone https://github.com/JesMedC/JadeCapitalSuiteOficial.git /opt/jade
   cd /opt/jade && git checkout <release-tag>
   ```
3. **Restore secrets** from off-VM storage (1Password / HashiCorp Vault):
   - `postgres_password.txt`
   - `minio_root_user.txt`, `minio_root_password.txt`
   - `jwt_access_token_secret.txt`, `jwt_refresh_token_secret.txt`
   - `mailgun_api_key.txt`, `stripe_api_key.txt`, `stripe_webhook_secret.txt`
   - `backup_gpg_key` (for backup decryption)
4. **Restore MinIO state** first (most stateless components depend on it):
   ```bash
   bash infrastructure/backup/restore-minio.sh jade-data
   ```
5. **Start the stack**:
   ```bash
   docker compose -f docker-compose.prod.yml up -d postgres redis minio
   ```
6. **Run the migration runner** (idempotent — safe to re-run):
   ```bash
   docker compose -f docker-compose.prod.yml up migrate
   ```
7. **Restore Postgres from latest dump**:
   ```bash
   bash infrastructure/backup/restore-postgres.sh postgres-<TIMESTAMP>.sql.gz
   ```
8. **Bring up the rest of the stack**:
   ```bash
   docker compose -f docker-compose.prod.yml up -d
   ```
9. **Update DNS A records** to point at the new VM's IP.
10. **Smoke-test critical endpoints**:
    ```bash
    curl -f https://<host>/health
    curl -f -X POST https://<host>/api/auth/login -d '{}' -H 'Content-Type: application/json'
    ```

**RTO**: ≤ 8 hours (provisioning + restore)
**RPO**: ≤ 24 hours for the logical dump fallback; ≤ 1 hour for verified WAL-G
base/WAL coverage.

## Backup retention policy

| Service   | Hot retention | Cold retention | Notes |
|-----------|---------------|----------------|-------|
| Postgres  | 7 daily       | 30 monthly     | `pg_dump` daily + continuous WAL-G; `archive_timeout=3600s` |
| Redis     | 7 daily       | —              | `BGSAVE` hourly (AOF provides sub-minute RPO) |
| MinIO     | 7 daily       | 90 daily       | `mc mirror --remove` nightly at 03:00 UTC |

Tunable via `BACKUP_RETENTION_DAYS` (Postgres) + `BACKUP_REDIS_RETENTION_HOURS`
(Redis) env vars.

## Backup verification (monthly)

The **monthly DR drill** verifies that backups are restorable. Schedule it on
the first Saturday of every month.

### Procedure

1. Run `bash scripts/test-pitr.sh` to prove WAL-G A/T/B recovery in isolated volumes.
2. Download the most recent production dump from MinIO as fallback evidence.
3. Run `infrastructure/backup/restore-postgres.sh` against a separate empty Postgres.
4. Verify row counts against the production dashboard's last-known totals:
   - `SELECT count(*) FROM trading.trades;`
   - `SELECT count(*) FROM identity.users;`
   - `SELECT count(*) FROM trading.trade_attachments;`
5. Capture the result in `docs/runbooks/disaster-recovery.md` §"Drill history".

### Drill history

| Date       | Tester | Result | Notes |
|------------|--------|--------|-------|
| YYYY-MM-DD | (pending) | — | First drill scheduled after Wave 10 merges. |

## Failure modes to watch

- **Backup cron silently failing**: alert on `mc ls backups/postgres/` returning
  zero files dated today (a missing backup is a missing backup).
- **GPG passphrase rotation**: rotating the backup GPG key requires re-encrypting
  the most recent dump; otherwise older dumps remain readable with the old key.
- **MinIO backup bucket out of space**: the `mc mirror --remove` will start
  pruning source-side deletions; if the backup bucket is smaller than the source,
  this is destructive — verify bucket size weekly.
- **Migration drift**: if `migrate.Dockerfile` runs successfully but new tables
  are missing, the most common cause is a non-idempotent migration sneaking in
  (e.g., `CREATE TABLE` without `IF NOT EXISTS`). The verification suite
  `tests/UnitTests/JadeCapital.Host.UnitTests/Backup/MigrationOrderTests.cs`
  guards against adding a non-idempotent migration.

## Related artifacts

- `infrastructure/backup/postgres-backup.sh` (daily pg_dump + mc cp)
- `infrastructure/backup/redis-backup.sh` (hourly BGSAVE + mc cp)
- `infrastructure/backup/minio-backup.sh` (nightly mc mirror --remove)
- `infrastructure/backup/restore-postgres.sh`
- `infrastructure/backup/pitr-drill.sh` and `pitr-contract.sh`
- `docker-compose.pitr.yml` (isolated WAL-G recovery harness)
- `infrastructure/backup/restore-redis.sh`
- `infrastructure/backup/restore-minio.sh`
- `infrastructure/postgres/migrate.Dockerfile` (order-agnostic apply)
- `docker-compose.prod.yml` `migrate:` service (mounts migrations/ into the
  runner container)
- `openspec/specs/backup-strategy/spec.md` (canonical RPO/RTO + retention
  requirements)
