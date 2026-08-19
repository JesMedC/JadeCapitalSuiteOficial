# syntax=docker/dockerfile:1.7
# Wave 10.4 slice — order-agnostic migration runner.
#
# Architectural intent:
#   * Migrations live in `infrastructure/postgres/migrations/`. After the
#     Wave 10.4 renumbering they are consecutive `0001_*.sql` ... `0032_*.sql`
#     so that `ls | sort` yields the correct application order.
#   * This Dockerfile does NOT enumerate individual COPY lines — that approach
#     is fragile (every new migration forces a Dockerfile edit + image rebuild).
#     Instead, the host bind-mounts `./infrastructure/postgres/migrations/` into
#     the container at `/migrations/` (see `docker-compose.prod.yml`'s
#     `migrate:` service) and the `CMD` loop applies them in `ls | sort` order.
#   * Idempotency: every migration file MUST be idempotent (`CREATE TABLE IF NOT
#     EXISTS`, `DROP ... IF EXISTS`, `DO $$ ... $$` blocks). The loop tolerates
#     "already exists" errors so a partial run can be safely re-attempted.
#   * PGPASSWORD is set explicitly because we override the official entrypoint
#     (CMD bash -c) and the Postgres image does not auto-export it.
FROM postgres:16-alpine
WORKDIR /migrations
CMD ["bash", "-c", "until pg_isready -h postgres -U \"$POSTGRES_USER\"; do sleep 2; done && \
     export PGPASSWORD=\"$$(cat /run/secrets/postgres_password)\" && \
     for sql in $$(ls /migrations/*.sql 2>/dev/null | sort); do \
         echo \"Applying $$(basename $$sql)\"; \
         psql -h postgres -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -v ON_ERROR_STOP=1 -f \"$$sql\" || { \
             echo \"Migration $$(basename $$sql) failed — aborting\"; \
             exit 1; \
         }; \
     done && \
     echo 'ALL MIGRATIONS OK'"]