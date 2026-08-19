# Disaster recovery runbook

**Status**: Wave 10 slice 10.4 (operational hardening)
**Owner**: ops + on-call
**RPO target**: ≤ 1 hour (Postgres WAL archiving); ≤ 24 hours (Redis, MinIO)
**RTO target**: ≤ 4 hours (database corruption); ≤ 8 hours (full VM loss)

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
**RPO**: ≤ 24 hours for Postgres dump; ≤ 1 hour for WAL (if WAL archiving is
configured via `wal-g` to an offsite target)

## Backup retention policy

| Service   | Hot retention | Cold retention | Notes |
|-----------|---------------|----------------|-------|
| Postgres  | 7 daily       | 30 monthly     | `pg_dump` daily at 02:00 UTC + WAL archiving hourly |
| Redis     | 7 daily       | —              | `BGSAVE` hourly (AOF provides sub-minute RPO) |
| MinIO     | 7 daily       | 90 daily       | `mc mirror --remove` nightly at 03:00 UTC |

Tunable via `BACKUP_RETENTION_DAYS` (Postgres) + `BACKUP_REDIS_RETENTION_HOURS`
(Redis) env vars.

## Backup verification (monthly)

The **monthly DR drill** verifies that backups are restorable. Schedule it on
the first Saturday of every month.

### Procedure

1. Spin up a Testcontainers Postgres + Redis + MinIO stack locally.
2. Download the most recent production dump from MinIO.
3. Run `infrastructure/backup/restore-postgres.sh` against the empty Postgres.
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
- `infrastructure/backup/restore-redis.sh`
- `infrastructure/backup/restore-minio.sh`
- `infrastructure/postgres/migrate.Dockerfile` (order-agnostic apply)
- `docker-compose.prod.yml` `migrate:` service (mounts migrations/ into the
  runner container)
- `openspec/specs/backup-strategy/spec.md` (canonical RPO/RTO + retention
  requirements)