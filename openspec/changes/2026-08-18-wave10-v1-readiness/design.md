# Design: Wave 10 — v1 Readiness (6 slices, ~4,350 LOC, 8 NEW specs)

**Change**: `2026-08-18-wave10-v1-readiness`
**Base branch**: `feature/0a-identity-model @ 49207e2` (Wave 9 archived)
**Mode**: hybrid (OpenSpec + engram)
**Strategy**: `feature-branch-chain` with `size:exception` per slice (Wave 5/6/7/8/9 precedent)
**Strict TDD**: ACTIVE — every spec scenario is RED-tested before GREEN impl
**Reference docs**: `explore.md` (487 LOC) · `proposal.md` (339 LOC) · 8 NEW specs in `specs/`

---

## 1. Architecture overview

### 1.1 What Wave 10 adds

Wave 10 is **operational + compliance hardening** — no new product features, no new modules, no new architectural primitives. Every change lands inside the existing clean-modular-monolith (`1.Api/Host` + 5 modules + 2 Shared layers). New surfaces are infra-only: `.github/`, `infrastructure/`, `scripts/`, `docs/`, `frontend/src/`, with one new module-level abstraction (`IUserCascadeDeletor`) that lives in `Identity.Application.Abstractions`.

```
                      ┌──────────────────────────── v1.0.0-rc1 ────────────────────────────┐
                      │                                                                   │
   .github/  ────►  ┌────────────────┐    ┌──────────────────┐    ┌────────────────┐       │
   workflows/       │  CI merge-gate │───►│  Build / test /  │───►│  Coverlet +    │       │
   dependabot.yml   │  (4 jobs)      │    │  swagger.json    │    │  OpenAPI export│       │
   PR template      └────────────────┘    └──────────────────┘    └────────────────┘       │
                      │                                                                   │
   infrastructure/ ─► ┌────────────────┐    ┌──────────────────┐    ┌────────────────┐       │
   nginx.conf         │  Edge reverse  │───►│  TLS termination │───►│  CSP / HSTS /  │       │
   Caddyfile          │  proxy (Caddy) │    │  Let's Encrypt   │    │  Permissions-  │       │
   docker-compose     └────────────────┘    └──────────────────┘    │  Policy        │       │
                      │                                              └────────────────┘       │
   scripts/  ───────► ┌────────────────┐    ┌──────────────────┐    ┌────────────────┐       │
   backup-*.sh        │  pg_dump + WAL │───►│  MinIO (DR       │───►│  Restore + DR  │       │
   restore-*.sh       │  + BGSAVE + mc │    │  stack)          │    │  drill log     │       │
   verify-*.sh        └────────────────┘    └──────────────────┘    └────────────────┘       │
                      │                                                                   │
   src/2.Modules/ ───► ┌────────────────┐    ┌──────────────────┐    ┌────────────────┐       │
   (NEW cascade)      │  DELETE /users │───►│  IUserCascade    │───►│  HardDelete    │       │
                      │  /me + /export │    │  Deletor (3 mod) │    │  SweepBG (30d) │       │
                      └────────────────┘    └──────────────────┘    └────────────────┘       │
                      │                                                                   │
   frontend/src/  ───► ┌────────────────┐    ┌──────────────────┐    ┌────────────────┐       │
   (legal + consent)   │  Cookie banner │───►│  Terms / Privacy │───►│  Account del   │       │
                      │  (localStorage)│    │  pages (TODO)    │    │  UI (settings) │       │
                      └────────────────┘    └──────────────────┘    └────────────────┘       │
                      └───────────────────────────────────────────────────────────────────┘
```

### 1.2 Wave 10 builds on Wave 9

| Wave 9 artifact (in `feature/0a-identity-model @ 49207e2`) | Wave 10 reuse |
|---|---|
| `audit.events` table (separate `AuditDbContext`, append-only) | 10.5 GDPR cascade emits 1 `User/Deleted` row + 1 pseudonymized `User/HardDeleted` row per purged user. Per-cascade audit row catches missed `IUserCascadeDeletor` registrations. |
| `IAuditLogger` + `DecoratedRepository<T>` (Scrutor 4.2.2) | 10.5 does NOT add new decorators. Reuses the typed-decorator pattern from Wave 7 7a.1 (User/Tenant/RiskProfile). |
| `AuditRetentionBackgroundService` (Wave 9 9b.1) at `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/BackgroundServices/AuditRetentionBackgroundService.cs` | 10.5's `HardDeleteSweepBackgroundService` is a **sibling** BackgroundService — same shape (per-cycle `IServiceScopeFactory` + `RunOnceAsync` public + `[0, +30min]` jitter + `try/catch + LogError + continue` isolation), different selection criterion (`status = 2 AND scheduled_for_hard_delete_at <= UtcNow`, not `occurred_at < cutoff`). |
| `Testcontainers.PostgreSql` + `WebApplicationFactory<Program>` (Wave 6 6f) | 10.1's `test-integration` CI job + 10.5's GDPR cascade integration test. |
| `multi-tenant` spec (Wave 6c.2, `tenant_id` JWT claim + `TenantContextMiddleware` + `ITenantContext`) | 10.5's `GET /api/users/me/export` reads `ITenantContext.CurrentUserId` to scope the export — cross-tenant reads return 404 (defense in depth). |
| `soft-delete-audit` spec (`ISoftDelete` + EF global query filter) | 10.5's cascade uses `ISoftDelete.MarkDeleted(userId, clock)` on 17 user-owned aggregates (3 modules). Hard-delete in 10.5 bypasses the filter and physically removes rows. |
| `IStripeGateway` (Wave 6a.1) | 10.6's `IValidateOptions<StripeOptions>` runs at startup against the same options POCO. No new gateway interface. |

### 1.3 Module dependency graph — new edges from Wave 10

Wave 10 adds **zero new project-to-project references** in the .NET solution. All new module-level dependencies are interface-only:

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                    Identity.Application.Abstractions (NEW)                    │
│                  IUserCascadeDeletor (interface, no impl)                    │
└───────────────┬──────────────────┬─────────────────────┬──────────────────────┘
                │                  │                     │
        implements           implements           implements
                │                  │                     │
        ┌───────▼──────┐   ┌───────▼────────┐   ┌───────▼──────────┐
        │   Identity   │   │    Trading     │   │     Billing      │
        │   (owns      │   │   (owns Trade/ │   │   (owns Subs /   │
        │    User)     │   │   Account /    │   │   StripeCustomer)│
        │              │   │   Journal /    │   │                  │
        │              │   │   Strategy)    │   │                  │
        └──────┬───────┘   └───────┬────────┘   └───────┬──────────┘
               │                  │                     │
               └──────────────────┼─────────────────────┘
                                  ▼
               ┌─────────────────────────────────────┐
               │   UserCascadeDeleterOrchestrator    │
               │   (composed in IdentityModule-      │
               │    Registration, collects all       │
               │    IEnumerable<IUserCascadeDeletor>)│
               └────────────────┬────────────────────┘
                                ▼
               ┌─────────────────────────────────────┐
               │  HardDeleteSweepBackgroundService   │
               │  (daily tick, idempotent, per-user  │
               │   transaction)                      │
               └─────────────────────────────────────┘
```

The orchestrator lives in `Identity.Infrastructure` so no other module gets a hard dep on `Identity.Application`. Modules register their own deletor; Identity aggregates them. This is the same DI pattern Wave 9 uses for `ISoftDeleteProviderRegistry` (see `IdentityModuleRegistration.cs:144-148`).

---

## 2. Slice designs

### 2.1 Slice 10.1 — CI/CD pipeline (`feature/wave10-ci-cd`, PR #37, ~800 LOC)

**Closes gaps**: A1 (no CI/CD) · A10 (no vulnerability scanning) · B6 partial (integration tests in CI)
**Spec**: `specs/ci-infrastructure/spec.md` (5 scenarios)
**Target LOC**: 800 (`size:exception` — CI infra precedent Wave 5/6/8)

#### Workflow architecture

```yaml
# .github/workflows/ci.yml — 4 parallel jobs + 1 sequential
name: ci
on:
  pull_request: { branches: [feature/0a-identity-model, feature/wave10-*] }
  push:        { branches: [feature/0a-identity-model] }
concurrency: { group: ci-${{ github.workflow }}-${{ github.ref }}, cancel-in-progress: true }

jobs:
  lint-backend:    { uses: ./.github/actions/setup-dotnet, runs: dotnet format --verify-no-changes }
  test-backend:    { uses: ./.github/actions/setup-dotnet, runs: dotnet test --nologo --filter 'FullyQualifiedName!~IntegrationTests' }
  test-integration:{ services: [postgres, redis], runs: dotnet test --filter 'FullyQualifiedName~IntegrationTests' }
  test-frontend:   { runs: npm --prefix frontend ci && npm test -- --ci && npm run build }
  openapi-export:  { needs: [test-backend], runs: dotnet swagger tofile --output artifacts/openapi.json, uploads artifact }
```

#### Dependabot

```yaml
# .github/dependabot.yml
version: 2
updates:
  - package-ecosystem: nuget, directory: /
    schedule: { interval: weekly }
    open-pull-requests-limit: 10
    groups: { patches: { patterns: ['*'], update-types: ['minor','patch'] } }
  - package-ecosystem: npm, directory: /frontend
    schedule: { interval: weekly }, open-pull-requests-limit: 10
  - package-ecosystem: github-actions, directory: /
    schedule: { interval: monthly }
