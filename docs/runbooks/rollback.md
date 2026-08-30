# Production rollback runbook

> Wave 10 slice 10.2 — author: sdd-apply (PR #38).
> Companion to `docs/runbooks/deployment.md`.

## 1. Roll back a deploy (container-level)

Goal: undo a bad `git pull + docker compose build + up` without
touching the database.

```bash
cd /srv/jadecapital
git log --oneline -10                           # find the previous good commit
git checkout <previous-commit-sha>              # or revert via PR

docker compose -f docker-compose.prod.yml build api frontend
docker compose -f docker-compose.prod.yml up -d --no-deps api frontend
```

Verify:

```bash
docker compose -f docker-compose.prod.yml ps
./scripts/wave5-smoke.sh https://jadecapital.example.com
```

Rollback boundary: only the API + frontend images change. State
(postgres / redis / minio) is untouched, so no data is at risk.

## 2. Roll back the API schema (DB-level, destructive)

Goal: undo a migration that broke production.

```bash
docker compose -f docker-compose.prod.yml stop api

# Pull the latest base backup + any WAL since (slice 10.4 brings
# scripts/restore-postgres.sh; the inline version below is a
# placeholder until that lands).
LATEST=$(ls -1t /var/backups/jadecapital/postgres/*.sql.gz | head -1)
gunzip < "$LATEST" | \
    docker compose -f docker-compose.prod.yml exec -T postgres \
        psql -U ${POSTGRES_USER:-jade} -d ${POSTGRES_DB:-jadecapital}
docker compose -f docker-compose.prod.yml start api
```

Rollback boundary: drops tables / rows written by migrations applied
after the chosen backup. Schedule a maintenance window before
executing.

## 3. Roll back a single user's data (point-in-time / soft)

Goal: undo a manual fix that wrote bad data for one user.

```bash
# Inside the API container's psql session.
BEGIN;
UPDATE users
SET    is_deleted = false,
       deleted_at_utc = NULL,
       deletion_reason = NULL
WHERE  id = '<user-id>';
-- audit: write an AuditEvent row describing the restoration.
COMMIT;
```

The application picks up `is_deleted=false` on the next request. This
is the soft-delete restoration path; the same SQL backs the GDPR
"restore before 30-day grace" path in slice 10.5.

## 4. Roll back the entire stack (DR drill)

If both database AND containers are corrupt, follow
`docs/runbooks/disaster-recovery.md` (lands in slice 10.4). That
runbook restores the latest base backup + WAL replay into a fresh
Postgres container, then re-runs migrations, then starts the rest of
the stack from clean images.

## 5. Incident triage cheat sheet

| Symptom | First thing to check |
|---|---|
| API returns 502 | `docker compose ps` — is `api` or `nginx` unhealthy? |
| API returns 500 on login | DB reachable? `docker compose logs --tail=100 postgres` |
| Mail not delivered | `mailgun_api_key` rotated? Check `__Secret:mailgun_api_key` via the `/admin/health` endpoint (Wave 10.6) |
| Stripe webhook 4xx | `stripe_webhook_secret` rotated? Check `__Secret:stripe_webhook_secret` |
| Postgres OOM-killed | Check `docker stats`, raise vm.max_map_count if Patroni etc. |

## 6. Post-rollback actions

1. Open a `hotfix/` branch describing the regression and the rollback.
2. Update the open PR description with a `🔁 Rolled back` note.
3. File the root cause as a new GitHub issue linked to the rolled-back PR.
4. Run the smoke script (`scripts/wave5-smoke.sh`) every 30 minutes for
   the next 4 hours to confirm the rollback stuck.
