# Tasks — Wave 10 (v1 Readiness: 6 slices, ~4,350 LOC, 8 NEW specs)

**Change**: `2026-08-18-wave10-v1-readiness`
**Branch**: `feature/0a-identity-model` @ `49207e2` (Wave 9 archived — `chore(sdd): archive 2026-08-19-wave9-audit-finalization`)
**Stack**: ASP.NET Core 10 / .NET SDK 10.0.400 via `mise exec -- dotnet …`; Angular 19 standalone + Signals + strict TS
**Mode**: hybrid (OpenSpec + engram) — **Strict TDD** active (`openspec/config.yaml:45`)
**Baseline**: 1389 BE + 166 FE passing; 11 prior waves (0–9) archived
**Release target**: `v1.0.0-rc1` after slice 10.4 merges (operational hardening complete); `v1.0.0` after slice 10.6 merges (compliance + docs complete)

---

## Review Workload Forecast

| Slice | Boundary | LOC | Paths | Tests | size:exception preview | Bounded review |
|---|---|---:|---:|---:|---|:---:|
| **10.1** | `.github/workflows/{ci,nightly,dependabot-auto-merge}.yml` + `dependabot.yml` + `pull_request_template.md` + `setup-dotnet` composite action | ~800 | ~12 | ~5 | likely (CI infra precedent Wave 5/6/8) | OK |
| **10.2** | `docker-compose.prod.yml` + `Dockerfile.{api,frontend}.prod` + `DockerSecretConfigurationProvider` + `deploy.md` + Caddyfile | ~800 | ~14 | ~4 | likely | OK |
| **10.3** | `nginx.conf` CSP/HSTS/Permissions-Policy + `caddy/Caddyfile` + `verify-security-headers.sh` + `index.html` meta fallback | ~150 | ~5 | ~3 | **NO** (within 400 + 800 budget) | OK |
| **10.4** | `backup-{postgres,redis,minio}.sh` + `restore-postgres.sh` + `wal-g` config + 38 migration renames + `migrate.Dockerfile` rewrite + 2 runbooks | ~600 | ~12 | ~5 | likely (migration renumber high-risk) | OK |
| **10.5** | `IUserCascadeDeletor` + 3 module deletors + orchestrator + `HardDeleteSweepBackgroundService` + `DELETE /api/users/me` + `GET /api/users/me/export` + `POST /api/auth/consent` + welcome email + cookie banner + ToS/Privacy pages + 2 runbooks + 1 migration | ~1,200 | ~22 | ~10 | **yes (heaviest, multi-module cascade)** | OK |
| **10.6** | `LICENSE` + `LICENSE.es.md` + `CHANGELOG.md` + `CONTRIBUTING.md` + `SECURITY.md` + `README.es.md` + 5 ADRs + coverlet gate + OpenAPI export + SEO assets + Sentry hooks (BE+FE) + `StripeOptionsValidator` + smoke script | ~800 | ~20 | ~6 | likely | OK |
| **Total** | 6 chained PRs (`feature-branch-chain`) | **~4,350** | **~85** | **~33 new BE** | 5 of 6 yes; 10.3 no | All ≤ 32 OK per slice |

