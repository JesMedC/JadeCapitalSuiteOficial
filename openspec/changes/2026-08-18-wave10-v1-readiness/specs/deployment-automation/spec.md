# Deployment Automation Specification

**Change**: 2026-08-18-wave10-v1-readiness
**Wave**: 10 (v1 readiness)
**Slice**: 10.2 — Production deployment story + secrets management
**Status**: NEW spec (no prior canonical)
**Strict TDD**: ACTIVE — every Requirement + Scenario here must be covered by tests in slice 10.2

## Purpose

Define the production deployment surface for Jade Capital Suite. The system MUST ship a hardened `docker-compose.prod.yml` (no Mailpit, no exposed Postgres/Redis ports, edge TLS termination required) plus multi-stage `Dockerfile.prod` for both backend (BE) and frontend (FE) with non-root users, read-only root filesystem, and healthchecks. Production secrets MUST load from a secrets manager (Docker Secrets default; Vault/Doppler/AWS Secrets Manager as documented alternatives), NEVER from `.env` files. A deployment runbook MUST document the certbot + Let's Encrypt flow, secrets rotation cadence, and rollback procedure. Without this slice, the prod container surface is identical to dev — an unrecoverable launch posture.

## ADDED Requirements

### Requirement: docker-compose.prod.yml runs the prod stack

The system MUST define `docker-compose.prod.yml` at the repo root, invoked via `docker compose -f docker-compose.prod.yml up -d`. The compose file MUST declare services: `api`, `frontend`, `postgres`, `redis`, `minio`, `nginx` (or `caddy` for TLS), `migrate`. The `postgres` + `redis` services MUST NOT expose `5432` or `6379` to the host (`expose:` only, no `ports:`). The `mailpit` service MUST NOT appear in the prod compose. Every service MUST declare `restart: always` + a `healthcheck` block. The `api` service MUST depend on `migrate: condition: service_completed_successfully` + `redis: condition: service_healthy` + `minio: condition: service_healthy`.

#### Scenario: prod stack starts + all services healthy

- GIVEN `docker-compose.prod.yml` is present and `.env.prod` has all required vars
- WHEN `docker compose -f docker-compose.prod.yml up -d` runs
- THEN all 7 services MUST reach `healthy` state within 2 minutes
- AND `docker compose ps` MUST show 0 `unhealthy` services
- AND `mailpit` MUST NOT appear in the service list

#### Scenario: nginx reverse proxy serves API + FE on :443

- GIVEN nginx (or Caddy) is the edge service
- WHEN a client hits `https://jadecapital.com/api/quotes`
- THEN the reverse proxy MUST forward to `api:8080` and return the JSON response
- AND `https://jadecapital.com/` MUST serve the FE `index.html`

### Requirement: Production secrets via Docker Secrets or external manager

The system MUST load production secrets from Docker Secrets (mounted at `/run/secrets/<name>`) as the default, with documented paths for Vault, Doppler, and AWS Secrets Manager. `JWT_ACCESS_TOKEN_SECRET`, `JWT_REFRESH_TOKEN_SECRET`, `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `MINIO_ROOT_PASSWORD`, `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET` MUST be sourced from secrets, NOT from env vars passed via `environment:`. The `docker-compose.prod.yml` MUST reference secrets via `secrets:` block. The `infrastructure/production/README.md` runbook MUST document rotation cadence (90 days for JWT secrets, immediate on staff change for DB passwords) and the one-time secret bootstrap flow.

#### Scenario: JWT secrets loaded from secrets store

- GIVEN `/run/secrets/jwt_access_token_secret` contains the signing secret
- WHEN the API container starts
- THEN `Jwt__AccessTokenSecret` MUST read from the secrets mount (verified by missing env var + successful startup)
- AND the env MUST NOT contain the plaintext secret in `docker inspect`

#### Scenario: secrets rotation runbook documented

- GIVEN an operator needs to rotate `JWT_ACCESS_TOKEN_SECRET`
- WHEN they consult `infrastructure/production/README.md`
- THEN the runbook MUST list the exact `docker secret create` + `docker service update` commands
- AND MUST warn that rotating without downtime requires a blue/green or rolling restart

### Requirement: Production healthchecks at edge + per-service

The system MUST expose `/health/live` (returns 200 when process is up) and `/health/ready` (returns 200 only when Postgres + Redis + MinIO are reachable) at the API. The edge reverse proxy MUST call `/health/ready` every 30 seconds; failure MUST remove the instance from the load-balancer pool. The `docker-compose.prod.yml` healthcheck for `api` MUST use the same TCP port probe pattern as `docker-compose.yml` (ASP.NET minimal images lack curl/wget).

#### Scenario: /health/ready returns 200 only when Postgres + Redis reachable

- GIVEN Postgres is healthy and Redis is healthy
- WHEN the API receives `GET /health/ready`
- THEN the endpoint MUST return HTTP 200 with `{"status":"healthy","checks":{"postgres":"healthy","redis":"healthy"}}`

- GIVEN Postgres is healthy but Redis is unreachable
- WHEN the API receives `GET /health/ready`
- THEN the endpoint MUST return HTTP 503 with `{"status":"unhealthy","checks":{"redis":"unhealthy"}}`

### Requirement: Deployment runbook covers deploy, rollback, incident, DR

The system MUST publish `docs/runbooks/deploy.md` covering: (a) first-time deploy (DNS, TLS via certbot, secrets bootstrap, smoke test), (b) routine deploy (image pull + `docker compose up -d` + health gate), (c) rollback (`docker compose down` + DB restore from latest `pg_dump` per slice 10.4), (d) incident response (log locations, on-call rotation, status page), (e) disaster recovery (point to `docs/runbooks/disaster-recovery.md` from slice 10.4).

#### Scenario: deployment runbook documents certbot + Let's Encrypt setup

- GIVEN an operator with a fresh VPS + DNS pointing to it
- WHEN they follow `docs/runbooks/deploy.md` §"First-time deploy"
- THEN the runbook MUST list the exact `certbot --nginx -d jadecapital.com` invocation
- AND MUST explain the DNS-01 challenge path for wildcard certs

#### Scenario: rollback runbook documents DB restore

- GIVEN a bad deploy just shipped and the operator needs to roll back
- WHEN they follow `docs/runbooks/deploy.md` §"Rollback"
- THEN the runbook MUST list `scripts/restore-postgres.sh <latest-dump>` as the first step
- AND MUST explain that rolling back the API image alone is insufficient if migrations ran

## Cross-references

- Closes gaps A2 (prod secrets management), A5 (no production deployment story)
- Related Wave 10 spec: `openspec/changes/2026-08-18-wave10-v1-readiness/specs/security-headers/spec.md` (TLS termination ships in 10.3)
- Related Wave 10 spec: `openspec/changes/2026-08-18-wave10-v1-readiness/specs/backup-strategy/spec.md` (restore script lives in 10.4)
- Source: `openspec/changes/2026-08-18-wave10-v1-readiness/explore.md` §A2, §A5

## Out of scope

- Kubernetes manifests (Wave 11+ — `docker compose` is the v1.0 surface)
- Helm charts (Wave 11+)
- Multi-region failover / DR site active-active (Wave 11+, gap G-C1)
- mTLS between services (single docker network, gap G-C4)
- Image SBOM attestation + cosign signing (Wave 11+)
- Blue/green or canary deploy (single-tenant v1.0 ships rolling restart)