```

Auto-merge workflow (`.github/workflows/dependabot-auto-merge.yml`) gates on `groups.patches` + CI green — no manual review for patch + minor BE updates. Major updates require human review (per spec §Requirement:Dependabot).

#### Branch protection

Per `specs/ci-infrastructure/spec.md` §"Branch protection", `feature/0a-identity-model` requires:
- `lint-backend`, `test-backend`, `test-integration`, `test-frontend`, `openapi-export` all green
- 1 approving review
- Linear history (squash or rebase merge)
- `gh pr merge --auto` rejected when any check red (Scenario 4)

#### Integration tests in CI

The `test-integration` job uses GitHub-hosted `services:` block (Postgres 16 + Redis 7). Testcontainers is unnecessary for this job — the service hostnames (`postgres`, `redis`) match what Testcontainers auto-provisions locally. Saves ~30s of container startup. The CI Postgres + Redis are ephemeral per run (no volume), and `migrate.Dockerfile` runs as a one-off init step via `services` health condition. If this proves too slow, Wave 11+ moves to Testcontainers with `ubuntu-latest-4-cores` (extra cost, see `explore.md:§A1` risk).

#### Nightly scan

```yaml
# .github/workflows/nightly.yml
on: { schedule: [{ cron: '0 3 * * *' }], workflow_run: { workflows: ['ci'], types: [completed] } }
jobs:
  vuln-scan:
    runs-on: ubuntu-latest
    steps:
      - run: dotnet list package --vulnerable --include-transitive
      - run: npm --prefix frontend audit --audit-level=high
      - uses: zaproxy/action-baseline@v0.12.0
        with: { target: 'http://localhost:8080', rulesFileName: '.zap/rules.tsv' }
```

Failures create GitHub Issues (not PR comments) to avoid blocking the merge-gate.

#### Files

| Path | Action | Purpose |
|---|---|---|
| `.github/workflows/ci.yml` | Create | 4 parallel jobs + openapi-export (5 jobs) |
| `.github/workflows/nightly.yml` | Create | OWASP ZAP + `dotnet list package --vulnerable` + `npm audit` |
| `.github/workflows/dependabot-auto-merge.yml` | Create | Auto-merge patch+minor NuGet PRs after CI green |
| `.github/dependabot.yml` | Create | NuGet + npm + github-actions ecosystems |
| `.github/pull_request_template.md` | Create | Spec link checkbox + test plan + migration notice |
| `.github/actions/setup-dotnet/action.yml` | Create | Composite action: `actions/setup-dotnet@v4` + NuGet cache key on `Directory.Build.props` hash + `global.json` |
| `.zap/rules.tsv` | Create | OWASP ZAP baseline rules (low-severity silenced) |

#### Testing strategy

| Layer | What | How |
|---|---|---|
| Workflow syntax | `actionlint` + GitHub's schema validator | Lint step in `lint-backend` job (no Docker required) |
| Service health | Postgres + Redis up + Testcontainers resolves service hostname | First integration test asserts `db.CanConnectAsync()` |
| Dependabot groups | Patch versions auto-merge | Manual repo-admin test: open a fake patch PR, watch auto-merge fire after CI |
| Branch protection | PR with red check cannot merge | `gh pr merge --auto` against a known-red PR → 403 |

---

### 2.2 Slice 10.2 — Production deployment + secrets (`feature/wave10-deployment`, PR #38, ~800 LOC)

**Closes gaps**: A2 (prod secrets) · A5 (no prod deployment story)
**Spec**: `specs/deployment-automation/spec.md` (6 scenarios)
**Target LOC**: 800 (`size:exception`)

#### `docker-compose.prod.yml` — design rules

The prod compose **extends** the dev compose, never replaces it. Differences from `docker-compose.yml`:

| Service | Dev (`docker-compose.yml`) | Prod (`docker-compose.prod.yml`) |
|---|---|---|
| `mailpit` | present (port 8025) | **removed** — replaced by `Mailgun__Host/Port/Key` envs on `api` |
| `postgres` ports | `["5432:5432"]` (dev convenience) | `expose: ["5432"]` only (no host port) |
| `redis` ports | `["6379:6379"]` | `expose: ["6379"]` only |
| `api` | `migrate` via `depends_on` | same; + `read_only: true`, `tmpfs: ["/tmp"]` |
| `nginx` / `caddy` | absent (frontend on 4200) | **NEW** edge service (TLS + reverse proxy) |
| All services | `restart: unless-stopped` | `restart: always` |
| All services | no healthcheck (or basic) | **MUST** declare `healthcheck:` (per spec §Requirement:docker-compose.prod) |
| `api` env | `.env` plain | `secrets:` block + `environment:` for non-secret config |

The `secrets:` block replaces plain envs for the 7 secrets listed in `specs/deployment-automation/spec.md` §Requirement:Production secrets: `jwt_access_token_secret`, `jwt_refresh_token_secret`, `postgres_password`, `redis_password`, `minio_root_password`, `stripe_secret_key`, `stripe_webhook_secret`.

#### Secrets adapter pattern

`ITenantContext`-style abstraction is overkill for secrets (config is already pluggable). Instead, Wave 10 ships a **`DockerSecretConfigurationProvider : ConfigurationProvider`** that mounts `/run/secrets/<name>` files and projects them as `ASPNETCORE_` env-var-style keys. The provider is wired in `Program.cs:50` BEFORE the `Configure<JwtOptions>` call (see `Program.cs:50-53` for the existing `JwtOptions` registration) so the existing `ValidateOnStart` chain picks up the secret value without code changes:

```csharp
// src/1.Api/JadeCapital.Host/Configuration/DockerSecretConfigurationProvider.cs
public sealed class DockerSecretConfigurationProvider : ConfigurationProvider
{
    private readonly string _secretsDir;
    public DockerSecretConfigurationProvider(string secretsDir = "/run/secrets")
        => _secretsDir = secretsDir;
    public override void Load()
    {
        if (!Directory.Exists(_secretsDir)) return; // dev / sandbox: skip silently
        foreach (var file in Directory.EnumerateFiles(_secretsDir))
        {
            var key = $"DOCKER_SECRET_{Path.GetFileName(file).ToUpperInvariant().Replace('-', '_')}";
            Data[key] = File.ReadAllText(file).Trim();
        }
    }
}
```

Vault / Doppler / AWS Secrets Manager migration is **config-only** (the runbook documents the env-var mappings). No code change required when the ops team swaps providers — only a different sidecar process + a different `appsettings.Production.json` mapping.

#### `Dockerfile.prod` — BE

Multi-stage build, follows Wave 4 4b.0 + Wave 9 hardening:

```dockerfile
# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src
COPY Directory.Packages.props Directory.Build.props global.json ./
COPY src/1.Api/JadeCapital.Host/JadeCapital.Host.csproj src/1.Api/JadeCapital.Host/
# (project references for all 5 modules)
RUN dotnet restore --locked-mode

FROM restore AS build
COPY . .
RUN dotnet publish src/1.Api/JadeCapital.Host -c Release -o /app/publish \
      --no-restore /p:PublishTrimmed=false /p:SelfContained=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN useradd --system --uid 1001 --shell /sbin/nologin jade
WORKDIR /app
COPY --from=build --chown=jade:jade /app/publish ./
USER jade
HEALTHCHECK --interval=30s --timeout=3s --retries=3 \
  CMD dotnet /app/JadeCapital.Host.dll --health-check || exit 1