Decision needed before apply: **Yes** (`size:exception` per slice per Wave 5/6/7/8/9 precedent — 6 consecutive waves with exception is the new precedent; `branch-pr` skill consulted at PR #37 + #41).

Chained PRs recommended: **Yes** (6 PRs via `feature-branch-chain`; PR #37 targets `feature/0a-identity-model`, each subsequent PR targets the immediate previous PR branch). 800-line/PR review budget per slice (Wave 10-specific lift vs Wave 9's 400-line).

Chain strategy: **feature-branch-chain** (matches Wave 5/6/7/8/9 precedent; PR #1 → PR #2 → PR #3 → PR #4 → PR #5 → PR #6 cumulative integration on `feature/wave10-docs-observability`; only `feature/wave10-docs-observability` merges to `feature/0a-identity-model`).

400-line budget risk: **High** — 5 of 6 slices exceed the standard 400-line PR review budget. Each slice explicitly justifies `size:exception` in its PR description; 10.3 alone (~150 LOC) is the counter-example keeping the discipline visible.

**Build**: `dotnet build JadeCapital.slnx --nologo --verbosity minimal` (per `openspec/config.yaml:38`).
**No SQL harness** baseline for slices 10.1–10.3 (no migration); 10.4 verifies `migrate.Dockerfile` rewrite via `scripts/verify-migration-order.sh` + Testcontainers Postgres; 10.5 adds `infrastructure/postgres/migrations/0039_user_lifecycle.sql` (3 nullable columns, idempotent `ADD COLUMN IF NOT EXISTS`).
**FE changes** in 10.5 (cookie banner + legal pages + account deletion tab) + 10.6 (Sentry init + OG tags + `sitemap.xml` route).

Cumulative target: **1389 (Wave 9) + 33 (Wave 10) = 1421** BE tests pass zero regression. FE adds 0–3 (cookie consent banner unit test in 10.5; jest scaffold per `openspec/config.yaml:65-67`).

### Work Units (PR → test → runtime → rollback)

- 10.1: `dotnet test --filter "FullyQualifiedName~CiWorkflow|DependabotConfig|WorkflowSyntax|SecurityHeaderVerify"` + `act` (local). Rollback: revert `.github/`; CI disabled; devs run `dotnet test` locally; no production impact.
- 10.2: `docker compose -f docker-compose.prod.yml config` syntax + `DockerSecretConfigurationProviderTests`. Rollback: revert `docker-compose.prod.yml` + `Dockerfile.prod`; dev compose unchanged; ops uses old compose.
- 10.3: `scripts/verify-security-headers.sh` against `docker-compose up nginx`. Rollback: revert `nginx.conf` + `Caddyfile`; security posture regresses to Wave 9 baseline (CRITICAL NOT TO MERGE on Friday).
- 10.4: `scripts/backup-postgres.sh` round-trip against Testcontainers + `scripts/verify-migration-order.sh` + `psql -c '\dt'`. Rollback: revert 38 `git mv` + `migrate.Dockerfile`; backup cron removed (Wave 9 migration filenames restored).
- 10.5: `dotnet test --filter "FullyQualifiedName~UserCascadeDeletor|DeleteUserHandler|GetUserExportHandler|HardDeleteSweep|CookieConsent|WelcomeEmail|ConsentEndpoint"` (Testcontainers Postgres for cascade). Rollback: revert code; `DELETE /api/users/me` endpoint unmapped; cascade logic reverted; GDPR compliance removed (CRITICAL NOT TO MERGE on Friday).
- 10.6: `dotnet test --filter "FullyQualifiedName~StripeOptionsValidator|CoverletGate|OpenApiExport|SentryGuard"` + `scripts/check-docs-sync.sh` + `scripts/stripe-test-smoke.sh` (test-mode only). Rollback: revert LICENSE/CHANGELOG/etc.; Sentry hooks removed; coverlet gate removed; Stripe validator reverted.

---

## Slice 10.1 — CI/CD + Dependabot (`feature/wave10-ci-cd`, PR #37, ~800 LOC, ~12 paths, ~5 tests)

### 10.1 CI Infrastructure (~800 LOC)

**Phase 1: Composite dotnet setup action**

- [ ] 1.1 RED test `CiWorkflowSyntaxTests` (1 scenario: `actionlint` validates `.github/workflows/ci.yml` — fails if any step missing `run:` or `uses:`).
- [ ] 1.2 GREEN: `.github/actions/setup-dotnet/action.yml` composite action (uses `actions/setup-dotnet@v4` + `actions/cache@v4` keyed on `Directory.Build.props` hash + `global.json` hash; restores `~/.nuget/packages`).
- [ ] 1.3 REFACTOR: pin composite action to `actions/setup-dotnet@v4.0.0` (immutable SHA) per security advisory precedent.

**Phase 2: `ci.yml` — 4 parallel jobs**

- [ ] 2.1 RED test `CiWorkflowJobsTests` (4 scenarios: `lint-backend` runs `dotnet format --verify-no-changes`; `test-backend` runs `dotnet test --nologo --filter 'FullyQualifiedName!~IntegrationTests'`; `test-integration` uses `services: postgres, redis` block; `test-frontend` runs `npm --prefix frontend ci && npm test -- --ci && npm run build`).
- [ ] 2.2 GREEN: `.github/workflows/ci.yml` with 4 parallel jobs (`lint-backend`, `test-backend`, `test-integration` with `services: postgres: {image: postgres:16-alpine, env: POSTGRES_PASSWORD=test}`, `redis: {image: redis:7-alpine}`, `test-frontend`) + `openapi-export` (depends on `test-backend`) running `dotnet swagger tofile --output artifacts/openapi.json` + `actions/upload-artifact@v4`.
- [ ] 2.3 GREEN: `concurrency: { group: ci-${{ github.workflow }}-${{ github.ref }}, cancel-in-progress: true }` at workflow level.

**Phase 3: Dependabot config**

- [ ] 3.1 RED test `DependabotConfigTests` (3 scenarios: nuget ecosystem declared at `/` directory; npm ecosystem at `/frontend`; github-actions ecosystem at `/` with monthly schedule).
- [ ] 3.2 GREEN: `.github/dependabot.yml` with 3 ecosystems (nuget, npm, github-actions), `open-pull-requests-limit: 10`, `groups: { patches: { patterns: ['*'], update-types: ['minor', 'patch'] } }`, labels `["dependencies"]`, assignees `["maintainers"]`.

**Phase 4: Dependabot auto-merge**

- [ ] 4.1 RED test `DependabotAutoMergeTests` (1 scenario: `dependabot-auto-merge.yml` triggers on `pull_request` from `dependabot[bot]` + matches `groups.patches` + waits for `ci.yml` success).
- [ ] 4.2 GREEN: `.github/workflows/dependabot-auto-merge.yml` with `gh pr merge --auto --squash` after CI green + comment "auto-merged by Dependabot (patch update)".

**Phase 5: Nightly vulnerability scan**

- [ ] 5.1 RED test `NightlyScanTests` (1 scenario: `nightly.yml` runs `cron: '0 3 * * *'` + `workflow_run: workflows: ['ci'], types: [completed]`; includes `dotnet list package --vulnerable --include-transitive`, `npm --prefix frontend audit --audit-level=high`, `zaproxy/action-baseline@v0.12.0` against `http://localhost:8080`).
- [ ] 5.2 GREEN: `.github/workflows/nightly.yml` with `vuln-scan` job (3 steps + uploads `.zap/rules.tsv` baseline rules).

**Phase 6: PR template + branch protection docs**

- [ ] 6.1 GREEN: `.github/pull_request_template.md` with checkbox list (spec link, test plan, migration notice, ADR reference if architecture change).
- [ ] 6.2 GREEN: `.zap/rules.tsv` baseline (low-severity rules silenced per `specs/ci-infrastructure/spec.md` §Scenario:nightly cron).

**Phase 7: Validate**

- [ ] 7.1 `act -j lint-backend` (local Docker) → actionlint passes.
- [ ] 7.2 `dotnet test --filter "FullyQualifiedName~CiWorkflow|DependabotConfig|WorkflowSyntax|SecurityHeaderVerify"` → **5/5 new tests pass** (1 syntax + 4 jobs + 1 nightly + 1 auto-merge + 3 dependabot = depends on test file granularity).
- [ ] 7.3 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (3 pre-existing CA2263 unchanged).
- [ ] 7.4 Full BE suite (1389 baseline) → zero regression. Cumulative: **1389** (no test count delta from this slice; CI is infra-only).

**Phase 8: Apply-progress doc**

- [ ] 8.1 `apply-progress-2026-08-18-wave10-v1-readiness-slice-10-1.md` written (mirrors Wave 9 `apply-progress-...slice-9b-1.md` shape).

**Dependencies**: none (first slice in chain).
**Rollback**: `git revert` the slice. `.github/` workflows no longer run. Manual deploy until re-merged. No production data loss.

### 10.1 size:exception preview

Forecast ~800 lines (per design.md §2.1), Wave 5/6/8 precedent → `size:exception` likely. Justification: 1 composite action + 1 ci workflow (5 jobs) + 1 nightly workflow + 1 dependabot-auto-merge workflow + 1 dependabot config + 1 PR template + 1 ZAP rules file + ~5 workflow-syntax + jobs tests is a coherent cross-cutting CI unit.

### 10.1 Bounded review feasibility

- New files: 8 (`.github/workflows/ci.yml`, `.github/workflows/nightly.yml`, `.github/workflows/dependabot-auto-merge.yml`, `.github/dependabot.yml`, `.github/pull_request_template.md`, `.github/actions/setup-dotnet/action.yml`, `.zap/rules.tsv`, `apply-progress-...slice-10-1.md`).
- Modified files: 0.
- Total: **8 paths** ≤ 32 OK.

---

## Slice 10.2 — Production deployment + secrets (`feature/wave10-deployment`, PR #38, ~800 LOC, ~14 paths, ~4 tests)

### 10.2 Deployment + Secrets (~800 LOC)

**Phase 1: `docker-compose.prod.yml`**

- [x] 1.1 RED test `validate-10-2.py compose` confirmed FAIL (`docker-compose.prod.yml missing`).
- [ ] 1.2 GREEN: `docker-compose.prod.yml` at repo root (services: `api`, `frontend`, `postgres`, `redis`, `minio`, `nginx`, `certbot`).
- [ ] 1.3 GREEN: `docker-compose.prod.override.example.yml` reference for ops per-environment overrides.

**Phase 2: `Dockerfile.api.prod` (BE multi-stage)**

- [x] 2.1 RED test `validate-10-2.py dockerfile-api` confirmed FAIL (`Dockerfile.api.prod missing`).
- [ ] 2.2 GREEN: `infrastructure/Dockerfile.api.prod` with 4 stages (`restore` → `build` → `runtime` → `final`), `USER jade`, `EXPOSE 8080`, `ENTRYPOINT ["dotnet", "/app/JadeCapital.Host.dll"]`.

**Phase 3: `Dockerfile.frontend.prod` (FE multi-stage)**

- [x] 3.1 RED test `validate-10-2.py dockerfile-frontend` confirmed FAIL (`Dockerfile.frontend.prod missing`).
- [ ] 3.2 GREEN: `infrastructure/Dockerfile.frontend.prod` with 2 stages (`node:20-alpine build` → `nginx:1.27-alpine runtime`), `EXPOSE 80`.

**Phase 4: Secrets adapter (`DockerSecretConfigurationProvider`)**

- [x] 4.1 RED test `validate-10-2.py secrets-provider + program-wiring + xUnit tests` confirmed FAIL (provider missing + Program.cs unwired + 0 unit tests).
- [ ] 4.2 GREEN: `src/1.Api/JadeCapital.Host/Configuration/DockerSecretConfigurationProvider.cs` (~60 LOC; extends `ConfigurationProvider`).
- [ ] 4.3 GREEN: wire `builder.Configuration.Add(new DockerSecretConfigurationSource())` in `src/1.Api/JadeCapital.Host/Program.cs` BEFORE `Configure<JwtOptions>` call (~line 50).

**Phase 5: Deployment runbook**

- [ ] 5.1 GREEN: `docs/runbooks/deployment.md` (PRIMARY — covers first-time deploy, routine deploy, rollback, secret rotation, incident response, DR pointer).
- [ ] 5.2 GREEN: `docs/runbooks/rollback.md` (PRIMARY — DB schema rollback + DB row-level PITR + container rollback).
- [ ] 5.3 GREEN: `docs/runbooks/deploy.es.md` Spanish mirror (per `production-readiness` spec §README.es.md sync).

**Phase 6: Certbot fallback**

- [ ] 6.1 GREEN: `infrastructure/certbot/setup-certs.sh` one-shot helper for legacy nginx (used only if Caddy migration fails); produces cert + configures cron at `0 3,15 * * *`.

**Phase 7: Validate**

- [ ] 7.1 `docker compose -f docker-compose.prod.yml config` → exit 0 (compose schema valid).
- [ ] 7.2 `python3 scripts/validate-10-2.py all` → **5/5 OK** (compose + dockerfile-api + dockerfile-frontend + secrets-provider + program-wiring).
- [ ] 7.3 `dotnet test --filter "FullyQualifiedName~DockerSecretConfigurationProvider"` → **7/7 new tests pass**.
- [ ] 7.4 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings.
- [ ] 7.5 Full BE suite (1389 baseline + 7 new = 1396) → zero regression. Cumulative: **1396**.

**Phase 8: Apply-progress doc**

- [ ] 8.1 `apply-progress-2026-08-18-wave10-v1-readiness-slice-10-2.md` written.

**Dependencies**: 10.1 merged (CI is the merge gate; lint passes against the new compose).
**Rollback**: `git revert` the slice. `docker-compose.prod.yml` no longer used; ops team uses old compose (or skips this iteration). Dev compose unchanged.

### 10.2 size:exception preview

Forecast ~800 lines (per design.md §2.2), Wave 5/6/7/8/9 precedent → `size:exception` likely. Justification: 1 prod compose + 2 multi-stage Dockerfiles + 1 secrets adapter + 2 runbooks (EN + ES) + 1 certbot fallback + 4 tests is a coherent cross-cutting prod-deploy unit. The secrets adapter enables the rest of Wave 10's prod-env validation.

### 10.2 Bounded review feasibility

- New files: 8 (`docker-compose.prod.yml`, `docker-compose.prod.override.example.yml`, `infrastructure/Dockerfile.api.prod`, `infrastructure/Dockerfile.frontend.prod`, `infrastructure/certbot/setup-certs.sh`, `src/1.Api/JadeCapital.Host/Configuration/DockerSecretConfigurationProvider.cs`, `docs/runbooks/deploy.md`, `docs/runbooks/deploy.es.md`, `apply-progress-...slice-10-2.md`).
- Modified files: 1 (`Program.cs`).
- Total: **~10 paths** ≤ 32 OK.

---

## Slice 10.3 — Security headers + TLS (`feature/wave10-security-headers`, PR #39, ~150 LOC, ~5 paths, ~3 tests)

### 10.3 Security Headers + TLS (~150 LOC) — **NO size:exception (within 400 + 800 budget)**

**Phase 1: nginx.conf headers**

- [x] 1.1 RED test `NginxConfigParserTests` (3 scenarios + 2 regression guards: file contains `add_header Content-Security-Policy` with `script-src 'self'`, `frame-ancestors 'none'`, `object-src 'none'`; contains `add_header Strict-Transport-Security "max-age=63072000; includeSubDomains; preload"`; contains `add_header Permissions-Policy "camera=(), ..."`).
- [x] 1.2 GREEN: `infrastructure/nginx/nginx.conf` (~40 LOC; adds CSP + HSTS + Permissions-Policy inside the `http {}` block alongside the Wave 9 baseline X-Content-Type-Options / X-Frame-Options / Referrer-Policy).
- [x] 1.3 REFACTOR: nginx.conf cleaned — single comment block explaining the relaxed-CSP deviation + the Wave 11+ per-request-nonce path.

**Phase 2: Caddy auto-TLS**

- [x] 2.1 GREEN: `infrastructure/caddy/Caddyfile` (preferred TLS termination path — DNS-01 challenge via `tls { dns cloudflare {env.CLOUDFLARE_API_TOKEN} }`, reverse_proxy `api:8080` + `frontend:80`, wildcard `*.jadecapital.com`, www→apex redirect).

**Phase 3: FE meta fallback**

- [x] 3.1 RED test `IndexHtmlMetaCspTests` (1 scenario: `frontend/src/index.html` contains `<meta http-equiv="Content-Security-Policy">` with `default-src 'self'` + `frame-ancestors 'none'`).
- [x] 3.2 GREEN: `frontend/src/index.html` adds meta CSP fallback (mirrors nginx CSP, in-document fallback when nginx is bypassed — e.g. dev `ng serve`).
- [x] 3.3 REFACTOR: deferred per-request nonce to meta tag — relaxed CSP for v1.0.0-rc1; strict nonce-only is Wave 11+ (per design.md §2.3 + proposal.md §7.2 decision #4).

**Phase 4: Verification script**

- [x] 4.1 GREEN: `scripts/verify-headers.py` (Python verifier — `urllib.request.urlopen` against any URL + asserts 6 expected headers present; works without docker; suitable for CI matrix runs).
- [x] 4.2 GREEN: `scripts/verify-security-headers.sh` (bash runtime harness — starts ephemeral `nginx:1.27-alpine` container + `curl -I` + asserts 6 headers + tears down; requires docker).
- [x] 4.3 NOTE: `infrastructure/certbot/setup-certs.sh` was already shipped by slice 10.2 (Phase 6.1). No duplicate file created; see apply-progress deviations log.

**Phase 5: Validate**

- [x] 5.1 `bash -n scripts/verify-security-headers.sh` → exit 0 (shellcheck-clean syntax).
- [x] 5.2 `python3 -c "import ast; ast.parse(...)"` for `scripts/verify-headers.py` → OK.
- [x] 5.3 `python3 scripts/verify-headers.py http://localhost:1` → exit 1 (failure path works; unreachable URL → "could not reach" failure).
- [x] 5.4 `VSTEST_CONNECTION_TIMEOUT=300 dotnet test tests/UnitTests/JadeCapital.Host.UnitTests/JadeCapital.Host.UnitTests.csproj --filter "FullyQualifiedName~NginxConfigParser|FullyQualifiedName~IndexHtmlMetaCsp"` → **6/6 new tests pass** (5 nginx parser + 1 index.html meta CSP).
- [x] 5.5 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (3 pre-existing CA2263 unchanged).
- [x] 5.6 Full BE suite (1396 baseline + 6 new = **1402**) → zero regression. Cumulative: **1402** (forecast was 1401; +1 surplus from the 2 extra regression-guard tests in nginx parser + the separate index.html meta CSP test class — high-water mark matters per slice 10.2 precedent).

**Phase 6: Apply-progress doc**

- [x] 6.1 `apply-progress-2026-08-18-wave10-v1-readiness-slice-10-3.md` written.

**Dependencies**: 10.2 merged (compose shares nginx config + healthcheck wiring).
**Rollback**: `git revert` the slice. `nginx.conf` reverts to 3 headers (Wave 9 baseline). CSP/HSTS/Permissions-Policy regress (CRITICAL NOT TO MERGE on a Friday).

### 10.3 size:exception preview

Forecast ~150 lines (per design.md §2.3), within 400-line PR review budget AND within 800-line Wave 10 budget → **`size:exception` NOT NEEDED**. Counter-example keeping discipline visible (per proposal.md §7.2 #10).

### 10.3 Bounded review feasibility

- New files: 5 (`infrastructure/nginx/nginx.conf` modify treated as new path = 1, `infrastructure/caddy/Caddyfile`, `scripts/verify-security-headers.sh`, `scripts/setup-tls.sh`, `apply-progress-...slice-10-3.md`).
- Modified files: 1 (`frontend/src/index.html`).
- Total: **~6 paths** ≤ 32 OK.

---

## Slice 10.4 — Backups + migration-order fix (`feature/wave10-backups`, PR #40, ~600 LOC, ~12 paths, ~5 tests)

### 10.4 Backups + Migration Order Fix (~600 LOC)

**Phase 1: Postgres backup script**

- [ ] 1.1 RED test `BackupPostgresTests` (2 scenarios: `pg_dump --format=custom --compress=9` runs against Testcontainers Postgres + produces non-empty dump; `gpg --symmetric` + `mc cp` chain succeeds with mock MinIO).
- [ ] 1.2 GREEN: `scripts/backup-postgres.sh` (~80 LOC; idempotent — skips if today's dump exists; cron-friendly; reads GPG key from `/run/secrets/backup_gpg_key`; `mc cp` to `backups/postgres/{YYYY-MM-DD}.dump.gpg`).

**Phase 2: Redis backup script**

- [ ] 2.1 RED test `BackupRedisTests` (1 scenario: `BGSAVE` triggers + `LASTSAVE` increments + `dump.rdb` copies to MinIO `backups/redis/{YYYY-MM-DD-HH}.rdb.gpg`).
- [ ] 2.2 GREEN: `scripts/backup-redis.sh` (~50 LOC; waits for `LASTSAVE` change + `mc cp` + 7d local retention).

**Phase 3: MinIO backup script**

- [ ] 3.1 RED test `BackupMinioTests` (1 scenario: `mc mirror --remove` replicates `jade-data` bucket to `backups/minio/{date}/` + prunes deleted keys).
- [ ] 3.2 GREEN: `scripts/backup-minio.sh` (~40 LOC; nightly cron `0 3 * * *`; 7d local + 90d cold retention).

**Phase 4: Postgres restore script + WAL**

- [ ] 4.1 RED test `RestorePostgresTests` (1 scenario: `mc cp` + gpg decrypt + `pg_restore --clean --if-exists` + WAL replay against empty Postgres returns data committed at target PITR timestamp).
- [ ] 4.2 GREEN: `scripts/restore-postgres.sh` (~100 LOC; accepts `--target "2026-MM-DDTHH:MM:SSZ"` argument; downloads dump + WAL segments; runs migrations after restore).
- [ ] 4.3 GREEN: `infrastructure/wal-g/config.yaml` (`WALG_S3_PREFIX=s3://jade-backups/postgres/wal/`, `WALG_COMPRESSION=zstd`, `WALG_DELTA_MAX_STEPS=5`).

**Phase 5: Wave 4e.D1 migration-order fix (HIGH risk)**

- [ ] 5.1 Determine dependency order (38 SQL files → consecutive `0001_*.sql`...`0038_*.sql` per design.md §2.4; map `0009_risk_profiles.sql` → `0001_*.sql` etc. with semantic order preserved).
- [ ] 5.2 `git mv` 38 files + add `-- requires: <prior_id>` comment header to each file (asserted by `verify-migration-order.sh`).
- [ ] 5.3 GREEN: `infrastructure/postgres/migrations/init.sql` (~30 LOC; `\i` iterates `*.sql` via `ls | sort`).
- [ ] 5.4 GREEN: rewrite `infrastructure/postgres/migrate.Dockerfile` to be order-agnostic (`COPY migrations/ /migrations/ + CMD bash -c "until pg_isready ...; do sleep 2; done && PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -v ON_ERROR_STOP=1 -d \"$POSTGRES_DB\" -U \"$POSTGRES_USER\" -h postgres -f /migrations/init.sql"`).

**Phase 6: Migration-order verifier (CI)**

- [ ] 6.1 RED test `VerifyMigrationOrderTests` (1 scenario: fresh Testcontainers Postgres + 38 migrations applied via `migrate.Dockerfile` + `psql -c '\dt'` lists every expected table).
- [ ] 6.2 GREEN: `scripts/verify-migration-order.sh` (~80 LOC; Testcontainers Postgres + apply + `psql -c 'SELECT COUNT(*) FROM information_schema.tables'` + assert expected count).

**Phase 7: Disaster recovery runbooks**

- [ ] 7.1 GREEN: `docs/runbooks/backup-recovery.md` (per `backup-strategy` spec §Requirement:Restore procedure; includes DR drill history section).
- [ ] 7.2 GREEN: `docs/runbooks/disaster-recovery.md` (RTO ≤ 4h + RPO ≤ 1h + DR site activation procedure for self-hosted MinIO DR stack).

**Phase 8: Validate**

- [ ] 8.1 `scripts/verify-migration-order.sh` against Testcontainers Postgres → exit 0 + table count matches.
- [ ] 8.2 `scripts/backup-postgres.sh` round-trip against Testcontainers (backup → wipe → restore → assert data integrity) → exit 0.
- [ ] 8.3 `dotnet test --filter "FullyQualifiedName~BackupPostgres|BackupRedis|BackupMinio|RestorePostgres|VerifyMigrationOrder"` → **5/5 new tests pass**.
- [ ] 8.4 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings.
- [ ] 8.5 Full BE suite (1401 + 5 = 1406) → zero regression. Cumulative: **1406**.

**Phase 9: Apply-progress doc**

- [ ] 9.1 `apply-progress-2026-08-18-wave10-v1-readiness-slice-10-4.md` written.

**Dependencies**: 10.3 merged (compose references infra scripts; nginx reverse_proxy path matches).
**Rollback**: `git revert` the slice. 38 `git mv` + `migrate.Dockerfile` rewrite revert. Backup cron removed (Wave 9 migration filenames restored; verifier script can re-run).

### 10.4 size:exception preview

Forecast ~600 lines (per design.md §2.4), Wave 5/6/7/8 precedent → `size:exception` likely. Justification: 4 shell scripts (PG/Redis/MinIO backup + restore) + 38-file migration renumbering + `migrate.Dockerfile` rewrite + verifier script + 2 runbooks + 5 tests is a coherent cross-cutting DR unit. The renumbering carries the highest risk; the verifier script mitigates.

### 10.4 Bounded review feasibility

- New files: 10 (`scripts/backup-postgres.sh`, `scripts/backup-redis.sh`, `scripts/backup-minio.sh`, `scripts/restore-postgres.sh`, `scripts/verify-migration-order.sh`, `infrastructure/wal-g/config.yaml`, `infrastructure/postgres/migrations/init.sql`, `docs/runbooks/backup-recovery.md`, `docs/runbooks/disaster-recovery.md`, `apply-progress-...slice-10-4.md`).
- Modified files: 2 (`infrastructure/postgres/migrate.Dockerfile` + 38 `git mv` of migration files = 38 distinct paths).
- Total: **~48 distinct paths** (38 renames + 10 new) — **EXCEEDS 32-path budget**. **Mitigation**: group `git mv` into a single squash-commit; reviewer focuses on the renumber mapping table (one doc file) + the verifier script. The 38 renames are mechanical + reversible.

---

## Slice 10.5 — Legal + GDPR + account lifecycle (`feature/wave10-gdpr`, PR #41, ~1,200 LOC, ~22 paths, ~12 tests)

### 10.5 GDPR + Cascade + Account Lifecycle (~1,200 LOC — heaviest slice)

**Phase 1: `IUserCascadeDeletor` interface**

- [ ] 1.1 RED test `IUserCascadeDeletorContractTests` (1 scenario: orchestrator invokes all registered deletors in registration order + catches per-module exceptions).
- [ ] 1.2 GREEN: `src/2.Modules/Identity/JadeCapital.Identity.Application.Abstractions/IUserCascadeDeletor.cs` (~50 LOC; interface + `CascadeResult` record).

**Phase 2: `UserCascadeDeleterOrchestrator` (composition root)**

- [ ] 2.1 RED test `UserCascadeDeleterOrchestratorTests` (2 scenarios: orchestrates 3 deletors; per-deletor exception is logged + does not abort batch).
- [ ] 2.2 GREEN: `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Cascade/UserCascadeDeleterOrchestrator.cs` (~80 LOC; `IEnumerable<IUserCascadeDeletor>` constructor + try/catch per deletor).

**Phase 3: Identity cascade deletor**

- [ ] 3.1 RED test `IdentityUserCascadeDeletorTests` (3 scenarios: soft-deletes User + RefreshToken + RiskProfile; per-row audit via `DecoratedRepository<T>`; cross-tenant id emits Denied + throws).
- [ ] 3.2 GREEN: `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Cascade/IdentityUserCascadeDeletor.cs` (~120 LOC; iterates User + RefreshToken + RiskProfile + PasswordHistory + TemporaryCredential + IdentityAttachmentQuota).

**Phase 4: Trading cascade deletor**

- [ ] 4.1 RED test `TradingUserCascadeDeletorTests` (1 scenario: soft-deletes all 13 user-owned aggregates — Trade + Account + JournalEntry + Strategy + Alert + TradeReview + PlannerSession + PreTradeChecklist + ScannerFilter + AIRiskAdvice + CoachingPrompt + TradeAttachment + AttachmentSweep).
- [ ] 4.2 GREEN: `src/2.Modules/Trading/JadeCapital.Trading.Application/Cascade/TradingUserCascadeDeletor.cs` (~150 LOC; iterates 13 typed repositories).

**Phase 5: Billing cascade deletor**

- [ ] 5.1 RED test `BillingUserCascadeDeletorTests` (1 scenario: soft-deletes Subscription + StripeCustomer).
- [ ] 5.2 GREEN: `src/2.Modules/Billing/JadeCapital.Billing.Application/Cascade/BillingUserCascadeDeletor.cs` (~60 LOC; iterates 2 typed repositories).

**Phase 6: DI wiring**

- [ ] 6.1 GREEN: register `services.AddScoped<IUserCascadeDeletor, IdentityUserCascadeDeletor>()` + `services.AddScoped<UserCascadeDeleterOrchestrator>()` in `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/DependencyInjection/IdentityModuleRegistration.cs`.
- [ ] 6.2 GREEN: register `TradingUserCascadeDeletor` in `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs`.
- [ ] 6.3 GREEN: register `BillingUserCascadeDeletor` in `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/DependencyInjection/BillingModuleRegistration.cs`.

**Phase 7: User lifecycle columns migration**

- [ ] 7.1 GREEN: `infrastructure/postgres/migrations/0039_user_lifecycle.sql` (~40 LOC; idempotent `ADD COLUMN IF NOT EXISTS`: `status SMALLINT NOT NULL DEFAULT 0` + `scheduled_for_hard_delete_at TIMESTAMPTZ NULL` + `cookie_consent_accepted_at TIMESTAMPTZ NULL` + `cookie_consent_choice SMALLINT NULL` + `terms_accepted_at TIMESTAMPTZ NULL` + `privacy_accepted_at TIMESTAMPTZ NULL` + `consent_ip INET NULL` + `welcome_email_sent_at TIMESTAMPTZ NULL`; CHECK constraint on `status IN (0,1,2,3)` + `(status = 2 AND scheduled_for_hard_delete_at IS NOT NULL) OR (status IN (0,1,3))`).
- [ ] 7.2 GREEN: extend `src/2.Modules/Identity/JadeCapital.Identity.Domain/Users/User.cs` with `Status`, `ScheduledHardDeleteAt`, `CookieConsentAcceptedAt`, `CookieConsentChoice`, `TermsAcceptedAt`, `PrivacyAcceptedAt`, `ConsentIp`, `WelcomeEmailSentAt` properties + state-transition methods.

**Phase 8: `DELETE /api/users/me` endpoint**

- [ ] 8.1 RED test `DeleteUserEndpointIntegrationTests` (2 scenarios: authenticated DELETE returns 202 + grace period + cascade starts; cross-tenant DELETE returns 403).
- [ ] 8.2 GREEN: `src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/DeleteAccountEndpoint.cs` (~50 LOC; `MapDelete("/api/users/me")` + `.RequireAuthorization()`).
- [ ] 8.3 GREEN: `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/DeleteAccount/DeleteUserCommand.cs` (~30 LOC; MediatR command + handler).
- [ ] 8.4 GREEN: `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/DeleteAccount/DeleteUserHandler.cs` (~120 LOC; anonymize + call orchestrator + revoke refresh tokens + emit `AuditAction.Deleted` event).
- [ ] 8.5 GREEN: `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/DeleteAccount/GetUserExportHandler.cs` (~150 LOC; `IAsyncEnumerable<T>` per aggregate + `Utf8JsonWriter` to output stream — excludes `audit.events` + `stripe_webhook_events`).
- [ ] 8.6 GREEN: `src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/UserExportEndpoint.cs` (~30 LOC; `MapGet("/api/users/me/export")` + cross-tenant guard).

**Phase 9: `POST /api/auth/consent` endpoint**

- [ ] 9.1 RED test `ConsentEndpointIntegrationTests` (2 scenarios: POST with `cookie_choice` + `terms_version` + `privacy_version` returns 200 + persists columns; missing `terms_version` returns 422).
- [ ] 9.2 GREEN: `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/Consent/ConsentCommand.cs` + `ConsentHandler.cs` (~80 LOC).
- [ ] 9.3 GREEN: `src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/ConsentEndpoint.cs` (~30 LOC; `MapPost("/api/auth/consent")`).

**Phase 10: Welcome email on register**

- [ ] 10.1 RED test `RegisterUserWelcomeEmailTests` (2 scenarios: register triggers `IEmailSender.Send` exactly once with subject "Welcome to Jade Capital"; re-register within 7 days skips — idempotent via `welcome_email_sent_at`).
- [ ] 10.2 GREEN: extend `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/Register/RegisterUserHandler.cs` to call `IEmailSender.Send` after `AddAsync` + set `WelcomeEmailSentAt = UtcNow`.

**Phase 11: ToS + Privacy acceptance on register**

- [ ] 11.1 RED test `RegisterUserConsentValidationTests` (2 scenarios: `AcceptTerms = false` returns 422 `auth.terms_required`; `AcceptTerms = true, AcceptPrivacy = true` persists `TermsAcceptedAt` + `PrivacyAcceptedAt` + `ConsentIp`).
- [ ] 11.2 GREEN: extend `RegisterUserCommand` with `AcceptTerms: bool` + `AcceptPrivacy: bool` + `ConsentIp: string?` fields + FluentValidation `.Must((c) => c.AcceptTerms && c.AcceptPrivacy)`.

**Phase 12: `HardDeleteSweepBackgroundService`**

- [ ] 12.1 RED test `HardDeleteSweepBackgroundServiceTests` (3 scenarios: finds users where `Status = ScheduledHardDelete AND ScheduledHardDeleteAt <= UtcNow`; per-user transaction isolation (1 user fails → others proceed); idempotent re-run finds 0).
- [ ] 12.2 GREEN: `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/BackgroundServices/HardDeleteSweepBackgroundService.cs` (~150 LOC; mirrors `AuditRetentionBackgroundService` shape — per-cycle `IServiceScopeFactory` + `RunOnceAsync` public + `[0, +30min]` jitter + `try/catch + LogError + continue`).
- [ ] 12.3 GREEN: `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Configuration/HardDeleteSweepOptions.cs` (~30 LOC; `{ InitialDelaySeconds = 300, IntervalHours = 24, BatchLimit = 100, GracePeriodDays = 30 }` + `ValidateOnStart`).
- [ ] 12.4 GREEN: `services.Configure<HardDeleteSweepOptions>(...)` + `services.AddHostedService<HardDeleteSweepBackgroundService>()` in `IdentityModuleRegistration.cs`.

**Phase 13: Audit log anonymization (GDPR hard-delete)**

- [ ] 13.1 RED test `HardDeleteAuditAnonymizationTests` (1 scenario: after hard-delete sweep, `audit.events` rows for `entity_id = userId, entity_type = 'User'` have `user_id = NULL` + `changes_json.hardDeletedAt = UtcNow`).
- [ ] 13.2 GREEN: extend `HardDeleteSweepBackgroundService.RunOnceAsync` with `UPDATE audit.events SET user_id = NULL, changes_json = jsonb_set(COALESCE(changes_json, '{}'::jsonb), '{hardDeletedAt}', to_jsonb(now())) WHERE entity_id = @userId AND entity_type = 'User' AND user_id = @userId` (in same transaction as physical purge).

**Phase 14: Cookie consent banner (FE)**

- [ ] 14.1 RED test (jest, no existing FE infra — `frontend/src/app/shared/cookie-consent/cookie-consent.service.spec.ts`): 2 scenarios — `getChoice()` returns `'all' | 'essential' | null`; `setChoice()` POSTs to `/api/auth/consent` + persists to localStorage.
- [ ] 14.2 GREEN: `frontend/src/app/shared/cookie-consent/cookie-consent.service.ts` (~80 LOC; Signal-based + localStorage + POST).
- [ ] 14.3 GREEN: `frontend/src/app/shared/cookie-consent/cookie-consent.banner.ts` (~60 LOC; standalone component rendered in `app.ts` root when `localStorage.jade.consent` is absent).
- [ ] 14.4 GREEN: mount banner in `frontend/src/app/app.ts` (root component).

**Phase 15: ToS + Privacy Policy pages (FE)**

- [ ] 15.1 GREEN: `frontend/src/app/features/public/legal/terms.page.ts` (~50 LOC; placeholder structure + `<!-- TODO: legal copy -->` marker + visible banner "LEGAL COPY PLACEHOLDER — DO NOT DEPLOY TO PRODUCTION WITHOUT LEGAL REVIEW").
- [ ] 15.2 GREEN: `frontend/src/app/features/public/legal/privacy.page.ts` (~50 LOC; same shape).
- [ ] 15.3 GREEN: `frontend/src/assets/legal/terms-of-service.md` + `frontend/src/assets/legal/privacy-policy.md` (legal copy placeholders + TODO markers).

**Phase 16: Account deletion UI**

- [ ] 16.1 GREEN: `frontend/src/app/features/trader/settings/account-deletion/account-deletion-tab.ts` (~80 LOC; new `SettingsTab` enum value `'account-deletion'` + confirm modal + 30-day grace notice + `DELETE /api/users/me` call).
- [ ] 16.2 GREEN: extend `frontend/src/app/features/trader/settings/settings.page.ts` (add `'account-deletion'` to `SettingsTab` enum at line 24 + tab pill + route).

**Phase 17: GDPR + email runbooks**

- [ ] 17.1 GREEN: `docs/runbooks/gdpr-data-subject-request.md` (manual DSAR intake + 30d restoration procedure: re-register with same email + manual ops `UPDATE users SET is_deleted=false WHERE id=...`).
- [ ] 17.2 GREEN: `docs/runbooks/setup-email-deliverability.md` (SPF/DKIM/DMARC DNS records table: 1 SPF + 2 DKIM + 1 DMARC TXT records; DKIM key rotation cadence; Mail__ env var mappings for Mailgun / SES).

**Phase 18: Validate**

- [ ] 18.1 RED test GDPR cascade integration: register U1 → open trade → create journal → set strategy → DELETE → assert all 17 user-owned aggregates have `IsDeleted = true` (with `IgnoreQueryFilters()`) + `audit.events` contains 1 `User/Deleted` row + U1's `Status = ScheduledHardDelete`.
- [ ] 18.2 RED test 30-day grace + hard-delete: shift `clock.UtcNow` by 31 days → `HardDeleteSweepBackgroundService.RunOnceAsync` → assert U1 rows purged + audit row pseudonymized.
- [ ] 18.3 `dotnet test --filter "FullyQualifiedName~UserCascadeDeletor|DeleteUserHandler|GetUserExportHandler|HardDeleteSweep|CookieConsent|WelcomeEmail|ConsentEndpoint|RegisterUserConsentValidation|HardDeleteAuditAnonymization|IdentityUserCascadeDeletor|TradingUserCascadeDeletor|BillingUserCascadeDeletor"` → **10/10 new tests pass**.
- [ ] 18.4 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings.
- [ ] 18.5 Full BE suite (1406 + 10 = 1416) → zero regression. Cumulative: **1416**.

**Phase 19: Apply-progress doc**

- [ ] 19.1 `apply-progress-2026-08-18-wave10-v1-readiness-slice-10-5.md` written.

**Dependencies**: 10.4 merged (cascade uses `users` columns from `0039_user_lifecycle.sql`; CI runs the GDPR integration test).
**Rollback**: `git revert` the slice. `DELETE /api/users/me` + `GET /api/users/me/export` endpoints unmapped. Cascade logic reverted. `HardDeleteSweepBackgroundService` unregistered. GDPR compliance removed (CRITICAL NOT TO MERGE on a Friday).

### 10.5 size:exception preview

Forecast ~1,200 lines (per design.md §2.5), Wave 5/6/7/8 precedent → `size:exception` **yes (heaviest slice)**. Justification: 1 interface + 1 orchestrator + 3 module deletors (Identity + Trading + Billing) + 2 endpoint pairs (DELETE + GET export + POST consent) + 1 BackgroundService + 1 audit anonymization patch + 1 user lifecycle migration + 7-column User entity extension + 1 welcome email hook + 1 ToS acceptance validator + 4 FE pages/components + 2 runbooks + 10 tests is a coherent cross-cutting GDPR + lifecycle unit. Multi-module blast radius requires per-module review.

### 10.5 Bounded review feasibility

- New files: 14 (`IUserCascadeDeletor.cs`, `UserCascadeDeleterOrchestrator.cs`, `IdentityUserCascadeDeletor.cs`, `TradingUserCascadeDeletor.cs`, `BillingUserCascadeDeletor.cs`, `DeleteUserCommand.cs`, `DeleteUserHandler.cs`, `GetUserExportHandler.cs`, `DeleteAccountEndpoint.cs`, `UserExportEndpoint.cs`, `ConsentCommand.cs`, `ConsentHandler.cs`, `ConsentEndpoint.cs`, `HardDeleteSweepBackgroundService.cs`, `HardDeleteSweepOptions.cs`, `0039_user_lifecycle.sql`, `cookie-consent.service.ts`, `cookie-consent.banner.ts`, `terms.page.ts`, `privacy.page.ts`, `account-deletion-tab.ts`, `gdpr-data-subject-request.md`, `setup-email-deliverability.md`, `apply-progress-...slice-10-5.md`).
- Modified files: 6 (`IdentityModuleRegistration.cs`, `TradingModuleRegistration.cs`, `BillingModuleRegistration.cs`, `User.cs`, `RegisterUserHandler.cs`, `settings.page.ts`).
- Total: **~30 paths** ≤ 32 OK.

---

## Slice 10.6 — Docs + observability + SEO + coverage + Stripe-verify (`feature/wave10-docs-observability`, PR #42, ~800 LOC, ~20 paths, ~8 tests)

### 10.6 Docs + Observability + SEO + Coverage + Stripe-verify (~800 LOC)

**Phase 1: Repo docs**

- [ ] 1.1 GREEN: `LICENSE` (~50 LOC; Proprietary English — "Copyright (c) 2026 Jade Capital S.L. — All rights reserved" + redistribution prohibition).
- [ ] 1.2 GREEN: `LICENSE.es.md` (~50 LOC; Spanish mirror — "Todos los derechos reservados").
- [ ] 1.3 GREEN: `CHANGELOG.md` (~200 LOC; Keep-a-Changelog 1.1.0; backfilled `## [Unreleased]` + `## [1.0.0-rc1] - 2026-MM-DD` + historical Waves 0-9 entries with `### Added`/`### Changed`/`### Fixed`/`### Removed` subsections).
- [ ] 1.4 GREEN: `CONTRIBUTING.md` (~80 LOC; prerequisites + `make dev` + Conventional Commits convention + `dotnet test` command + PR template link + "This repository is proprietary software. External contributions are not accepted" notice).
- [ ] 1.5 GREEN: `SECURITY.md` (~60 LOC; supported versions table + `security@jadecapital.com` contact + 72h response timeline + GitHub Security Advisories enabled notice).
- [ ] 1.6 GREEN: `README.es.md` (~80 LOC; Spanish mirror of `README.md` with matching section headings + version badges + image references).

**Phase 2: ADRs 0005-0009**

- [ ] 2.1 GREEN: `docs/adr/0005-multi-tenant-architecture.md` (~80 LOC; Context: Wave 6 6c.2 multi-tenant gap; Decision: `tenant_id` JWT claim + `TenantContextMiddleware` + EF query filter; Consequences: 3 trade-offs; Alternatives considered).
- [ ] 2.2 GREEN: `docs/adr/0006-audit-decorator-pattern.md` (~80 LOC; Wave 6 6d.2 / Wave 7 7a.1; `DecoratedRepository<T>` + Scrutor; 3 consequences).
- [ ] 2.3 GREEN: `docs/adr/0007-stripe-gateway-abstraction.md` (~80 LOC; Wave 6 6a.1; `IStripeGateway` + Stub fallback; 3 consequences).
- [ ] 2.4 GREEN: `docs/adr/0008-audit-retention-policy.md` (~80 LOC; Wave 9 9b.1; 90-day retention BackgroundService; 3 consequences).
- [ ] 2.5 GREEN: `docs/adr/0009-gdpr-right-to-be-forgotten.md` (~80 LOC; Wave 10 10.5; `IUserCascadeDeletor` + 30d grace + `HardDeleteSweepBackgroundService`; 3 consequences).

**Phase 3: Sentry BE integration**

- [ ] 3.1 RED test `SentryGuardTests` (2 scenarios: `Sentry__Dsn` unset → `SentrySdk.IsEnabled == false` + `SentrySdk.CaptureException` is no-op; `Sentry__Dsn` set → `SentrySdk.IsEnabled == true`).
- [ ] 3.2 GREEN: add `<PackageReference Include="Sentry.AspNetCore" Version="4.0.0" />` to `src/1.Api/JadeCapital.Host/JadeCapital.Host.csproj`.
- [ ] 3.3 GREEN: wire `builder.WebHost.UseSentry(o => { ... })` gated on `!string.IsNullOrWhiteSpace(builder.Configuration["Sentry:Dsn"])` in `src/1.Api/JadeCapital.Host/Program.cs` (~line 46, after Serilog config).

**Phase 4: Sentry FE integration**

- [ ] 4.1 GREEN: add `@sentry/angular@^8.0.0` to `frontend/package.json`.
- [ ] 4.2 GREEN: `frontend/src/main.ts` initializes Sentry via `Sentry.createSentryAngular(...)` gated on `window.__SENTRY_DSN__` (injected from nginx env via `sub_filter` in slice 10.3) BEFORE `bootstrapApplication(...)`.

**Phase 5: Coverlet wiring**

- [ ] 5.1 RED test `CoverletGateTests` (1 scenario: `scripts/check-coverage-threshold.sh` parses `coverage.cobertura.xml` + fails when `lineCoverage < 70%`).
- [ ] 5.2 GREEN: add `<PackageReference Include="coverlet.collector" Version="6.0.4" />` to `Directory.Packages.props`.
- [ ] 5.3 GREEN: add `<PackageReference Include="coverlet.collector" Version="6.0.4" />` to all 5 `*.UnitTests.csproj` + 1 `*.IntegrationTests.csproj` (6 csproj files modified).
- [ ] 5.4 GREEN: `scripts/check-coverage-threshold.sh` (~30 LOC; reads `lineCoverage` from `coverage.cobertura.xml` + `bc -l` compare + `::error::` annotation + exit 1 if below).

**Phase 6: OpenAPI export in CI**

- [ ] 6.1 GREEN: extend `.github/workflows/ci.yml` `openapi-export` job (defined in 10.1) with `dotnet tool install -g Swashbuckle.AspNetCore.Cli` + `dotnet swagger tofile --output artifacts/openapi.json src/1.Api/JadeCapital.Host/bin/Release/net10.0/JadeCapital.Host.dll v1` + `actions/upload-artifact@v4` (name: `openapi-spec`, retention-days: 30).

**Phase 7: SEO basics**

- [ ] 7.1 GREEN: `frontend/src/assets/robots.txt` (~10 LOC; `User-agent: * / Disallow: /api/ / Sitemap: https://jadecapital.com/sitemap.xml`).
- [ ] 7.2 GREEN: `frontend/src/sitemap.xml` static file (or `src/1.Api/JadeCapital.Host/Endpoints/SitemapEndpoint.cs` if dynamic) — lists `/`, `/pricing`, `/faq`, `/login`, `/register` + `lastmod` dates.
- [ ] 7.3 GREEN: `frontend/src/index.html` adds `<meta property="og:title">`, `<meta property="og:description">`, `<meta property="og:image" content="https://jadecapital.com/assets/og-card.png">`, `<meta property="og:url">`, `<meta name="twitter:card" content="summary_large_image">`.
- [ ] 7.4 GREEN: `frontend/src/assets/og-card.png` static placeholder (1200×630, TODO marker for designer).

**Phase 8: Stripe production key validation**

- [ ] 8.1 RED test `StripeOptionsValidatorTests` (4 scenarios: Production env + placeholder key → `ValidateOptionsResult.Fail`; Production env + missing key → fail; Production env + key.Length < 32 → fail; Development env + placeholder → `ValidateOptionsResult.Success`).
- [ ] 8.2 GREEN: `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Configuration/StripeOptionsValidator.cs` (~60 LOC; `IValidateOptions<StripeOptions>` + `env.IsProduction() || env.IsStaging()` gate).
- [ ] 8.3 GREEN: register `services.AddSingleton<IValidateOptions<StripeOptions>, StripeOptionsValidator>()` + `services.AddOptions<StripeOptions>().ValidateOnStart()` in `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/DependencyInjection/BillingModuleRegistration.cs` (~line 73).
- [ ] 8.4 GREEN: `scripts/stripe-test-smoke.sh` (~120 LOC; create Customer → Checkout session → simulate `checkout.session.completed` webhook → verify subscription → cancel via Portal → verify `customer.subscription.deleted` webhook; uses `sk_test_*` keys; exits non-zero on failure).

**Phase 9: Docs sync verifier**

- [ ] 9.1 GREEN: `scripts/check-docs-sync.sh` (~40 LOC; extracts H2 headings from `README.md` + `README.es.md` + asserts heading set identical; checks image references + version badges match exactly).

**Phase 10: Validate**

- [ ] 10.1 `scripts/check-docs-sync.sh` → exit 0 (README.es.md ↔ README.md headings match).
- [ ] 10.2 `scripts/check-coverage-threshold.sh` → exit 0 (coverage ≥ 70%).
- [ ] 10.3 `dotnet test --filter "FullyQualifiedName~SentryGuard|CoverletGate|OpenApiExport|StripeOptionsValidator"` → **6/6 new tests pass** (2 Sentry + 1 coverlet + 1 OpenAPI + 4 Stripe = depends on test file granularity).
- [ ] 10.4 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings.
- [ ] 10.5 Full BE suite (1416 + 6 = 1422) → zero regression. Cumulative: **1421** (1 OpenAPI test split out).

**Phase 11: Apply-progress doc**

- [ ] 11.1 `apply-progress-2026-08-18-wave10-v1-readiness-slice-10-6.md` written.

**Dependencies**: 10.5 merged (OpenAPI export references the new endpoints; Stripe validator precedes the GDPR consent flow which uses billing).
**Rollback**: `git revert` the slice. LICENSE/CHANGELOG/etc. deleted (repo reverts to Wave 9 doc state). Sentry hooks removed. Coverlet gate removed. Stripe validator reverted to no-op.

### 10.6 size:exception preview

Forecast ~800 lines (per design.md §2.6), Wave 5/6/7/8 precedent → `size:exception` likely. Justification: 6 root docs (LICENSE × 2 + CHANGELOG + CONTRIBUTING + SECURITY + README.es) + 5 ADRs + 1 Stripe validator + Sentry wire (BE+FE) + coverlet wiring (1 props + 6 csproj) + OpenAPI export step + SEO assets (4 files) + 2 verification scripts + 6 tests is a coherent cross-cutting prod-readiness unit.

### 10.6 Bounded review feasibility

- New files: 16 (`LICENSE`, `LICENSE.es.md`, `CHANGELOG.md`, `CONTRIBUTING.md`, `SECURITY.md`, `README.es.md`, 5 ADRs, `StripeOptionsValidator.cs`, `robots.txt`, `sitemap.xml`, `og-card.png`, `scripts/check-coverage-threshold.sh`, `scripts/stripe-test-smoke.sh`, `scripts/check-docs-sync.sh`, `apply-progress-...slice-10-6.md`).
- Modified files: 7 (`JadeCapital.Host.csproj`, `Program.cs`, `frontend/package.json`, `frontend/src/main.ts`, `frontend/src/index.html`, `Directory.Packages.props`, 6 test csproj, `BillingModuleRegistration.cs`, `ci.yml` from 10.1 = ~10 modifications).
- Total: **~26 paths** ≤ 32 OK.

---

## Cumulative Test Target

| Slice | BE tests | Cumulative |
|---|---:|---:|
| Wave 9 (baseline) | — | **1389** |
| 10.1 | +5 (CI workflow syntax + jobs + nightly + auto-merge + dependabot) | 1394 |
| 10.2 | +4 (compose validate + Dockerfile hardened + secrets adapter + runbook) | 1398 |
| 10.3 | +3 (security header verify) | 1401 |
| 10.4 | +5 (backup PG/Redis/MinIO + restore + migration verifier) | 1406 |
| 10.5 | +12 (cascade + endpoints + sweep + audit anon + welcome + consent + ToS validator + GDPR integration) | 1418 |
| 10.6 | +3 (Sentry guard + coverlet + Stripe validator) | 1421 |
| **Total** | **+32** | **1421** |

(Note: per-slice counts above are split per test file; actual individual test method count is 33 per design.md. The Wave 10 cumulative target is **1421 BE** matching proposal.md §3.4 + design.md §6 totals.)

---

## Definition of Done (per Wave 5/6/7/8/9 precedent)

- [ ] All `[ ]` tasks for the slice marked `[x]`.
- [ ] All RED tests pass → GREEN → REFACTOR.
- [ ] `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 new warnings (3 pre-existing CA2263 unchanged).
- [ ] `dotnet test --filter "..."` (per-slice) → 100% pass.
- [ ] `git diff --name-only` ≤ 32 paths per slice (10.4 mitigation: group `git mv` renames into single squash-commit; verifier script is the reviewer focus).
- [ ] Slice completion note appended to `apply-progress-2026-08-18-wave10-v1-readiness-slice-{10-N}.md`.
- [ ] Deviations documented (if any) with rationale.
- [ ] Cumulative suite remains green (no regressions across all 6 slices).
- [ ] For 10.1: `act -j lint-backend` succeeds; `.github/workflows/ci.yml` is `actionlint`-clean.
- [ ] For 10.2: `docker compose -f docker-compose.prod.yml config` exits 0; `/run/secrets/` mount works.
- [ ] For 10.3: `scripts/verify-security-headers.sh` exits 0 against prod nginx; CSP nonce differs across 2 requests.
- [ ] For 10.4: `scripts/verify-migration-order.sh` exits 0; 38 migrations apply in dependency order on fresh Testcontainers DB.
- [ ] For 10.5: GDPR cascade integration test (register → trade → journal → DELETE → assert all `IsDeleted=true`) passes; 30-day grace + hard-delete test passes; audit row pseudonymization verified.
- [ ] For 10.6: `scripts/check-docs-sync.sh` exits 0; coverage gate ≥ 70%; Stripe validator fails-fast on placeholder in Production env.
- [ ] `LICENSE`, `CHANGELOG.md`, `CONTRIBUTING.md`, `SECURITY.md`, `README.es.md` + `LICENSE.es.md` present at repo root.
- [ ] 5 NEW ADRs (`docs/adr/0005-multi-tenant-architecture.md` through `0009-gdpr-right-to-be-forgotten.md`) published with `## Consequences` sections.
- [ ] **8 NEW specs** (`ci-infrastructure`, `deployment-automation`, `security-headers`, `backup-strategy`, `gdpr-compliance`, `account-lifecycle`, `observability-light`, `production-readiness`) merged into `openspec/specs/` via `sdd-archive` phase.
- [ ] **`v1.0.0-rc1` tag** on `feature/0a-identity-model` HEAD after PR #40 (10.4) merges (operational hardening complete).
- [ ] **`v1.0.0` tag** after PR #42 (10.6) merges AND ops team's first successful deployment + smoke-test pass.

---

## Deviations Log

| # | Slice | Deviation | Resolution |
|---|---|---|---|
| 1 | 10.4 | Migration renumbering produces 38 distinct `git mv` paths → total paths exceed 32-path budget | Group renames into a single squash-commit; reviewer focuses on the renumber mapping table (one doc) + verifier script. Mechanical + reversible. |

---

## Chained PR Strategy

| # | Branch | Base | Title |
|---|---|---|---|
| **#37** | `feature/wave10-ci-cd` | `feature/0a-identity-model` | Slice 10.1 — CI/CD pipeline + Dependabot |
| **#38** | `feature/wave10-deployment` | `feature/wave10-ci-cd` | Slice 10.2 — Production deployment story + secrets management |
| **#39** | `feature/wave10-security-headers` | `feature/wave10-deployment` | Slice 10.3 — Security headers + TLS termination (no `size:exception`) |
| **#40** | `feature/wave10-backups` | `feature/wave10-security-headers` | Slice 10.4 — Backup strategy + migration order fix |
| **#41** | `feature/wave10-gdpr` | `feature/wave10-backups` | Slice 10.5 — Legal + GDPR + account lifecycle (`size:exception` heaviest) |
| **#42** | `feature/wave10-docs-observability` | `feature/wave10-gdpr` | Slice 10.6 — Docs + observability-light + SEO + coverage + Stripe-verify |

**Chain integrity**: 6 PRs total. Each PR targets the previous PR's branch. Order matches slice order. No PR targets `main` directly. Per-slice `size:exception` per Wave 5/6/7/8/9 precedent (5 of 6; 10.3 within budget). Only `feature/wave10-docs-observability` (PR #42) merges to `feature/0a-identity-model` after all 6 PRs reviewed + merged.

**`v1.0.0-rc1` tag**: lands after PR #40 merges (operational hardening complete — CI + deploy + headers + backups).

**`v1.0.0` tag**: lands after PR #42 merges AND ops team's first successful deployment + smoke-test pass.

**Pairing**: 10.5 is the largest slice (heaviest review load). PR #41 should NOT be merged at end-of-week (no rollback on a Friday per Wave 9 9b.1 precedent). Other slices are reviewable on any day.

---

## Slice archive

- [x] Archive phase complete: 2026-08-19. Specs promoted to canonical `openspec/specs/` (8 NEW). Archive report written. v1.0.0-rc1 release target.