EXPOSE 8080
ENTRYPOINT ["dotnet", "/app/JadeCapital.Host.dll"]
```

#### `Dockerfile.prod` — FE

Two-stage: `node:20-alpine` build → `nginxinc/nginx-unprivileged:1.27-alpine` runtime serving `dist/` + `frontend/nginx.conf` (hardened with CSP headers from slice 10.3).

#### Runbook

`docs/runbooks/deploy.md` covers (per `specs/deployment-automation/spec.md` §Requirement:Deployment runbook): (a) first-time deploy (DNS, certbot, secrets bootstrap), (b) routine deploy (`docker compose pull && up -d`), (c) rollback (`scripts/restore-postgres.sh <latest>` + image tag revert), (d) incident response (log locations, on-call rotation, status page), (e) DR pointer to `disaster-recovery.md` (shipped in 10.4). Spanish mirror at `docs/runbooks/deploy.es.md` (per `production-readiness` spec §README.es.md).

#### Files

| Path | Action | Purpose |
|---|---|---|
| `docker-compose.prod.yml` | Create | Prod stack (no Mailpit, `expose:` only, healthchecks, secrets block) |
| `docker-compose.prod.override.example.yml` | Create | Reference for ops to override per-environment |
| `infrastructure/Dockerfile.api.prod` | Create | Multi-stage BE build (non-root, healthcheck) |
| `infrastructure/Dockerfile.frontend.prod` | Create | Multi-stage FE build (nginx runtime) |
| `infrastructure/certbot/Caddyfile` | Create | Caddy auto-TLS (preferred path) |
| `infrastructure/certbot/setup-certs.sh` | Create | One-shot certbot helper for legacy nginx |
| `src/1.Api/JadeCapital.Host/Configuration/DockerSecretConfigurationProvider.cs` | Create | Mounts `/run/secrets/*` as config keys |
| `src/1.Api/JadeCapital.Host/Program.cs` | Modify | Wire `DockerSecretConfigurationProvider` before `Configure<JwtOptions>` (line ~50) |
| `docs/runbooks/deploy.md` | Create | First-time + routine + rollback + incident |
| `docs/runbooks/deploy.es.md` | Create | Spanish mirror |

---

### 2.3 Slice 10.3 — Security headers + TLS (`feature/wave10-security-headers`, PR #39, ~150 LOC)

**Closes gaps**: A3 (CSP + HSTS missing) · A7 (no TLS termination)
**Spec**: `specs/security-headers/spec.md` (5 requirements, 7 scenarios)
**Target LOC**: 150 (only `nginx.conf` + `index.html` + Caddyfile) — **NO `size:exception`**

#### CSP — relaxed mode for v1.0.0-rc1

Per user decision (proposal.md §7.2, decision #4) + `specs/security-headers/spec.md` §Requirement:CSP, ships in **relaxed mode**:

```
default-src 'self';
script-src  'self' 'nonce-{per-request}';
style-src   'self' 'unsafe-inline';
img-src     'self' data: https:;
connect-src 'self' https://api.jadecapital.com wss:;
frame-ancestors 'none';
base-uri    'self';
form-action 'self';
object-src  'none';
```

The nonce is emitted by a tiny nginx `sub_filter` snippet (no Lua module needed) that injects a `<meta http-equiv="Content-Security-Policy" content="script-src 'self' 'nonce-{random}'">` tag on every HTML response. The `{random}` value is a `$request_id` placeholder set by `nginx.conf` (built-in variable, 32 hex chars). The CSP header reuses the same nonce so the browser can correlate.

Strict nonce-only (no `'unsafe-inline'` for styles) is **Wave 11+** — Angular's compiler injects inline styles at build time and would need template-level nonce orchestration. The relaxed posture is dominant in ASP.NET Core + Angular production deployments and blocks 90% of the XSS surface (no injected scripts).

#### HSTS preload

```
Strict-Transport-Security: max-age=63072000; includeSubDomains; preload
```

HSTS preload submission to `hstspreload.org` is a **manual ops decision** (per spec §Out of scope) — not automated. The 2-year max-age + `preload` directive is the qualifying value.

#### Permissions-Policy + COEP/COOP/CORP

```
Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=(), magnetometer=(), gyroscope=(), accelerometer=()
Cross-Origin-Opener-Policy: same-origin
Cross-Origin-Embedder-Policy: require-corp
Cross-Origin-Resource-Policy: same-origin
```

These deny browser features the app never uses. Future feature adds (camera for KYC, payment for Stripe Elements) require removing the relevant directive.

#### TLS via Caddy

`infrastructure/certbot/Caddyfile` (Caddy auto-TLS preferred over certbot because renewal is built-in):

```
jadecapital.com, *.jadecapital.com {
  tls { dns cloudflare {env.CLOUDFLARE_API_TOKEN} }  # DNS-01 for wildcard
  reverse_proxy api:8080
  reverse_proxy /hubs/* api:8080
  @frontend path /assets/* /sitemap.xml /robots.txt
  handle @frontend { root * /srv reverse_proxy frontend:80 }
  handle { reverse_proxy frontend:80 }
}
```

Dev: `caddy trust` workflow with self-signed cert + `NODE_EXTRA_CA_CERTS` for jest. Prod: DNS-01 challenge via Cloudflare token (or Route53 / DigitalOcean — env-driven).

#### Files

| Path | Action | Purpose |
|---|---|---|
| `infrastructure/nginx/nginx.conf` | Modify (existing 25 LOC → ~70 LOC) | Add CSP + HSTS + Permissions-Policy + COEP/COOP/CORP + nonce injection |
| `infrastructure/caddy/Caddyfile` | Create | Auto-TLS via DNS-01 |
| `frontend/src/index.html` | Modify (existing ~12 LOC → ~25 LOC) | Add OG tags (10.6) + `<meta>` CSP fallback when nginx is bypassed (dev `ng serve`) |
| `scripts/verify-security-headers.sh` | Create | `curl -I` against prod URL + assert all 7 headers present + CSP nonce rotates across 2 requests |

---

### 2.4 Slice 10.4 — Backups + migration-order fix (`feature/wave10-backups`, PR #40, ~600 LOC)

**Closes gaps**: A6 (no backup story) · B26 (migration-order fix, Wave 4e.D1) · B16 partial (DR runbook)
**Spec**: `specs/backup-strategy/spec.md` (5 requirements, 7 scenarios)
**Target LOC**: 600 (`size:exception` — migration renumbering is high-risk)

#### Backup scripts

| Script | Cron | RPO | Target | Retention |
|---|---|---|---|---|
| `scripts/backup-postgres.sh` | `0 2 * * *` (daily 02:00 UTC) | 1h via `wal-g` | `s3://jade-backups/postgres/{YYYY-MM-DD}.dump.gpg` | 30d hot + 365d cold |
| `scripts/backup-redis.sh` | `0 * * * *` (hourly) | 5 min (AOF everysec) | `s3://jade-backups/redis/{YYYY-MM-DD-HH}.rdb.gpg` | 7d |
| `scripts/backup-minio.sh` | `0 3 * * *` (daily 03:00 UTC) | 24h | `s3://jade-backups/minio/{date}/` | 7d + 90d cold |
| `scripts/restore-postgres.sh` | on-demand | — | empty Postgres → fully restored | — |

For v1.0 the "S3" target is the existing self-hosted MinIO in a separate DR stack (`mc alias set dr https://dr-minio.example.com ...`). The `mc` + `rclone` commands work identically against real S3 / B2 / DO Spaces — only the endpoint + creds change.

#### Postgres backup — `pg_dump` + `wal-g` for PITR

`pg_dump --format=custom --compress=9` runs daily. `wal-g` ships WAL segments continuously for point-in-time recovery within the last 24h. The `archive_timeout = 3600` in `postgresql.conf` (or `wal_level = replica` + `archive_mode = on` + `archive_command = 'wal-g wal-push %p'`) caps the RPO at 1h.

#### Redis backup — AOF + BGSAVE

The dev compose already has `--appendonly yes` (`docker-compose.yml:26`). The backup script adds hourly `BGSAVE` + `mc cp dump.rdb` to the backup bucket. RPO ≤ 5 min because AOF is `everysec`.

#### MinIO backup — `mc mirror`

`mc mirror --remove` (per spec §Requirement:MinIO backups) prunes deleted files from the backup bucket. Trade attachments in `jade-data` are user-uploaded; loss is unrecoverable without the backup.

#### Wave 4e.D1 — migration-order fix

The 38 SQL files in `infrastructure/postgres/migrations/` use non-consecutive naming: `0009`, `0011`, `0012`, ..., `0018`, `0019`, ..., `0026_NOT_NULL`, `20260806_0001`, ..., `20260814_0008`. The current `migrate.Dockerfile` hard-codes a 30-line `psql -f` chain in the exact apply order — fragile, untested against a fresh DB, and the dependency order isn't documented.

**Renumbering strategy** (per `specs/backup-strategy/spec.md` §Requirement:Wave 4e.D1):

1. Determine dependency order (each migration's referenced tables + columns force an order; the new file lands in `0038_renumber_migrations.sql` with comments listing the order).
2. `git mv` each file to `0001_*.sql` ... `0038_*.sql` based on the dependency order.
3. Rewrite `migrate.Dockerfile` to be order-agnostic:

```dockerfile
# syntax=docker/dockerfile:1.7
FROM postgres:16-alpine
WORKDIR /migrations
COPY migrations/ /migrations/
# Order-agnostic apply: ls | sort handles 0001..0038 regardless of any future
# renumber. The verifier (slice 10.1's scripts/verify-migration-order.sh)
# asserts dependency order via a 'requires:' comment header check.
RUN for f in $(ls /migrations/*.sql | sort); do
      head -1 "$f" | grep -qE "^-- requires: " || echo "WARN: $f has no requires: header";
    done
CMD ["bash", "-c", "until pg_isready -h postgres -U \"$POSTGRES_USER\"; do sleep 2; done && \
     PGPASSWORD=\"$POSTGRES_PASSWORD\" psql -v ON_ERROR_STOP=1 -d \"$POSTGRES_DB\" -U \"$POSTGRES_USER\" -h postgres -f /migrations/init.sql"]
```

`/migrations/init.sql` is a tiny bootstrap that iterates `*.sql` via `\i`. No more 30-line `psql -f` chain in the Dockerfile. Any future renumbering is purely a file rename + init.sql change.

4. `scripts/verify-migration-order.sh` (runs in CI's `test-integration` job) spins up a Testcontainers Postgres + `docker-entrypoint-initdb.d` + asserts `psql -c '\dt'` lists every expected table.

#### Files

| Path | Action | Purpose |
|---|---|---|
| `scripts/backup-postgres.sh` | Create | `pg_dump` + gpg + `mc cp` (idempotent: skips if today's dump exists) |
| `scripts/backup-redis.sh` | Create | `BGSAVE` + `LASTSAVE` wait + `mc cp` |
| `scripts/backup-minio.sh` | Create | `mc mirror --remove` |
| `scripts/restore-postgres.sh` | Create | `mc cp` + gpg decrypt + `pg_restore --clean --if-exists` + WAL replay |
| `scripts/verify-migration-order.sh` | Create | CI verifier: fresh Testcontainers Postgres + apply 38 migrations + assert table count |
| `infrastructure/wal-g/config.yaml` | Create | `wal-g` PITR config (env-var-driven bucket + GPG key) |
| `infrastructure/postgres/migrations/0001_*.sql` ... `0038_*.sql` | Rename (38 files via `git mv`) | Consecutive numbering + `requires:` comment header per file |
| `infrastructure/postgres/migrate.Dockerfile` | Rewrite | Order-agnostic (`ls | sort` + `init.sql`) |
| `infrastructure/postgres/migrations/init.sql` | Create | Iterates `*.sql` via `\i` |
| `docs/runbooks/backup-recovery.md` | Create | Per spec §Requirement:Restore procedure; includes DR drill history section |
| `docs/runbooks/disaster-recovery.md` | Create | RTO ≤ 4h + RPO ≤ 1h + DR site activation procedure |

---

### 2.5 Slice 10.5 — Legal + GDPR + account lifecycle (`feature/wave10-gdpr`, PR #41, ~1,200 LOC)

**Closes gaps**: A8 (ToS + Privacy + Cookie) · A9 (GDPR Art. 17) · G-A1 (GDPR Art. 20) · G-A3 (Cookie consent) · G-A4 (SPF/DKIM/DMARC) · G-A5 (Welcome email) · B18 (Account deletion UI) · B16 partial (GDPR DSAR runbook)
**Spec**: `specs/gdpr-compliance/spec.md` + `specs/account-lifecycle/spec.md` (10 + 5 scenarios)
**Target LOC**: 1,200 — **heaviest slice, `size:exception`**

#### State machine (per `specs/account-lifecycle/spec.md` §Requirement:User lifecycle)

```
                              DELETE /api/users/me
  ┌─────────┐  ─────────────────────────────────────►  ┌──────────────┐
  │ Active  │                                           │ SoftDeleted  │
  │ status=0│                                           │ status=1     │
  └─────────┘                                           └──────┬───────┘
                                                                │ cascade +
                                                                │ schedule
                                                                │ UtcNow+30d
                                                                ▼
                                                       ┌─────────────────────┐
                                                       │ ScheduledHardDelete │
                                                       │ status=2            │
                                                       └──────────┬──────────┘
                                                                  │ 30d elapses
                                                                  │ BackgroundService
                                                                  ▼
                                                       ┌─────────────────────┐
                                                       │    HardDeleted      │
                                                       │  (rows purged +     │
                                                       │  1 pseudonymized    │
                                                       │  audit row)         │
                                                       └─────────────────────┘
```

#### `IUserCascadeDeletor` interface

Lives in `Identity.Application.Abstractions` (per `explore.md:198-217`):

```csharp
namespace JadeCapital.Identity.Application.Abstractions;

/// <summary>
/// Wave 10.5 — per-module cascade deleter. Each module that owns
/// user-scoped aggregates registers one implementation. The
/// IdentityModuleRegistration collects all IEnumerable<IUserCascadeDeletor>
/// and composes a UserCascadeDeleterOrchestrator that invokes each in
/// registration order with try/catch isolation.
/// </summary>
public interface IUserCascadeDeletor
{
    /// <summary>
    /// Soft-delete every row owned by userId across the module's
    /// ISoftDelete-implementing aggregates. Returns the number of
    /// rows affected (0 if no rows owned). Throws ONLY on
    /// unrecoverable infrastructure failures — per-aggregate errors
    /// are logged and swallowed (defense in depth).
    /// </summary>
    Task<CascadeResult> SoftDeleteUserAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Physically purge every row owned by userId (hard delete).
    /// Called by HardDeleteSweepBackgroundService after the 30-day
    /// grace expires. audit.events rows are NOT purged (compliance).
    /// </summary>
    Task HardDeleteUserAsync(Guid userId, CancellationToken ct);
}

public sealed record CascadeResult(int SoftDeletedRows, int SkippedRows, IReadOnlyList<string> Errors);
```

#### Per-module implementations

| Module | Implementation | Owns | Soft-delete fields to set |
|---|---|---|---|
| Identity | `IdentityUserCascadeDeletor` (`Identity.Infrastructure/Cascade/`) | `User`, `RiskProfile`, `RefreshToken`, `PasswordHistory`, `TemporaryCredential`, `IdentityAttachmentQuota` (1 + 5 + n) | `IsDeleted`, `DeletedAtUtc`, `DeletedByUserId` |
| Trading | `TradingUserCascadeDeletor` (`Trading.Application/Cascade/`) | `Trade`, `Account`, `Journal`, `Strategy`, `Alert`, `PlannerTask`, `ScannerFilter`, `PreTradeChecklist`, `TradeReview`, `Attachment`, `ImportJob`, `RiskProfile` (mirror), `AIRiskAdvice`, `CoachingPromptAi`, `QuotationCache` (15+) | same |
| Billing | `BillingUserCascadeDeletor` (`Billing.Application/Cascade/`) | `Subscription`, `StripeCustomer` (2) | same |

Each implementation iterates its known aggregates via the typed `IRepository<T>` (Wave 7 7a.0 Scrutor pattern). It uses the existing `ISoftDelete.MarkDeleted(userId, clock)` — no new audit code; `DecoratedRepository<T>` already emits the `AuditAction.Deleted` row per cascade (per Wave 6 6d.2 / Wave 9 9b.1).

#### Composition root (orchestrator)

```csharp
// src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Cascade/UserCascadeDeleterOrchestrator.cs
public sealed class UserCascadeDeleterOrchestrator : IUserCascadeDeletor
{
    private readonly IReadOnlyList<IUserCascadeDeletor> _deletors;
    private readonly ILogger<UserCascadeDeleterOrchestrator> _logger;
    public UserCascadeDeleterOrchestrator(
        IEnumerable<IUserCascadeDeletor> deletors,
        ILogger<UserCascadeDeleterOrchestrator> logger)
    {
        _deletors = deletors.ToList();
        _logger = logger;
    }
    public async Task<CascadeResult> SoftDeleteUserAsync(Guid userId, CancellationToken ct)
    {
        var total = 0; var skipped = 0; var errors = new List<string>();
        foreach (var d in _deletors)
        {
            try { var r = await d.SoftDeleteUserAsync(userId, ct); total += r.SoftDeletedRows; skipped += r.SkippedRows; errors.AddRange(r.Errors); }
            catch (Exception ex) { _logger.LogError(ex, "Cascade module {Module} failed for user {UserId}", d.GetType().Name, userId); errors.Add($"{d.GetType().Name}: {ex.Message}"); }
        }
        return new CascadeResult(total, skipped, errors);
    }
    public async Task HardDeleteUserAsync(Guid userId, CancellationToken ct)
    {
        // Same pattern; the AuditDbContext is bypassed (audit.events is append-only).
    }
}
```

DI registration (in `IdentityModuleRegistration.cs`):

```csharp
// IUserCascadeDeletor: every module registers an impl; Identity aggregates them.
services.AddScoped<IUserCascadeDeletor, IdentityUserCascadeDeletor>();
// (Trading + Billing register their own in their respective module registrations;
//   MediatR scans the assemblies so the IEnumerable<IUserCascadeDeletor> resolution
//   auto-collects all 3.)
services.AddScoped<UserCascadeDeleterOrchestrator>();
```

`DeleteUserHandler` resolves `UserCascadeDeleterOrchestrator` (NOT the `IUserCascadeDeletor` interface — the orchestrator is the one that calls all 3 modules).

#### Audit log anonymization (per `proposal.md:§3.3` + `explore.md:§G-A2`)

When the `HardDeleteSweepBackgroundService` physically purges a user:

```sql
-- Pseudonymize the audit row (keep the trail, remove PII)
UPDATE audit.events
   SET user_id = NULL,
       changes_json = jsonb_set(
         COALESCE(changes_json, '{}'::jsonb),
         '{hardDelete}',
         jsonb_build_object(
           'original_user_id_hash', encode(digest(original_user_id::text, 'sha256'), 'hex'),
           'purgedAt', to_jsonb(now())
         )
       )
 WHERE entity_id = @userId AND entity_type = 'User' AND action = 2; -- Deleted
```

The `original_user_id_hash` is a SHA-256 of the purged user's GUID (deterministic — same user always hashes to the same value, so the audit trail can be cross-referenced without exposing the GUID). Tamper-evidence (hash chain across rows) is **out of scope** for Wave 10 (decision #12 in `proposal.md`).

#### `DELETE /api/users/me` endpoint

`src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/DeleteAccountEndpoint.cs`:

```csharp
public static class DeleteAccountEndpoint
{
    public static IEndpointRouteBuilder MapDeleteAccountEndpoint(this IEndpointRouteBuilder r)
    {
        r.MapDelete("/api/users/me", async (Guid id, IMediator m, CancellationToken ct) =>
        {
            var result = await m.Send(new DeleteUserCommand(id, UserAgent: "...", Ip: "..."), ct);
            return result.IsSuccess
                ? Results.Accepted(value: new { gracePeriodDays = 30, hardDeleteScheduledAt = result.Value.ScheduledFor })
                : result.Error.Code switch
                {
                    "user.not_found" => Results.NotFound(),
                    _ => Results.Problem(result.Error.Message, statusCode: 500)
                };
        }).RequireAuthorization();
        return r;
    }
}
```

`DeleteUserHandler` (in `Identity.Application/Features/Auth/DeleteAccount/`):

1. Anonymize: `email = "deleted-{userId:N}@anonymized.local"`, `display_name = "Deleted User"`, `IsDeleted = true`, `ScheduledHardDeleteAt = clock.UtcNow + 30 days`, `Status = 2`.
2. Call `IUserCascadeDeleter.SoftDeleteUserAsync(userId, ct)` (the orchestrator).
3. Revoke all refresh tokens: `_refreshTokens.RevokeAllForUserAsync(userId, ct)`.
4. Emit ONE `audit.events` row: `EntityType = "User"`, `Action = AuditAction.Deleted`, `ChangesJson = { anonymizedEmail, scheduledFor, cascadeSoftDeletedRows }`. This is the **only** path through `UserAuditDecorator` that writes a real `Deleted` audit row — the decorator's normal `DeleteAsync` throws `NotSupportedException` (see `UserAuditDecorator.cs:114-123`). The handler bypasses the decorator by calling the inner `IUserRepository.UpdateAsync(user, ct)` (the anonymization is an Update, not a Delete).

#### `GET /api/users/me/export` endpoint

`src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/UserExportEndpoint.cs`:

Streams a JSON object with arrays per entity. Uses `IAsyncEnumerable<T>` per aggregate to avoid buffering the full export in memory (per spec §Scenario:export includes trades). The handler is read-only and bypasses all `ISoftDelete` filters (`IgnoreQueryFilters()`) so the user sees their own soft-deleted data.

```csharp
// Pseudocode (strict TDD → RED test in slice 10.5)
public async Task StreamExportAsync(Guid userId, Stream output, CancellationToken ct)
{
    await using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false });
    writer.WriteStartObject();
    writer.WriteStartArray("trades");
    await foreach (var t in _tradingExport.GetTradesForUserAsync(userId, ct))
        JsonSerializer.Serialize(writer, t, JsonOpts);
    writer.WriteEndArray();
    // ... 12 more arrays
    writer.WriteEndObject();
}
```

`audit.events` and `stripe_webhook_events` are **deliberately excluded** (per `specs/gdpr-compliance/spec.md` §Requirement:GET /api/users/me/export — compliance trail + system tables). Cross-tenant reads return 404 (defense in depth via `ITenantContext.CurrentUserId` check).

#### `HardDeleteSweepBackgroundService`

Shape mirrors `AuditRetentionBackgroundService` exactly (per `explore.md:198` + Wave 9 9b.1 precedent):

```csharp
// src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/BackgroundServices/HardDeleteSweepBackgroundService.cs
public sealed class HardDeleteSweepBackgroundService : BackgroundService
{
    // (constructor mirrors AuditRetentionBackgroundService: IServiceScopeFactory +
    //  IOptionsMonitor<HardDeleteSweepOptions> + ILogger<...>)
    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sweep = scope.ServiceProvider.GetRequiredService<IUserCascadeDeletor>(); // orchestrator
            var options = _options.CurrentValue;
            var cutoff = DateTimeOffset.UtcNow; // now (not "older than" — schedule is absolute)
            var usersToPurge = await _userRepo.ListByStatusAsync(UserStatus.ScheduledHardDelete, cutoff, options.BatchLimit, ct);
            foreach (var u in usersToPurge)
            {
                using var tx = await _uow.BeginTransactionAsync(ct);
                await sweep.HardDeleteUserAsync(u.Id, ct);
                await _audit.LogAsync(new AuditEventEntry("User", u.Id, AuditAction.Deleted, /* pseudonymized */), ct);
                await tx.CommitAsync(ct);
                _logger.LogInformation("HardDeleted user {UserId} after {Days}d grace", u.Id, (clock.UtcNow - u.SoftDeletedAt).Days);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { _logger.LogError(ex, "HardDeleteSweep: failed; will retry next cycle"); }
    }
}
```

`options: { InitialDelaySeconds = 300, IntervalHours = 24, BatchLimit = 100 }` — matches `AuditRetentionOptions` shape (5-min initial delay, daily cycle, max 100 users per cycle). Per-user transaction isolation (one user fails → others proceed). `RunOnceAsync` is `public` for deterministic test driving (Wave 6 6d.2 + Wave 9 9b.1 precedent).

#### Cookie consent + ToS/Privacy FE

- `frontend/src/app/features/public/legal/terms.page.ts` — placeholder structure with `<!-- TODO: legal copy -->` markers + i18n-ready (Spanish default per `index.html:2`). Page MUST render the "LEGAL COPY PLACEHOLDER — DO NOT DEPLOY TO PRODUCTION WITHOUT LEGAL REVIEW" banner until copy lands.
- `frontend/src/app/features/public/legal/privacy.page.ts` — same shape.
- `frontend/src/app/shared/cookie-consent/cookie-consent.service.ts` — Signal-based, persists to `localStorage.jade.consent` + POSTs to `/api/auth/consent`.
- `frontend/src/app/shared/cookie-consent/cookie-consent.banner.ts` — standalone component rendered in `app.ts` root when `localStorage.jade.consent` is absent.
- `frontend/src/app/features/trader/settings/account-deletion/` — new tab in `settings.page.ts` (extends `SettingsTab` enum at line 24). Calls `DELETE /api/users/me` on confirm + shows 30-day grace notice.

#### Welcome email + email deliverability

`RegisterUserHandler` (existing, line 22) gets a new step after `AddAsync`: enqueue a welcome email via the existing `IEmailSender` (`Program.cs:97` wires Mailpit dev / SMTP prod). Idempotency: `users.welcome_email_sent_at` is set after the first send; subsequent registers of the same user (re-issue token) skip.

`docs/runbooks/setup-email-deliverability.md` lists the DNS records the ops team must publish before prod:

| Record | Type | Value |
|---|---|---|
| `@` | TXT | `v=spf1 include:mailgun.org -all` (or `include:amazonses.com`) |
| `selector1._domainkey` | TXT | `<DKIM public key from Mailgun / SES>` |
| `selector2._domainkey` | TXT | `<DKIM public key 2>` |
| `_dmarc` | TXT | `v=DMARC1; p=quarantine; rua=mailto:dmarc@jadecapital.com` |

The Mail__ env vars (`Mail__DkimPrivateKey`, `Mail__DkimSelector`) wire DKIM signing for outbound SMTP. Mailpit dev profile skips DKIM (already configured).

#### Files

| Path | Action | Purpose |
|---|---|---|
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Abstractions/IUserCascadeDeletor.cs` | Create | Interface (no impl) |
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/DeleteAccount/DeleteUserCommand.cs` | Create | Anonymize + schedule command |
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/DeleteAccount/DeleteUserHandler.cs` | Create | Orchestrator caller + audit row + refresh-token revoke |
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/DeleteAccount/GetUserExportHandler.cs` | Create | Streaming JSON export |
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/Consent/ConsentCommand.cs` | Create | Cookie + ToS + Privacy acceptance (Wave 10.5) |
| `src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/DeleteAccountEndpoint.cs` | Create | `DELETE /api/users/me` |
| `src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/UserExportEndpoint.cs` | Create | `GET /api/users/me/export` |
| `src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/ConsentEndpoint.cs` | Create | `POST /api/auth/consent` |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Cascade/UserCascadeDeleterOrchestrator.cs` | Create | Composes IEnumerable<IUserCascadeDeletor> |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Cascade/IdentityUserCascadeDeletor.cs` | Create | Owns User + RiskProfile + RefreshToken + ... |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/BackgroundServices/HardDeleteSweepBackgroundService.cs` | Create | Daily tick, idempotent, per-user tx |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Configuration/HardDeleteSweepOptions.cs` | Create | { InitialDelay, IntervalHours, BatchLimit, GracePeriodDays } |
| `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/DependencyInjection/IdentityModuleRegistration.cs` | Modify | Add `IUserCascadeDeletor` + orchestrator + sweep service + `users` columns migration reference |
| `src/2.Modules/Trading/JadeCapital.Trading.Application/Cascade/TradingUserCascadeDeletor.cs` | Create | Owns 15+ trading aggregates |
| `src/2.Modules/Trading/JadeCapital.Trading.Infrastructure/DependencyInjection/TradingModuleRegistration.cs` | Modify | Register `IUserCascadeDeletor` impl |
| `src/2.Modules/Billing/JadeCapital.Billing.Application/Cascade/BillingUserCascadeDeletor.cs` | Create | Owns Subscription + StripeCustomer |
| `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/DependencyInjection/BillingModuleRegistration.cs` | Modify | Register `IUserCascadeDeletor` impl |
| `src/2.Modules/Identity/JadeCapital.Identity.Domain/Users/User.cs` | Modify | Add `Status` (Active/SoftDeleted/ScheduledHardDelete/HardDeleted), `ScheduledHardDeleteAt`, `CookieConsentAcceptedAt`, `CookieConsentChoice`, `TermsAcceptedAt`, `PrivacyAcceptedAt`, `ConsentIp`, `WelcomeEmailSentAt` (3 lifecycle + 5 consent cols) |
| `infrastructure/postgres/migrations/0039_user_lifecycle.sql` | Create | New migration: ALTER TABLE identity.users ADD COLUMN ... (idempotent) |
| `frontend/src/app/features/public/legal/terms.page.ts` | Create | Placeholder + TODO marker |
| `frontend/src/app/features/public/legal/privacy.page.ts` | Create | Placeholder + TODO marker |
| `frontend/src/app/shared/cookie-consent/cookie-consent.service.ts` | Create | Signal + localStorage + POST |
| `frontend/src/app/shared/cookie-consent/cookie-consent.banner.ts` | Create | Standalone banner component |
| `frontend/src/app/features/trader/settings/account-deletion/account-deletion-tab.ts` | Create | New tab in settings.page.ts |
| `frontend/src/app/features/trader/settings/settings.page.ts` | Modify | Add 'account-deletion' to SettingsTab enum (line 24) + tab pill |
| `docs/runbooks/gdpr-data-subject-request.md` | Create | Manual DSAR intake + 30d restoration procedure (if user requests within grace) |
| `docs/runbooks/setup-email-deliverability.md` | Create | SPF/DKIM/DMARC DNS records + DKIM rotation cadence |

---

### 2.6 Slice 10.6 — Docs + observability + SEO + coverage + Stripe-verify (`feature/wave10-docs-observability`, PR #42, ~800 LOC)

**Closes gaps**: B5 (coverage report) · B9 (FE error reporting — Sentry FE) · B11 (SEO) · B13 (LICENSE + CHANGELOG + CONTRIBUTING + SECURITY.md) · B14 (README.es.md) · B15 (ADRs 0005-0009) · B17 + A4 (Stripe prod key validation)
**Specs**: `specs/observability-light/spec.md` + `specs/production-readiness/spec.md` (8 + 9 scenarios)
**Target LOC**: 800 (`size:exception`)

#### LICENSE (Proprietary / Todos los derechos reservados)

`LICENSE` (English) + `LICENSE.es.md` (Spanish mirror) per user decision. `CONTRIBUTING.md` explicitly states: "This repository is proprietary software. External contributions are not accepted. See LICENSE."

#### ADRs 0005-0009

| File | Decision | Source wave |
|---|---|---|
| `docs/adr/0005-multi-tenant-architecture.md` | `tenant_id` JWT claim + `TenantContextMiddleware` + EF query filter (Wave 6 6c.2) | Wave 6 |
| `docs/adr/0006-audit-decorator-pattern.md` | `DecoratedRepository<T>` + Scrutor `services.Decorate<>` + typed decorators (Wave 6 6d.2 / Wave 7 7a.1) | Wave 6/7 |
| `docs/adr/0007-stripe-gateway-abstraction.md` | `IStripeGateway` + `StubStripeGateway` fallback (Wave 6 6a.1) | Wave 6 |
| `docs/adr/0008-audit-retention-policy.md` | 90-day retention `AuditRetentionBackgroundService` (Wave 9 9b.1) | Wave 9 |
| `docs/adr/0009-gdpr-right-to-be-forgotten.md` | `IUserCascadeDeletor` + 30d grace + `HardDeleteSweepBackgroundService` (Wave 10 10.5) | Wave 10 |

Each follows Context → Decision → Consequences → Alternatives. Minimum 3 consequences per ADR (per spec §Scenario:5 ADRs).

#### Sentry integration (BE + FE)

```csharp
// src/1.Api/JadeCapital.Host/Program.cs (add after Serilog config, ~line 46)
if (!string.IsNullOrWhiteSpace(builder.Configuration["Sentry:Dsn"]))
{
    builder.WebHost.UseSentry(o =>
    {
        o.Dsn = builder.Configuration["Sentry:Dsn"];
        o.Environment = builder.Environment.EnvironmentName;
        o.Release = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        o.TracesSampleRate = 0.1; // 10% of requests traced
        o.AttachStacktrace = true;
    });
}
```

When `Sentry__Dsn` is unset (dev / sandbox) → no Sentry init → `SentrySdk.IsEnabled == false` → no warning logged (silent skip per spec §Scenario:Sentry DSN unset).

FE: `@sentry/angular@8.x` initialized in `main.ts` only when `window.__SENTRY_DSN__` is set (injected from nginx env via `sub_filter` in slice 10.3's nginx config).

#### Coverlet + 70% line coverage gate

- Add `<PackageReference Include="coverlet.collector" Version="6.0.4" />` to all 5 `*.UnitTests.csproj` + 1 `*.IntegrationTests.csproj`.
- CI step: `dotnet test --collect:"XPlat Code Coverage" --results-directory ./artifacts/coverage`.
- Gate step (per `specs/production-readiness/spec.md` §Requirement:Coverage gate):

```bash
# scripts/check-coverage-threshold.sh
LINE_PCT=$(grep -oP '"lineCoverage":\K[0-9.]+' artifacts/coverage/coverage.cobertura.xml | head -1)
if (( $(echo "$LINE_PCT < 70" | bc -l) )); then
  echo "::error::Coverage $LINE_PCT% < 70% threshold"
  exit 1
fi
```

Baseline measurement: if current < 70%, raise the gate in 5% increments over the Wave 10 lifecycle (tracked in CHANGELOG).

#### OpenAPI export in CI

Per `specs/observability-light/spec.md` §Requirement:OpenAPI export, the `test-integration` job adds:

```yaml
- run: dotnet tool install -g Swashbuckle.AspNetCore.Cli
- run: dotnet swagger tofile --output artifacts/openapi.json src/1.Api/JadeCapital.Host/bin/Release/net10.0/JadeCapital.Host.dll v1
- uses: actions/upload-artifact@v4
  with: { name: openapi-spec, path: artifacts/openapi.json, retention-days: 30 }
```

Artifact-only (NOT committed to repo) to avoid drift from runtime Swagger.

#### SEO basics

| Asset | Location | Purpose |
|---|---|---|
| `frontend/src/sitemap.xml` | Generated at build time by `sitemap-generator.ts` | Lists public routes (`/`, `/pricing`, `/faq`, `/login`, `/register`) + `lastmod` |
| `frontend/src/robots.txt` | Static | `User-agent: * / Disallow: /api/ / Sitemap: https://jadecapital.com/sitemap.xml` |
| `frontend/src/index.html` | Modify | Add `og:title`, `og:description`, `og:image`, `og:url`, `twitter:card` (per spec) |
| `src/1.Api/JadeCapital.Host/Endpoints/SitemapEndpoint.cs` | Create | `GET /sitemap.xml` returns the XML |

`og:image` is a static `frontend/src/assets/og-card.png` (1200×630 placeholder, TODO marker for designer).

#### Stripe production key validation

`src/1.Api/JadeCapital.Host/Configuration/StripeOptionsValidator.cs` (per `specs/production-readiness/spec.md` §Requirement:Stripe validation):

```csharp
public sealed class StripeOptionsValidator : IValidateOptions<StripeOptions>
{
    private readonly IHostEnvironment _env;
    public StripeOptionsValidator(IHostEnvironment env) => _env = env;
    public ValidateOptionsResult Validate(string? name, StripeOptions options)
    {
        if (_env.IsDevelopment() || _env.IsEnvironment("Sandbox"))
            return ValidateOptionsResult.Success; // dev/sandbox skips
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add("StripeOptions.ApiKey is required in Production/Staging.");
        else if (options.ApiKey.StartsWith("sk_test_local_dev_placeholder", StringComparison.Ordinal))
            failures.Add("StripeOptions.ApiKey contains the dev placeholder.");
        else if (options.ApiKey.Length < 32)
            failures.Add("StripeOptions.ApiKey length < 32 chars (suspicious).");
        if (string.IsNullOrWhiteSpace(options.WebhookSecret))
            failures.Add("StripeOptions.WebhookSecret is required in Production/Staging.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
```

Registration in `BillingModuleRegistration.cs` (the existing `Configure<StripeOptions>(...)` at line ~73):

```csharp
services.AddSingleton<IValidateOptions<StripeOptions>, StripeOptionsValidator>();
services.AddOptions<StripeOptions>().ValidateOnStart();
```

`scripts/stripe-test-smoke.sh` exercises the full lifecycle: create customer → checkout → webhook → verify subscription → cancel via portal → verify cancellation webhook. Uses `sk_test_*` keys; exits non-zero on any failure.

#### Files

| Path | Action | Purpose |
|---|---|---|
| `LICENSE` | Create | Proprietary English |
| `LICENSE.es.md` | Create | Spanish mirror |
| `CHANGELOG.md` | Create | Keep-a-Changelog 1.1.0; backfilled Waves 0-9 + new Wave 10 entries |
| `CONTRIBUTING.md` | Create | Dev setup + commit convention + proprietary notice |
| `SECURITY.md` | Create | `security@jadecapital.com` + 72h response timeline |
| `README.es.md` | Create | Spanish mirror of `README.md` |
| `docs/adr/0005-multi-tenant-architecture.md` | Create | ADR |
| `docs/adr/0006-audit-decorator-pattern.md` | Create | ADR |
| `docs/adr/0007-stripe-gateway-abstraction.md` | Create | ADR |
| `docs/adr/0008-audit-retention-policy.md` | Create | ADR |
| `docs/adr/0009-gdpr-right-to-be-forgotten.md` | Create | ADR |
| `src/1.Api/JadeCapital.Host/Configuration/StripeOptionsValidator.cs` | Create | `IValidateOptions<StripeOptions>` |
| `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/DependencyInjection/BillingModuleRegistration.cs` | Modify | Wire validator + `ValidateOnStart` (~line 73) |
| `src/1.Api/JadeCapital.Host/JadeCapital.Host.csproj` | Modify | Add `Sentry.AspNetCore` 4.x |
| `src/1.Api/JadeCapital.Host/Program.cs` | Modify | Wire Sentry (~line 46) |
| `frontend/package.json` | Modify | Add `@sentry/angular@^8.0.0` |
| `frontend/src/main.ts` | Modify | Init Sentry when `window.__SENTRY_DSN__` set |
| `Directory.Packages.props` | Modify | Add `coverlet.collector 6.0.4` |
| `tests/**/*.csproj` | Modify (6 files) | Add `coverlet.collector` reference |
| `frontend/src/index.html` | Modify | OG tags + sitemap link |
| `frontend/src/sitemap.xml` | Create | Static + build-time generation script |
| `frontend/src/robots.txt` | Create | Static |
| `frontend/src/assets/og-card.png` | Create | 1200×630 placeholder (TODO marker) |
| `frontend/src/app/shared/seo/seo.service.ts` | Create | Sets `document.title` + meta tags per route |
| `scripts/stripe-test-smoke.sh` | Create | Stripe test-mode lifecycle smoke test |
| `scripts/check-coverage-threshold.sh` | Create | 70% line coverage gate |
| `scripts/check-docs-sync.sh` | Create | Verifies README.md ↔ README.es.md headings match |

---

## 3. Cross-cutting patterns

### 3.1 Module dependency diagram (after Wave 10)

```
                          ┌──────────────────────────────────┐
                          │  src/1.Api/JadeCapital.Host       │
                          │  (Program.cs: middleware + DI)    │
                          └────┬─────────────┬───────────┬───┘
                               │             │           │
       ┌───────────────────────┼─────────────┼───────────┼──────────────────┐
       │                       │             │           │                  │
       ▼                       ▼             ▼           ▼                  ▼
┌──────────────┐  ┌──────────────────┐  ┌────────────┐  ┌────────────┐  ┌─────────────┐
│  Identity    │  │     Trading      │  │   Billing  │  │   Admin    │  │ PublicPortal│
│  .Application│  │  .Application    │  │ .Application│  │ .Application│ │  (scaffold) │
│  .Api        │  │  .Api            │  │ .Infrastructure│ │ .Infrastructure│             │
│  .Infrastructure│  .Infrastructure  │  │  .PublicApi │  │  .Api        │  │             │
└──────┬───────┘  └────────┬─────────┘  └─────┬──────┘  └──────┬─────┘  └─────────────┘
       │                    │                  │                │
       │     IUserCascadeDeletor (interface in Identity.Application.Abstractions)  │
       │     ──────────────────── implements ──────────────────── implements ─────►│
       │                    │                  │
       │    ┌───────────────┴──────────────────┘
       │    │  UserCascadeDeleterOrchestrator (Identity.Infrastructure)
       │    │  collects all 3 IUserCascadeDeletor impls via DI
       │    └────────────────┬────────────────────┐
       │                     │                    │
       ▼                     ▼                    ▼
   (own User)         (own 15+ aggregates)  (own Subscription + StripeCustomer)
       │                     │                    │
       └─────────────────────┼────────────────────┘
                             ▼
                  HardDeleteSweepBackgroundService
                  (Identity.Infrastructure, daily tick)
                             │
                             ▼
                  ┌─────────────────────────┐
                  │  Shared.Kernel          │
                  │  - ISoftDelete          │
                  │  - IAuditLogger         │
                  │  - ITenantContext       │
                  │  - IClock               │
                  └─────────────────────────┘
```

**No new project-to-project references in the .NET solution.** The 3 modules' cascade deletors all depend on `IUserCascadeDeletor` (interface in Identity.Application.Abstractions). The orchestrator lives in Identity.Infrastructure. This is the **same composition pattern** Wave 9 uses for `ISoftDeleteProviderRegistry` (see `IdentityModuleRegistration.cs:144-148`).

### 3.2 GDPR cascade pattern (decision record)

The 17 user-owned aggregates across 3 modules need a typed cascade. The 3 candidate patterns and their trade-offs:

| Option | Tradeoff | Decision |
|---|---|---|
| **A. Reflection-based** (discover all `ISoftDelete` aggregates via EF metadata) | Tight coupling to EF; hard to test in unit; misses aggregates registered in different DbContexts; bypasses tenant isolation | ❌ Rejected |
| **B. Event-bus (MediatR notification + per-module handler)** | Loose coupling; but requires every module to register a handler; failure in one module can leave the user partially deleted; audit row granularity is coarse | ❌ Rejected (loose) |
| **C. `IUserCascadeDeletor` interface + per-module impl + orchestrator composition** | Explicit module boundary; per-module unit test; orchestrator catches per-module exceptions; audit row per cascade is visible via existing `DecoratedRepository<T>` | ✅ **Chosen** |

**Rationale**: Pattern C matches Wave 9's `ISoftDeleteProviderRegistry` precedent (`IdentityModuleRegistration.cs:144-148`). Each module owns its aggregates and registers one cascade deletor. The orchestrator collects via DI (`IEnumerable<IUserCascadeDeletor>` auto-aggregation) and invokes each with try/catch isolation. Per-cascade audit rows already exist via the existing `DecoratedRepository<T>` Scrutor pattern (Wave 6 6d.2) — no new audit infrastructure.

### 3.3 Audit log anonymization (GDPR hard-delete)

When user U1 is hard-deleted after the 30-day grace:

```sql
-- Pseudonymize the audit row: keep the trail, remove the PII link
UPDATE audit.events
   SET user_id = NULL,
       changes_json = jsonb_set(
         COALESCE(changes_json, '{}'::jsonb),
         '{gdprAnonymizedAt}',
         to_jsonb(now())
       ),
       entity_id = 'deleted_user_' || encode(digest(entity_id::text, 'sha256'), 'hex')
 WHERE entity_id = @userId
   AND entity_type = 'User'
   AND user_id = @userId;

-- Physically purge the user row + dependents
DELETE FROM identity.users WHERE id = @userId;
DELETE FROM identity.refresh_tokens WHERE user_id = @userId;
-- (trading.* + billing.* purged by TradingUserCascadeDeletor + BillingUserCascadeDeletor)
```

The `entity_id` rewrite uses a deterministic SHA-256 hash of the original GUID — same user always hashes to the same value, so the audit trail can still be cross-referenced (e.g., "show me all audit events for the user that was deleted on 2026-10-15") without exposing the original ID. `audit.events` is never purged (compliance trail per Wave 9 spec).

This is **NOT** a tamper-evident hash chain (cross-row signing). Per proposal.md §7.2 decision #12, that is Wave 11+ (gap G-A2).

### 3.4 Secret rotation pattern

`DockerSecretConfigurationProvider` (slice 10.2) reads `/run/secrets/<name>` files and projects them as `ASPNETCORE_` keys:

| Env-var key | Reads from |
|---|---|
| `DOCKER_SECRET_JWT_ACCESS_TOKEN_SECRET` | `/run/secrets/jwt_access_token_secret` |
| `DOCKER_SECRET_POSTGRES_PASSWORD` | `/run/secrets/postgres_password` |
| ... (7 secrets total per spec) | ... |

Existing `Configure<JwtOptions>(...)` (Program.cs:50) + `ValidateOnStart` already works because the provider runs before options resolution. The rotation procedure (per `infrastructure/production/README.md`):

1. `docker secret create jwt_access_token_secret_v2 ./new-secret.txt`
2. `docker service update --secret-rm jwt_access_token_secret --secret-add jwt_access_token_secret_v2 jade_api`
3. Zero-downtime via blue/green (rolling restart picks up the new secret on the next config reload).

Vendor migration (Vault / Doppler / AWS SM) is **config-only** — the same env keys get populated by a different sidecar process. No code change required.

### 3.5 Backup retention policy (slice 10.4 detail)

| Asset | Script | Cron | Retention | Off-site target |
|---|---|---|---|---|
| Postgres | `backup-postgres.sh` | `0 2 * * *` daily | 30d hot + 365d cold | `mc cp` to MinIO `backups/postgres/` |
| Postgres WAL | `wal-g wal-push` | continuous (cron-managed) | 24h rolling | `mc cp` to MinIO `backups/postgres/wal/` |
| Redis | `backup-redis.sh` | `0 * * * *` hourly | 7d | `mc cp` to MinIO `backups/redis/` |
| MinIO (attachments) | `backup-minio.sh` | `0 3 * * *` daily | 7d local + 90d cold | `mc mirror` to MinIO `backups/minio/` |

RPO ≤ 1h (Postgres via `wal-g` hourly WAL archive). RTO ≤ 4h (full restore + WAL replay + migration re-apply documented in `disaster-recovery.md`). DR drill runs annually (calendar anniversary tracked in `backup-recovery.md §Drill history`).

---

## 4. Risks + mitigations

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| **R1** | **GDPR cascade blast radius** — 17 aggregates × 3 modules; missed registration leaks user data post-DELETE | Med | Critical | (a) Per-module integration test: register U1 → open trade → journal → strategy → DELETE → assert `IsDeleted=true` (with `IgnoreQueryFilters()`) on every aggregate; (b) per-cascade audit row catches missed `IUserCascadeDeletor` impls in `audit.events`; (c) 30-day grace + manual DSAR runbook (R5) |
| **R2** | **Migration-order renumbering breaks deploy** — 38 SQL files renamed in one slice | Med | High | (a) `git mv` with verifier-script dry-run; (b) `scripts/verify-migration-order.sh` runs Testcontainers fresh-DB in CI; (c) `migrate.Dockerfile` rewritten to be order-agnostic (`ls | sort` + `init.sql`); (d) `git revert` per slice is safe (Wave 9 PR #36 precedent) |
| **R3** | **Stripe prod key validator blocks local dev** — strict validation fails `Development` env | Low | Medium | Validator gates on `env.IsProduction() \|\| env.IsStaging()`. Dev `.env` keeps placeholder. `appsettings.Development.json` has `StripeOptions.SkipValidation = true` |
| **R4** | **LICENSE + ToS / Privacy placeholders shipped to prod** — legal copy TODO markers | Med | Compliance | (a) `CONTRIBUTING.md` + `CHANGELOG.md` carry "LEGAL COPY PLACEHOLDER — DO NOT DEPLOY TO PRODUCTION WITHOUT LEGAL REVIEW"; (b) 10.6 `production-readiness` spec §Definition of Done flags this; (c) `terms.page.ts` + `privacy.page.ts` render a visible banner until copy lands; (d) v1.0.0-rc1 tag is the **op** team's go/no-go |
| **R5** | **CI runner quota exhaustion at launch ramp** — 5 PRs/week × ~10 min + nightly × ~30 min = ~1400 min | Low | Medium | NuGet + npm cache hit >80% (keys on `Directory.Build.props` + `package-lock.json`); nightly cron not on every push; self-hosted runner migration is Wave 11+ |
| **R6** | **CSP nonce doesn't reach Angular's lazy-loaded chunks** — relaxed CSP `'unsafe-inline'` for styles, per-request nonce for scripts | Low | Low | Decision: ship relaxed CSP (proposal §7.2 #4). Verified by `scripts/verify-security-headers.sh` against an actual `ng build` + nginx |
| **R7** | **`size:exception` precedent erosion** — 6 consecutive waves (5/6/7/8/9/10) | Med | Process | `branch-pr` skill consulted at PR #37 + #41. 10.3 (~150 LOC) is the counter-example that keeps the discipline visible |
| **R8** | **GDPR user requests restoration within 30d grace** — re-register + manual ops restore | Low | Low | `docs/runbooks/gdpr-data-subject-request.md` documents the manual flow (re-issue password + `UPDATE users SET is_deleted=false WHERE id=...`). NOT automated (Wave 11+) |
| **R9** | **Welcome email + DKIM not configured at first deploy** — emails land in spam | Med | Medium | `docs/runbooks/setup-email-deliverability.md` is in `docs/runbooks/`; idempotent 7-day suppression; email failure is logged + does NOT block registration |
| **R10** | **Hard-delete BackgroundService clock-skew bugs** — purges a user before 30d elapses | Low | Critical | Guard is `status = 2 AND scheduled_for_hard_delete_at <= UtcNow` (NOT `is_deleted AND created_at < cutoff`). Wave 6 6d.2 `FakeClock` precedent in tests |
| **R11** | **Data export endpoint buffers 17 aggregates in memory** | Med | Medium | `IAsyncEnumerable<T>` per aggregate + `Utf8JsonWriter` to output stream (per spec §Scenario:export streams). Memory ceiling = largest single aggregate row, NOT sum |
| **R12** | **Sentry init in FE after Angular bootstrap** — misses bootstrap errors | Low | Low | Sentry init in `main.ts` BEFORE `bootstrapApplication(...)` call (per `observability-light` spec §Out of scope note). Source maps resolved via `release` tag + `.pdb` files in prod Docker image |

---

## 5. Open architectural questions

These questions are non-blocking for `sdd-tasks` but should be resolved before `sdd-apply`:

1. **Legal copy owner** (slice 10.5): who supplies the ToS / Privacy Policy text? User confirmed placeholders + TODO markers ship. Wave 10 merges with placeholders; user/legal must replace before v1.0.0 deploy. If the user wants Wave 10 to block on legal copy, schedule slip is 1-2 weeks.

2. **CSP strict mode timeline** (slice 10.3): relaxed CSP is locked for v1.0.0-rc1. Wave 11+ work to drop `'unsafe-inline'` for styles requires Angular template-level nonce orchestration. Approximate effort: 1 dedicated slice (~600 LOC). Confirm Wave 11 owner.

3. **Backup off-site target** (slice 10.4): self-hosted MinIO in a separate DR stack is locked. AWS S3 / Backblaze B2 is the v1.1+ upgrade path. Confirm DR stack is reachable from prod (network ACLs, VPN).

4. **`HardDeleteSweepOptions.GracePeriodDays` configurability** (slice 10.5): locked at 30 days for v1.0 (matches Stripe billing cycle refund window). Should the user be able to override via `HardDeleteSweep:GracePeriodDays` env var? Current plan: NO (compliance invariant) — but flag for review.

5. **`users.welcome_email_sent_at` placement** (slice 10.5): the column lives on `identity.users` (adds a column to the user table that may not be GDPR-relevant after hard-delete). Alternative: track welcome email state in `Identity.Infrastructure/Email/WelcomeEmailLog` table. Current plan: keep on `users` table; the column is purged with the user on hard-delete. Confirm.

6. **Migration `0039_user_lifecycle.sql` idempotency** (slice 10.5): new `users` columns are all nullable + `ADD COLUMN IF NOT EXISTS`. The cascade deletor orchestration depends on these columns. If a partial-migration state is encountered (some columns added, others not), the cascade handler must be defensive. Plan: enum-typed Status column with DEFAULT 0 + CHECK constraint widened (mirrors Wave 6 0029 `audit_events_action_check_widen.sql`).

7. **Sentry sampling rate** (slice 10.6): `TracesSampleRate = 0.1` is the default. Prod may want 0.05 (cost) or 0.5 (debug). Make configurable via `Sentry:TracesSampleRate` env. Confirm the default.

---

## Appendix A — Slice summary table

| PR # | Slice | Branch | LOC | `size:exception` | Tests added | Specs (NEW) | Critical gaps closed |
|---|---|---|---|---|---|---|---|
| #37 | 10.1 | `feature/wave10-ci-cd` | 800 | yes | ~5 | `ci-infrastructure` | A1, A10, B6 partial |
| #38 | 10.2 | `feature/wave10-deployment` | 800 | yes | ~4 | `deployment-automation` | A2, A5 |
| #39 | 10.3 | `feature/wave10-security-headers` | 150 | **no** | ~3 | `security-headers` | A3, A7 |
| #40 | 10.4 | `feature/wave10-backups` | 600 | yes | ~5 | `backup-strategy` | A6, B26, B16 partial |
| #41 | 10.5 | `feature/wave10-gdpr` | 1,200 | **yes (heaviest)** | ~10 | `gdpr-compliance` + `account-lifecycle` | A8, A9, G-A1, G-A3, G-A4, G-A5, B18, B16 partial |
| #42 | 10.6 | `feature/wave10-docs-observability` | 800 | yes | ~6 | `observability-light` + `production-readiness` | B5, B9, B11, B13, B14, B15, B17, A4 |
| **Total** | 6 chained PRs | | **~4,350** | 5 of 6 | **~33** | 8 NEW | 20 gaps |

## Appendix B — References

- `openspec/changes/2026-08-18-wave10-v1-readiness/explore.md` (487 LOC) — gap audit
- `openspec/changes/2026-08-18-wave10-v1-readiness/proposal.md` (339 LOC) — decisions
- `openspec/changes/2026-08-18-wave10-v1-readiness/specs/{8 specs}` — 8 NEW capabilities
- `openspec/specs/multi-tenant/spec.md` — `ITenantContext` for 10.5 export endpoint
- `openspec/specs/stripe/spec.md` — `IStripeGateway` for 10.6 validator context
- `openspec/specs/soft-delete-audit/spec.md` — `ISoftDelete` for 10.5 cascade
- `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/BackgroundServices/AuditRetentionBackgroundService.cs` — Wave 9 9b.1 precedent for 10.5's `HardDeleteSweepBackgroundService`
- `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/UserAuditDecorator.cs:114-123` — the `NotSupportedException` 10.5's DELETE flow must bypass
- `src/1.Api/JadeCapital.Host/Program.cs:50-53` — `JwtOptions` `ValidateOnStart` precedent for 10.6's `StripeOptions` validator
- `src/1.Api/JadeCapital.Host/PiiLogScrubber.cs` — existing PII filter (extended in 10.6's `observability-light` spec)
- `src/3.Shared/JadeCapital.Shared.Kernel/Audit/AuditAction.cs:27-63` — `AuditAction.Deleted` reused for 10.5's GDPR cascade
- `infrastructure/postgres/migrations/` (38 files) — renumbered in 10.4
- `infrastructure/postgres/migrate.Dockerfile` — rewritten in 10.4 to be order-agnostic
- `infrastructure/nginx/nginx.conf:17-19` — current 3 headers; 10.3 extends to 7
- `docker-compose.yml` — dev compose; 10.2 ships `docker-compose.prod.yml` alongside
- `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Stripe/StripeOptions.cs` — POCO extended with validator in 10.6
- `docs/adr/` (4 existing) — 10.6 adds 5 (0005-0009)
- `docs/PROJECT-STATUS.md:110,142` — Wave 4e.D1 + broker-integration deferral context
- `docs/runbooks/local-dev.md` (only existing) — 10.6 adds 4 (deploy, backup-recovery, disaster-recovery, gdpr-data-subject-request)
- Wave 9 verify-report: 3 CRITICAL bugs remediated (PR #36) — informs Wave 10's per-cascade audit row strategy
