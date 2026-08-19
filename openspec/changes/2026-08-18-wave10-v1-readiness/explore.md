# Explore — Wave 10 (v1 readiness)

**Change**: `2026-08-18-wave10-v1-readiness`
**Branch**: `feature/0a-identity-model` @ `49207e2` (Wave 9 just archived — `chore(sdd): archive 2026-08-19-wave9-audit-finalization`)
**Stack**: ASP.NET Core 10 / .NET SDK 10.0.400 (via `mise exec -- dotnet …`)
**Mode**: hybrid (OpenSpec + engram); explore is read-only (Strict TDD does not apply to research)
**Tests at baseline**: 1389 BE + 166 FE (post-Wave 9; 230 test files across 5 unit test assemblies + 1 integration assembly)

---

## Intent

Ship what is missing to call Jade Capital Suite **v1.0 release-ready**. Wave 10 closes every gap classified as **Critical (A)** in the prior v1-readiness audit, plus the low-effort high-value **Important (B)** items that block a credible v1.0 launch (legal docs, GDPR right-to-be-forgotten, CI/CD, prod deployment story, security headers, TLS, runbooks, basic observability hooks, test coverage). Defers deep observability, multi-region, accessibility WCAG-AAA, PWA, and broker integrations to v1.1+ (Wave 11+).

Wave 10 is **operational + compliance hardening**, not new features. Every slice produces code (or config) that is verifiable against the spec — no slice is purely documentary.

---

## Context

- **Source state**: all 11 waves (0–9) are archived in `openspec/changes/archive/`. The active changes dir is empty (as of `49207e2`).
- **Tests**: 1389 BE + 166 FE passing on `feature/0a-identity-model`. Wave 9 closed 5 audit-decorator + query-API + retention slices.
- **Deferred list** (`docs/PROJECT-STATUS.md` §4): 12 items currently deferred — Wave 10 subsumes 5 of them (real broker → v1.1, virus scanner → v1.1, cloud AI → v1.1, SSE → v1.1, multi-tenant AI rate limit → v1.1).
- **Architectural surface**: monolith `1.Api/JadeCapital.Host` + 5 modules (`Identity`, `Trading`, `Billing`, `Admin`, `PublicPortal` [scaffold]) + 2 Shared layers (`Kernel`, `Infrastructure`). All v1.0 product features are shipped.
- **OpenSpec convention**: Wave 10 follows the Wave 9 `feature-branch-chain` strategy with `size:exception` per slice (Wave 5/6/7/8/9 precedent).
- **Strict TDD**: `openspec/config.yaml:45` declares `strict_tdd: true`. Every slice is TDD-driven (RED → GREEN → REFACTOR).

---

## v1 gaps audit validation

Legend: ✅ confirmed (gap exists, audit was right) · ❌ false positive (audit was wrong) · 🔄 partial (gap is partially filled but incomplete)

### A. Critical (block v1 release)

| ID | Audit claim | Verdict | Evidence |
|---|---|---|---|
| **A1** | No CI/CD pipeline (`.github/` missing) | ✅ confirmed | `ls -la .github` → no such directory; `find . -name "*.yml" -not -path "*/node_modules/*"` → only smoke scripts in `scripts/`. No `.github/workflows/`, `.gitlab-ci.yml`, `azure-pipelines.yml`, or `Jenkinsfile`. |
| **A2** | Production secrets management (only `.env`) | ✅ confirmed | `ls /home/nitro/Proyects/JadeCapitalSuiteOficial/.env*` → only `.env` + `.env.example`. No `vault/`, `docker-compose.vault.yml`, `secrets/` directory, no Vault/Doppler/AWS Secrets Manager references. |
| **A3** | CSP + HSTS headers missing from `nginx.conf` | ✅ confirmed | `infrastructure/nginx/nginx.conf:17-19` shows only `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`. No `Content-Security-Policy`, no `Strict-Transport-Security`. Same in `frontend/nginx.conf` (no security headers at all). |
| **A4** | Stripe production keys not verified (StubStripeGateway fallback) | ✅ confirmed | `.env:32` has `STRIPE_SECRET_KEY=sk_test_local_dev_placeholder`. `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/DependencyInjection/BillingModuleRegistration.cs:73-78` swaps to `StubStripeGateway` when `StripeOptions.ApiKey` is null/empty. The placeholder trips the same fallback — production deploy would silently use the stub. |
| **A5** | No production deployment story | ✅ confirmed | `ls Dockerfile.prod docker-compose.prod.yml docker-compose.production.yml helm/ k8s/ infrastructure/prod` → all absent. Only `backend/Dockerfile` + `frontend/Dockerfile` + `docker-compose.yml` (dev). |
| **A6** | DB backup + migration strategy not documented | ✅ confirmed | `docs/runbooks/local-dev.md:46-50` is the ONLY backup reference — a one-liner `pg_dump` invocation. No cron, no off-site, no Redis/MinIO backups, no point-in-time recovery, no `wal-g` or `pgbackrest`. Migration-order fix (Wave 4e.D1) is still pending per `PROJECT-STATUS.md:110`. |
| **A7** | SSL/TLS termination not configured | ✅ confirmed | `infrastructure/nginx/conf.d/jade.conf:8` → `listen 80;` (HTTP). `frontend/nginx.conf:2` → `listen 80;` (HTTP). `docker-compose.yml:129,154` → `ports: ["18080:8080"]` and `["4200:80"]` expose only HTTP. No certbot/Caddy/Traefik/LetsEncrypt integration. |
| **A8** | ToS + Privacy Policy + Cookie consent don't exist | ✅ confirmed | `grep -r "terms\|privacy\|cookie"` in `frontend/src/` → 0 matches in code. `landing-page.ts` mentions "encryption AES-256" (line 703) but no legal pages. `openspec/specs/` has no `legal/`, `terms/`, or `privacy/` spec folder. |
| **A9** | GDPR Art. 17 (right-to-be-forgotten) not implemented | ✅ confirmed | `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Audit/UserAuditDecorator.cs:114-123` explicitly throws `NotSupportedException("User deletion happens via Tenant reassignment, not direct delete.")`. No `DELETE /api/users/me`, no cascade-delete flow, no `ForgetUserCommand`. Spec explicitly defers to Wave 10+ (`openspec/specs/soft-delete-audit/spec.md:1114`). |
| **A10** | Dependency vulnerability scanning doesn't exist | ✅ confirmed | `find . -name ".github"` → absent; no Dependabot config; no `npm audit` CI step; no `dotnet list package --vulnerable` CI step; no Snyk/Renovate. `backend/Dockerfile:6` uses `mcr.microsoft.com/dotnet/sdk:10.0` with `RUN dotnet restore` — no `--locked-mode` or vulnerability gate. |

### B. Important (high quality, not blockers)

| ID | Audit claim | Verdict | Evidence |
|---|---|---|---|
| **B1** | Observability: no OTel, Prometheus, Datadog, Sentry | ✅ confirmed | `JadeCapital.Host.csproj:13-21` — no `OpenTelemetry.*` packages. Serilog only (line 16). No Prometheus exporter. No Sentry SDK. |
| **B2** | Uptime monitoring | ✅ confirmed | No UptimeRobot/Better Stack/Pingdom config in any runbook. `/health/live` + `/health/ready` exist (Program.cs:401-409) but are not wired to external monitoring. |
| **B3** | WCAG a11y audit + axe-core | ✅ confirmed | `frontend/package.json:10-32` — no `@axe-core/*` or `jest-axe`. No a11y tests. |
| **B4** | E2E tests in CI (Playwright/Cypress) | ✅ confirmed | `grep -r "playwright\|cypress"` in `frontend/` → only protractor in `package-lock.json` (legacy). No e2e config files. |
| **B5** | Test coverage report (coverlet declared `available: false`) | ✅ confirmed | `openspec/config.yaml:60-62` — `coverage: available: false, tool: coverlet, note: No coverlet reference found in csproj or props`. |
| **B6** | Integration tests against real Postgres (44 tests not run) | 🔄 partial | `tests/IntegrationTests/JadeCapital.Api.IntegrationTests/` has ~33 test files using Testcontainers — works locally with Docker, but NOT in any CI pipeline (A1). So they run on a developer's machine but not in the merge-gate. |
| **B7** | Mutation testing (Stryker.NET) | ✅ confirmed | No `Stryker` config; not mentioned in any spec. |
| **B8** | Performance / load testing (k6, Locust) | ✅ confirmed | `scripts/` has only `wave4-smoke.sh` + `wave5-smoke.sh` — functional probes, not load. No k6/Locust/JMeter scripts. |
| **B9** | FE error reporting (Sentry/Bugsnag) | ✅ confirmed | `frontend/package.json` — no `@sentry/*` or `@bugsnag/*`. |
| **B10** | i18n / multi-language | � partial | `frontend/src/index.html:2` → `<html lang="es">`; landing-page.ts is Spanish-first. No `@angular/localize` runtime, no `@ngx-translate/core`, no JSON message catalogs. So technically Spanish-only today; no i18n infrastructure for adding English. |
| **B11** | SEO basics (OG tags, sitemap.xml, robots.txt) | ✅ confirmed | `frontend/src/index.html:1-12` has only `<title>`, `<meta charset>`, `<meta name="viewport">`, `<meta name="theme-color">`, `<meta name="description">`. No `og:title`, `og:image`, `twitter:card`, `sitemap.xml`, `robots.txt`. |
| **B12** | PWA / offline mode | ✅ confirmed | `frontend/package.json` — no `@angular/service-worker`. `angular.json` — no `serviceWorker` builder config. No `ngsw-config.json`. |
| **B13** | License + CHANGELOG + CONTRIBUTING + SECURITY.md | ✅ confirmed | `ls LICENSE* CHANGELOG* CONTRIBUTING* SECURITY*` → all absent at repo root. |
| **B14** | README.es.md | ✅ confirmed | `ls README*` → only `README.md`. |
| **B15** | Additional ADRs (only 4 exist) | ✅ confirmed | `ls docs/adr/` → `0001-auto-confirm-on-register.md`, `0002-background-service-over-hangfire.md`, `0003-remove-tier-field-from-frontend-user.md`, `0004-size-exception-audit-wave-0.md`. Missing: multi-tenant, audit decorator pattern, Stripe gateway abstraction, audit retention policy, GDPR right-to-be-forgotten. |
| **B16** | Production runbooks (deploy, rollback, incident, DR) | ✅ confirmed | `docs/runbooks/` → only `local-dev.md`. Missing: `deploy.md`, `rollback.md`, `incident-response.md`, `disaster-recovery.md`, `gdpr-data-subject-request.md`. |
| **B17** | OpenAPI export to file in CI | ✅ confirmed | `Program.cs:287-308` sets up `SwaggerGen` + `Swashbuckle` for runtime Swagger UI, but no `dotnet swagger tofile` step in CI. No `swagger.json` artifact. |
| **B18** | Account deletion UI flow | ✅ confirmed | `frontend/src/app/features/trader/settings/settings.page.ts` — settings page exists but no delete-account section. No DELETE endpoint behind it. |
| **B19** | Audit log export (CSV/JSON) for compliance | ✅ confirmed | Wave 9 added `GET /api/admin/audit/events` (read), but no `GET /api/admin/audit/events/export` endpoint. Spec explicitly defers to Wave 10+ (`openspec/changes/archive/2026-08-19-wave9-audit-finalization/proposal.md:59`). |
| **B20** | Multi-tenant AI rate limits | ✅ confirmed | `Program.cs:187-272` — rate limits are per-IP (`auth-strict`, `api-general`, `api-quotes`, `api-billing`) and per-user-id (billing only). No tenant-keyed rate limit. Multiple specs defer this to Wave 7+; still pending. |
| **B21** | Real-time alerts via SignalR (Wave 7 deferred) | ✅ confirmed | `Program.cs:173-178` + `openspec/specs/realtime/spec.md` — SignalR exists for `quotes` only. No alerts hub. |
| **B22** | Calendar integration (Google Calendar) | ✅ confirmed | Internal calendar (`/app/calendar`, `CalendarPage`) exists; no Google Calendar OAuth integration. Explicitly deferred (`openspec/changes/archive/2026-08-18-trader-strategies-alerts-planner/proposal.md:79`). |
| **B23** | Cloud AI providers (OpenAI/Anthropic) | ✅ confirmed | `Program.cs:116-129` — only Ollama registered (`IAIProvider` → `OllamaHttpClient`). No OpenAI/Anthropic providers. |
| **B24** | Real broker integration (IBKR, MT5) | ✅ confirmed | No broker abstraction; CSV/MT4 importers exist but no native broker API. Deferred per `PROJECT-STATUS.md:142`. |
| **B25** | Real virus scanner (ClamAV) | ✅ confirmed | No `IClamAvScanner` interface, no `clamav-net` package. Deferred. |
| **B26** | Migration-order fix (Wave 4e.D1) | ✅ confirmed | `infrastructure/postgres/migrations/` has 38 files; ordering issue carried forward since Wave 4 (`PROJECT-STATUS.md:110`). |
| **B27** | Streaming tokens in FE (SSE) | ✅ confirmed | No SSE endpoint; no `EventSource` consumer. Deferred to Wave 7+ per `PROJECT-STATUS.md:114`. |
| **B28** | AI signal generation from scanner | ✅ confirmed | `IAIRiskAdvisor` exists (post-trade + pre-trade); no scanner-triggered signal generation. |
| **B29** | Image optimization | ✅ confirmed | `frontend/src/index.html` references no `<link rel="preload">` for images. No Angular image-pipeline config. |
| **B30** | CDN | ✅ confirmed | Docker serves all static assets from `frontend` container; no Cloudflare/Fastly config. |
| **B31–B40** | Various operational improvements | ✅ confirmed | No FE error boundary, no per-user rate limit (IP-only), no webhook signing for outbound webhooks (Stripe only inbound). |

### C. Nice-to-have (v1.1+)

All 16 deferred items (mobile native, copy trading, dark mode toggle, social auth, etc.) are **out of scope for Wave 10** — confirmed by definition (they're explicitly post-v1.0).

---

## Additional gaps identified

The prior audit caught the **standard v1.0 readiness checklist**. Reading the actual code surfaced **18 additional gaps** the audit missed. These are grouped by severity:

### A-grade (genuinely v1 blockers — not in the original audit)

| ID | Title | Evidence |
|---|---|---|
| **G-A1** | **GDPR Art. 20 — data portability** | The audit mentions Art. 17 (right-to-be-forgotten) but **NOT** Art. 20 (right to data portability — export). Both are mandatory under GDPR Art. 15/20. A user must be able to download a JSON/CSV archive of their trades, journals, settings, alerts, strategies, etc. No endpoint exists. |
| **G-A2** | **Audit log tamper-evidence** | `audit.events` rows can technically be UPDATEd if the `AuditDbContext` is compromised. No hash chain (each row links to previous row's hash) or append-only enforcement beyond code discipline. A tamper-evident log is required by SOC 2 / ISO 27001. For v1.0: at minimum, document the trust model (DB-level role separation) + add a Wave 11 follow-up. |
| **G-A3** | **Cookie consent banner** (EU ePrivacy Directive) | The audit lists "Cookie consent" under A8 but the **technical banner** is separate from the legal docs. A user from EU must be able to opt-out of non-essential cookies. No banner, no consent storage, no script gating. |
| **G-A4** | **Email deliverability (SPF/DKIM/DMARC)** | `Mail__*` env vars exist (`.env:36-42`) and the SMTP transport is wired (Mailkit + Mailpit fallback in `Program.cs:94-97`). But no DNS record documentation; no DKIM signing key rotation; no `Return-Path` configuration; no bounce handling. Production email WILL land in spam without these. |
| **G-A5** | **Welcome email + onboarding flow** | The user can register (POST `/api/auth/register`) but no welcome email is sent. No first-login wizard. No "tell us about your trading" questionnaire that would let the AI risk advisor work better from day 1. |
| **G-A6** | **GDPR cookie consent banner** | Distinct from G-A3 — even outside EU, Chrome/Firefox require opt-in for analytics. The FE has zero analytics today, so this is "future-proofing" — but the audit doesn't flag it. |

### B-grade (high-quality gaps — should land in Wave 10 or Wave 11)

| ID | Title | Evidence |
|---|---|---|
| **G-B1** | **Feature flags / kill switches** | No `IFeatureFlag` interface, no LaunchDarkly/Unleash/Flagsmith integration, no DB-backed flag table. A kill switch would let us disable a hot feature (e.g., AI risk advisor) without a redeploy. Risky for v1 prod. |
| **G-B2** | **Per-user rate limiting** | `Program.cs:187-272` — every policy is per-IP (`api-{ip}`, `auth-{ip}`) except `api-billing` (per-user). An authenticated trader abusing the API from one IP can hammer `POST /api/trades` with no user-level throttle. |
| **G-B3** | **API versioning strategy** | `Program.cs:290` — `SwaggerDoc("v1", ...)`. But no `Asp.Versioning` middleware; URL is `/api/...` not `/api/v1/...`. No deprecation/sunset header support. Future "v2" rollout will break clients silently. |
| **G-B4** | **Frontend global error boundary** | `frontend/src/app/app.config.ts` — no global `ErrorHandler` provider. A runtime error in a lazy-loaded route crashes the SPA with a white screen. |
| **G-B5** | **Database connection pooling tuning** | `docker-compose.yml:104` — `Include Error Detail=true` in the connection string; no `Maximum Pool Size`, `Minimum Pool Size`, `Connection Idle Lifetime`, `Connection Pruning Interval`. Npgsql defaults to 100 max connections; for 1 API instance + 1 migrate + pgAdmin that's fine, but for prod load testing we need explicit tuning. |
| **G-B6** | **ORM N+1 query detection** | EF Core 9 + no `EFCore.NPlusOneQueryDetector` middleware. Code review only. A future Wave 11 perf slice would add `Microsoft.EntityFrameworkCore.Proxies`-style detection or `EFCore.PerformanceTester`. |
| **G-B7** | **Redis backup strategy** | `docker-compose.yml:23-35` — Redis with `--appendonly yes` (good), but no cron for `BGSAVE` snapshots, no off-site upload. Quote cache loss is recoverable; rate-limit + refresh-token loss is not. |
| **G-B8** | **MinIO backup strategy** | `docker-compose.yml:50-69` — MinIO with `minio-data` volume, but no `mc mirror` snapshot script. Trade review attachments are user-uploaded; loss is unrecoverable. |
| **G-B9** | **Customer support channel** | No `support@jadecapital.com` documented, no Intercom/Zendesk widget, no `/help` page beyond FAQ. |
| **G-B10** | **Pricing tiers → real Stripe Price IDs** | `infrastructure/postgres/migrations/20260814_0008_SeedPlans.sql` seeds plan data, but `StripeOptions.DefaultPriceId` + `CustomerPortalConfigurationId` are null. No documented process for "this is how we map a plan to a Stripe Price ID in prod". |
| **G-B11** | **Audit log backup** | `audit.events` has the 90-day retention BackgroundService (Wave 9 9b.1), but the **rows themselves are not backed up off-site**. If the DB is lost, the compliance trail is lost too. |
| **G-B12** | **Outbound webhook signing** | Only Stripe webhooks come IN (with signature verification). The codebase has NO outbound webhooks today. If we add them later (e.g., Zapier integration), they'll need HMAC signing. Flag now to avoid retroactive fixes. |
| **G-B13** | **FE bundle size budget enforcement** | `frontend/angular.json:30` declares `maximumWarning: "1mb"` for initial bundle. But no CI step enforces it; a lazy-loaded module could silently bloat. |
| **G-B14** | **Contract testing (FE ⇄ BE)** | No Pact broker, no OpenAPI schema validation test in CI. The FE generates types manually from the BE swagger — drift is possible. |
| **G-B15** | **Visual regression testing** | No Percy/Chromatic/Playwright visual diff. CSS changes can silently break the mobile-nav. |
| **G-B16** | **Penetration testing history** | Never done. Pre-launch must have at least an OWASP ZAP baseline scan. |
| **G-B17** | **Admin role hierarchy** | `ITenantContext.IsSuperAdmin` exists (Wave 7 7a.1). But only ONE admin tier. For a SaaS, the distinction "super-admin (cross-tenant)" vs "tenant-admin (own tenant only)" matters for RBAC. |
| **G-B18** | **Stripe webhook idempotency table** | `IStripeWebhookEventRepository` exists (append-only, Wave 6) — this is **confirmed implemented**. ✅ NOT a gap. (Listed for completeness — the audit implicitly assumes this exists; it does.) |

### C-grade (v1.1+ candidates)

- **G-C1**: Multi-region / disaster recovery (no failover plan, no read-replica strategy).
- **G-C2**: Chaos engineering (no game-day drills).
- **G-C3**: SOC 2 / ISO 27001 certification (months-long process).
- **G-C4**: mTLS between services (single docker network — no service-to-service TLS).
- **G-C5**: DNS CAA records, DNSSEC.
- **G-C6**: Encryption at rest (Postgres TDE).
- **G-C7**: Audit log partitioning by month (Postgres native partitioning — for >100M rows).
- **G-C8**: AI signal generation from scanner (cross-Wave with B28).
- **G-C9**: Mobile native (iOS/Android — out of scope per the audit's C-tier).

---

## Proposed slice breakdown

The prior audit's recommendation ("close A1-A10 + low-effort Bs") is **too aggressive for one wave** — 10 critical items + 8 important items = ~6,000–8,000 LOC of work + ~30 new tests + 8 new spec files. That is at least 2 waves at Wave 9 cadence. Wave 10 targets **the must-ship set** that genuinely blocks a credible v1.0 launch; Wave 11 picks up the rest.

| Slice | Sub-scope | Gaps covered | Effort | Dependencies | Target LOC |
|---|---|---|---|---|---|
| **10.1 — CI/CD pipeline + dependency scanning** | `.github/workflows/ci.yml` + dependabot + nightly scheduled | A1, A10, B6 (CI-runnable integration tests) | **High** (CI infra is its own thing) | none | ~600 LOC (config) + ~200 LOC (test selection logic) |
| **10.2 — Secrets + deployment story** | `docker-compose.prod.yml` + `Dockerfile.prod` + Vault/AWS Secrets Manager runbook + production `.env.example` | A2, A5 | Medium | none | ~500 LOC (Docker compose + Dockerfile) + ~300 LOC (docs) |
| **10.3 — Security headers + TLS termination** | CSP/HSTS in nginx.conf + Caddy reverse proxy + certbot script | A3, A7 | Medium | none | ~150 LOC (nginx + Caddyfile) |
| **10.4 — Backup + migration strategy** | Backup scripts (Postgres + Redis + MinIO) + cron config + `wal-g` setup + DR runbook + Wave 4e.D1 migration-order fix | A6, B26, B16 (partial) | Medium | none | ~400 LOC (scripts) + ~200 LOC (migration renumbering) |
| **10.5 — Legal docs + GDPR + account deletion** | ToS + Privacy Policy + Cookie consent + `DELETE /api/users/me` + data-export endpoint + GDPR runbook | A8, A9, G-A1, G-A3, G-A4 (SPF/DKIM/DMARC docs), G-A5 (welcome email), B18, B16 (gdpr runbook) | **High** (multi-spec, multi-module) | 10.1 (needs CI to enforce spec scenarios) | ~1,200 LOC + ~2 new spec files |
| **10.6 — Documentation + observability-light + SEO + coverage** | LICENSE, CHANGELOG, CONTRIBUTING, SECURITY.md, README.es.md, 4 new ADRs, OpenAPI export in CI, coverlet + coverage gate, sitemap.xml + robots.txt + OG tags, Sentry SDK hooks (BE + FE), Stripe production key validation | B5, B9, B11, B13, B14, B15, B17, A4 (Stripe prod key validation) | Medium | 10.1 (CI runs the OpenAPI export), 10.3 (security headers) | ~800 LOC + ~1 new spec file |
| **Wave 10 total** | 6 chained PRs | **12 critical + 8 important + 8 additional** | **6 PRs over 4-6 weeks** | | ~3,950 LOC + ~3 new spec files |

### Slice-by-slice breakdown (detail)

#### Slice 10.1 — CI/CD pipeline (A1, A10, B6)

- **`.github/workflows/ci.yml`** — 4 jobs:
  1. `lint-backend`: `dotnet format --verify-no-changes` + Roslynator rules
  2. `test-backend`: `dotnet test --nologo --verbosity minimal` (excludes integration) — must pass in <5 min
  3. `test-integration`: `dotnet test --filter "FullyQualifiedName~IntegrationTests"` with `services: postgres, redis` (Testcontainers — Docker-in-Docker) — must pass in <10 min
  4. `test-frontend`: `npm --prefix frontend test -- --ci` + `npm --prefix frontend run build`
- **`.github/workflows/nightly.yml`** — nightly OWASP ZAP baseline scan + `npm audit` + `dotnet list package --vulnerable --include-transitive`
- **`.github/dependabot.yml`** — weekly BE (NuGet) + FE (npm) updates; auto-merge for patch versions only
- **`.github/pull_request_template.md`** — checkbox checklist (spec link, tests, migration if needed)
- **No new spec** — covered by `openspec/specs/` operational hygiene (NEW spec: `ci-infrastructure`).

#### Slice 10.2 — Secrets + deployment story (A2, A5)

- **`docker-compose.prod.yml`** — production variant: removes Mailpit, removes MinIO (or keeps with HTTPS), removes port-expose of postgres/redis, forces `restart: always`, requires TLS termination at edge (Caddy/Traefik/cloud LB).
- **`Dockerfile.prod`** (BE) — multi-stage with `--locked-mode` restore + non-root user + read-only root filesystem + healthcheck.
- **`Dockerfile.prod`** (FE) — same hardening; serves `dist/` over nginx with hardened nginx.conf.
- **`infrastructure/production/README.md`** — runbook on secrets management (Vault / Doppler / AWS Secrets Manager), env-var injection patterns, secret rotation cadence.
- **No new spec** — covered by `deployment-automation` (NEW).

#### Slice 10.3 — Security headers + TLS termination (A3, A7)

- **`infrastructure/nginx/nginx.conf`** — add `Content-Security-Policy` (with nonce), `Strict-Transport-Security: max-age=63072000; includeSubDomains; preload`, `Permissions-Policy`, `Cross-Origin-Opener-Policy`, `Cross-Origin-Embedder-Policy`, `Cross-Origin-Resource-Policy`.
- **`infrastructure/caddy/Caddyfile`** — Caddy reverse proxy with auto-TLS (Let's Encrypt) for `jadecapital.com`. Used in dev (self-signed via `caddy trust`) and prod (DNS-01 challenge).
- **`scripts/setup-tls.sh`** — one-shot certbot helper for environments without Caddy (legacy nginx).
- **`scripts/verify-security-headers.sh`** — curl + `grep` for the 7 expected headers; fails CI if missing.
- **NEW spec**: `security-headers` (CSP nonce strategy, HSTS preload list, COEP/COOP/CORP rationale).

#### Slice 10.4 — Backup + migration strategy (A6, B16, B26)

- **`scripts/backup-postgres.sh`** — `pg_dump` → gzip → `aws s3 cp` (or `rclone` to B2/Backblaze). Cron-friendly. 30-day retention on bucket.
- **`scripts/backup-redis.sh`** — `BGSAVE` → copy `dump.rdb` to S3.
- **`scripts/backup-minio.sh`** — `mc mirror --remove` to a backup bucket.
- **`scripts/restore-postgres.sh`** — accepts a date or a snapshot file; runs `pg_restore` against a clean DB; runs migrations.
- **`infrastructure/wal-g/config.yaml`** — if using `wal-g` for PITR instead of (or alongside) `pg_dump`.
- **Wave 4e.D1 fix**: rename migrations from `0009` → `0009_legacy_name` then `0009_renamed` so the alphabetical order matches dependency order. Update `infrastructure/postgres/migrate.Dockerfile` to be order-agnostic (use `ls | sort` + dependency array).
- **NEW runbook**: `docs/runbooks/backup-recovery.md` + `docs/runbooks/disaster-recovery.md`.
- **NEW spec**: `backup-strategy` (RPO ≤ 1h, RTO ≤ 4h, retention 90d hot + 365d cold).

#### Slice 10.5 — Legal docs + GDPR + account deletion (A8, A9, G-A1, G-A3, G-A4, G-A5, B18, B16)

- **Legal pages** in `frontend/src/app/features/public/legal/`:
  - `terms.page.ts` — placeholder structure with `i18n` keys; copy deferred to lawyer (out of scope)
  - `privacy.page.ts` — same
  - `cookie-consent.page.ts` — opt-in/out banner; stores choice in `localStorage` + DB column `users.cookie_consent_accepted_at`
- **`POST /api/auth/consent`** — endpoint for explicit acceptance on register + on cookie banner change
- **`DELETE /api/users/me`** — right-to-be-forgotten endpoint:
  1. Soft-delete `users` row (`IsDeleted = true`)
  2. Cascade soft-delete every row where `user_id = @userId` across `trading`, `billing`, `audit` (except `audit.events` — append-only with `user_id` set to NULL per retention policy; Wave 9 already supports this)
  3. Schedule hard-delete after 30-day grace period (`users.scheduled_for_hard_delete_at`)
  4. Emit `audit.events` row with `EntityType = "User"` and `Action = Deleted` (the **one** allowed exception to `UserAuditDecorator.DeleteAsync`'s `NotSupportedException`)
  5. Revoke all refresh tokens
- **`GET /api/users/me/export`** — data-portability endpoint (GDPR Art. 20):
  1. Streams JSON: `trades`, `journals`, `trade_reviews`, `strategies`, `alerts`, `planner_sessions`, `pre_trade_checklists`, `risk_profile`, `settings`
  2. Excludes `audit.events` (compliance log) + `stripe_webhook_events` (system)
  3. Returns 200 with `Content-Disposition: attachment; filename=jadecapital-export-{userId}.json`
- **`IUserCascadeDeletor`** interface — module-agnostic; each module registers a `ICascadeDeletor<TAggregate>` for its entities.
- **`POST /api/auth/welcome-email`** — on register, send Mail via existing Mail__ pipeline; idempotent (skip if already sent in the last 7 days)
- **`scripts/setup-email-deliverability.md`** — SPF/DKIM/DMARC DNS records doc; DKIM signing key rotation.
- **NEW specs**:
  - `gdpr-compliance` (right-to-be-forgotten + data portability + cookie consent)
  - `account-lifecycle` (DELETE /api/users/me + cascade + grace period)

#### Slice 10.6 — Documentation + observability-light + SEO + coverage (B5, B9, B11, B13, B14, B15, B17, A4)

- **`LICENSE`** — MIT (or chosen by user; flag as open question)
- **`CHANGELOG.md`** — Keep-a-Changelog 1.1.0 format; first entry = Wave 0; entries for Waves 1-9 backfilled
- **`CONTRIBUTING.md`** — dev setup, commit convention, PR template
- **`SECURITY.md`** — vulnerability disclosure process, supported versions, contact email `security@jadecapital.com`
- **`README.es.md`** — translated README in Spanish (mirror structure, not auto-generated)
- **4 new ADRs** in `docs/adr/`:
  - `0005-multi-tenant-architecture.md` — `tenant_id` JWT claim + `TenantContextMiddleware` (Wave 6 6c.2)
  - `0006-audit-decorator-pattern.md` — `DecoratedRepository<T>` + Scrutor `services.Decorate<...>` (Wave 6/7/8)
  - `0007-stripe-gateway-abstraction.md` — `IStripeGateway` + Stub fallback (Wave 6 6a.1)
  - `0008-audit-retention-policy.md` — 90-day retention BackgroundService (Wave 9 9b.1)
  - Plus one NEW for Wave 10:
  - `0009-gdpr-right-to-be-forgotten.md`
- **CI step in `.github/workflows/ci.yml`** — `dotnet swagger tofile --output artifacts/swagger.json` → upload as artifact
- **Coverlet** — add `coverlet.collector` package to all `*.UnitTests.csproj` + `*.IntegrationTests.csproj`; add `--collect:"XPlat Code Coverage"` to CI test commands; gate at 70% line coverage (the Wave 9 audit decorators alone push us well above 70%)
- **Stripe production key validation** (A4) — `StripeOptions` adds `IValidateOptions<StripeOptions>` that fails startup if `ApiKey.StartsWith("sk_test_local_dev_placeholder")` OR `ApiKey.Length < 32` OR `WebhookSecret.IsNullOrWhitespace()`. The dev `.env` uses `StripeOptions.SkipValidation = true` (or a `--dev` switch).
- **Sentry hooks** (B9) — BE: `Sentry.AspNetCore` 4.x + `SentryOptions` configured via env. FE: `@sentry/angular` 8.x + `Sentry.init()`. Only initializes when `Sentry__Dsn` env is set (skip in dev).
- **SEO** (B11) — `sitemap.xml` generated by a new `GET /sitemap.xml` endpoint (lists public routes + pricing + FAQ); `robots.txt` static in nginx; OG tags + `og:image` (a static landing-card image) added to `index.html`.
- **NEW spec**: `observability-light` (Serilog structured + Sentry optional + 5xx rate alert).

---

## Proposed specs

Per OpenSpec convention, every NEW capability lands in `openspec/changes/2026-08-18-wave10-v1-readiness/specs/<slug>/spec.md`. Wave 10's specs:

| Spec | NEW/DELTA | Slug | Description | Scenarios expected |
|---|---|---|---|---|
| `ci-infrastructure` | **NEW** | `ci-infrastructure` | GitHub Actions CI workflow (4 jobs: lint-backend, test-backend, test-integration, test-frontend) + Dependabot config + nightly OWASP/snyk scan. Defines the merge-gate. | ~8 (lint pass, BE test pass, integration test pass with Docker, FE test pass, FE build pass, OWASP scan, dependabot dry-run, PR template present) |
| `deployment-automation` | **NEW** | `deployment-automation` | `docker-compose.prod.yml` + `Dockerfile.prod` (BE+FE) + secrets-management runbook (Vault/Doppler/AWS SM) + healthcheck at edge. | ~6 (prod compose validates, BE Dockerfile hardened, FE Dockerfile hardened, secrets runbook present, healthcheck passes, no Mailpit/MinIO exposed in prod) |
| `security-headers` | **NEW** | `security-headers` | CSP with nonce, HSTS preload, Permissions-Policy, COEP/COOP/CORP. Caddy reverse proxy with auto-TLS. | ~7 (CSP nonce rotates per request, HSTS preload header, Permissions-Policy denies unused APIs, COEP/COOP/CORP set, TLS auto-renewal, self-signed in dev, prod-only TLS) |
| `backup-strategy` | **NEW** | `backup-strategy` | Postgres `pg_dump`/`wal-g` cron + Redis BGSAVE + MinIO mirror. RPO ≤ 1h, RTO ≤ 4h. Restore script + DR runbook + Wave 4e.D1 migration-order fix. | ~8 (PG backup cron, PG restore dry-run, Redis BGSAVE, MinIO mirror, restore-from-backup E2E, migration-order idempotent, DR runbook, RPO/RTO doc) |
| `gdpr-compliance` | **NEW** | `gdpr-compliance` | Right-to-be-forgotten (`DELETE /api/users/me` with cascade + grace period) + data portability (`GET /api/users/me/export`) + cookie consent banner + ToS/Privacy acceptance on register. | ~10 (DELETE user 200 + cascade verified, DELETE user with grace period emits audit row, export contains trades/journals/etc, export excludes audit.events, cookie consent stored, ToS acceptance required for register, etc.) |
| `account-lifecycle` | **NEW** | `account-lifecycle` | User lifecycle states: Active → SoftDeleted (cascade) → ScheduledHardDelete (30d grace) → HardDeleted. Background service for hard-delete sweep. | ~6 (soft-delete cascade contract, hard-delete sweep idempotent, grace period respected, audit row emitted, refresh tokens revoked, deletion email sent) |
| `observability-light` | **NEW** | `observability-light` | Serilog structured logs + optional Sentry BE/FE (only init when `Sentry__Dsn` set) + `/health/live` + `/health/ready` already exist. OpenAPI export to `swagger.json` in CI. | ~6 (Serilog request log, Sentry BE exception capture, Sentry FE exception capture, OpenAPI file in CI artifact, health endpoints return 200, dev env skips Sentry) |
| `production-readiness` | **NEW** | `production-readiness` | Documentation bundle: LICENSE + CHANGELOG + CONTRIBUTING + SECURITY.md + README.es.md + 5 new ADRs (0005-0009). Also: Stripe production key validation (`IValidateOptions<StripeOptions>`). SEO (sitemap.xml + robots.txt + OG tags). Test coverage report (coverlet + 70% gate). | ~10 (LICENSE present, CHANGELOG keeps entries, CONTRIBUTING has commit convention, SECURITY has disclosure email, README.es.md translated, 5 ADRs present, sitemap.xml generated, OG tags in index.html, coverage gate enforced, Stripe prod key validated at startup) |

**Total**: 8 NEW specs, ~61 spec scenarios. Wave 10 spec coverage lands all 8 in the same change directory; the proposal will likely split into 2 proposal.md docs (10a "operations" covering slices 10.1-10.4 + 10.6; 10b "compliance" covering slice 10.5) — TBD at proposal phase.

---

## Chained PR order

Per the orchestrator's preflight: `feature-branch-chain` strategy with `size:exception` per slice (Wave 5/6/7/8/9 precedent). 800-line/PR review budget per slice; each slice explicitly justifies `size:exception` if exceeded.

| PR # | Slice | Branch (proposed) | Title | Target LOC | size:exception? |
|---|---|---|---|---:|---|
| **#1** | 10.1 | `feature/wave10-ci-cd` | "Wave 10.1 — CI/CD pipeline + dependency scanning" | ~800 | likely (CI infra + nightly + dependabot) |
| **#2** | 10.2 | `feature/wave10-deployment` | "Wave 10.2 — Production deployment story + secrets management" | ~800 | likely (compose + Dockerfiles + runbook) |
| **#3** | 10.3 | `feature/wave10-security-headers` | "Wave 10.3 — Security headers + TLS termination" | ~150 | no |
| **#4** | 10.4 | `feature/wave10-backups` | "Wave 10.4 — Backup strategy + migration order fix" | ~600 | likely (scripts + migration renumbering) |
| **#5** | 10.5 | `feature/wave10-gdpr` | "Wave 10.5 — Legal docs + GDPR + account deletion" | ~1,200 | **yes** (multi-module cascade + 2 new endpoints + welcome email) |
| **#6** | 10.6 | `feature/wave10-docs-observability` | "Wave 10.6 — Documentation + observability-light + SEO + coverage" | ~800 | likely (many doc files + 5 ADRs + CI step changes) |
| **Wave 10 total** | | 6 chained PRs | | **~4,350 LOC** | 5 of 6 slices need exception |

**Order rationale**:
- **10.1 first** (CI): every later slice benefits from the merge-gate; CI catches bugs in 10.2-10.6 immediately.
- **10.2 + 10.3 second** (deployment + security headers): orthogonal to each other and to 10.1, but block any "first deploy" attempt. Can be parallelized as feature/10a-deployment + feature/10b-security-headers if a 2-PR parallel branch is preferred.
- **10.4 third** (backups): depends on 10.1 (CI runs the backup-restore E2E test) + 10.2 (prod compose knows about backup volumes). Independent of 10.3.
- **10.5 fourth** (GDPR): the heaviest slice; lands AFTER CI exists so the GDPR scenarios are gated. Depends on 10.1 only.
- **10.6 last** (docs + observability): picks up loose ends; depends on 10.3 (security headers verified), 10.1 (CI runs OpenAPI export + coverlet), 10.5 (GDPR runbook references the docs).

---

## Risks

### Per-slice risks

**10.1 — CI/CD**:
- **GitHub-hosted runners cost**: Testcontainers Postgres needs Docker-in-Docker; may need `ubuntu-latest-4-cores` runners at ~$0.008/min × ~10 min × ~50 PRs/week = ~$20/week. Trivial, but flag for budget.
- **Cache hit rate**: NuGet + npm cache keys must match `Directory.Build.props` + `package-lock.json` exactly; cache miss doubles CI time. Use `actions/cache@v4` with hash files.
- **OWASP ZAP nightly**: full scan takes ~30 min; budget for nightly failures (false positives need triage).

**10.2 — Deployment story**:
- **`Dockerfile.prod` size**: with `--locked-mode` + `--source-link` + SBOM, the prod image may grow. Use `dotnet publish /p:PublishTrimmed=true` for non-AOT-incompatible paths; verify with smoke test.
- **Vault/Doppler integration**: out-of-band secret rotation may break running pods; needs graceful restart strategy. Document in runbook.
- **`docker-compose.prod.yml` drift**: dev vs prod compose drift is a classic foot-gun. Use `docker compose.override.yml` pattern OR a single compose with profiles (`--profile dev`).

**10.3 — Security headers**:
- **CSP nonce**: Angular injects `<script>` tags at build time; nonce-based CSP requires a per-request nonce passed via meta tag → Angular template. Wave 10 ships a strict CSP + `'unsafe-inline'` fallback for the first version; future Wave 11 hardens.
- **HSTS preload**: once submitted to `hstspreload.org`, removal takes months. Test thoroughly before submitting.
- **Caddy TLS in dev**: self-signed certs break curl/Jest; need `NODE_EXTRA_CA_CERTS` + Jest `setupFiles` for dev.

**10.4 — Backups**:
- **Off-site upload credential**: `aws s3 cp` needs IAM creds. Where do those live? Same Vault pattern as 10.2. Or use `rclone` with a config file mounted as a secret.
- **`pg_dump` vs `wal-g`**: `pg_dump` is logical (slow restore, no PITR); `wal-g` is physical (fast restore, PITR). For v1.0, start with `pg_dump` daily + WAL archiving via `wal-g` for hourly RPO.
- **Migration-order fix**: renumbering 38 migration files is a high-risk rename; one missed reference breaks the migrate service. Strategy: use `git mv` with care + run the full Testcontainers migration suite + manual `psql -f` against a fresh DB.

**10.5 — GDPR + account deletion**:
- **Cascade blast radius**: 17 user-owned aggregates across 3 modules (Identity, Trading, Billing). A bug in `IUserCascadeDeletor` registration = user-visible data leak post-deletion. Mitigation: integration test that registers a user, opens a trade, creates a journal, sets a strategy, then DELETEs + asserts all rows have `IsDeleted=true` (with `IgnoreQueryFilters()`).
- **Grace period**: 30 days is long enough for "oops" recovery but short enough for GDPR compliance. If a user wants to undelete, they re-register with the same email → restore flow. Document in `gdpr-data-subject-request.md`.
- **Audit row for deletion**: `UserAuditDecorator.DeleteAsync` currently throws `NotSupportedException`. Wave 10's DELETE endpoint must bypass the decorator OR the decorator must add an exception path. **Recommendation**: add a new method `IUserRepository.AnonymizeAsync(Guid userId, ct)` that sets `Email = "deleted-{userId:N}@anonymized.local"`, `DisplayName = "Deleted User"`, `IsDeleted = true` (does NOT throw). The cascade then runs on the anonymized user. Audit row is emitted by the new `DeleteUserHandler`.
- **Data export format**: JSON is fine for v1.0; CSV per-aggregate is a Wave 11 nice-to-have. GDPR mandates "structured, commonly used, machine-readable format" — JSON qualifies.

**10.6 — Docs + observability + SEO + coverage**:
- **Coverage gate at 70%**: if current coverage is < 70%, the CI step fails immediately. Run `dotnet test /p:CollectCoverage=true` first to get the baseline; if < 70%, raise the gate in 5% increments over Wave 10.
- **OpenAPI export drift**: if the export is checked in (vs artifact-only), it'll diverge from runtime Swagger within hours. Recommendation: artifact-only (don't commit `swagger.json`).
- **Sentry init order**: Sentry SDK must init BEFORE the Angular app boots (in `main.ts`); a late init misses the bootstrap errors. Document in `observability-light` spec.
- **Stripe production key validation**: if we validate strictly, the dev `.env` (with placeholder) will FAIL startup. Mitigation: dev uses `StripeOptions.SkipValidation = true` OR an env-flag (`ASPNETCORE_ENVIRONMENT=Development`).

### Cross-slice risks

- **Sequencing dependency risk**: 10.5 (GDPR) is the largest slice. If it slips, it blocks the "we're v1.0 ready" claim. Mitigation: ship 10.1-10.4 as a "v1.0.0-rc1" tag (operational hardening complete) and gate the v1.0.0 tag on 10.5.
- **`size:exception` precedent erosion**: Wave 5/6/7/8/9 all needed exceptions. If Wave 10's 10.5 needs exception, that's 6 consecutive waves with exception — flag for `branch-pr` skill review (the chain becomes harder to review at this point).
- **CI runner quota**: GitHub Actions free tier = 2000 min/month. Wave 10's CI runs ~10 min per PR × ~50 PRs = ~500 min. Plus the nightly (~30 min × 30 days = 900 min). Total ~1400 min — within budget but tight. If we hit the cap, migrate to GitHub-hosted paid runners or self-hosted.
- **Compliance scope creep**: GDPR Art. 17 + Art. 20 + cookie consent is a lawyer's domain. Wave 10 ships the ENGINEERING; the legal COPY (ToS text, Privacy Policy text) is provided by user/legal counsel, NOT by the spec. Flag in Open Questions.

---

## Dependencies between slices

```
10.1 (CI) ──┬──> 10.2 (deploy) ──> 10.4 (backups)
             │                          ▲
             ├──> 10.3 (security headers)
             │
             ├──> 10.5 (GDPR) ─────────────────────────> 10.6 (docs/obs)
             │                                              ▲
             └──────────────────────────────────────────────┘
```

- **10.1 unblocks all others**: every slice needs CI to enforce its scenarios.
- **10.2 + 10.3 are parallel**: deployment story vs security headers don't touch each other.
- **10.4 depends on 10.2**: the backup scripts need to know the prod compose volume layout.
- **10.5 depends on 10.1 only**: GDPR scenarios are independent of deployment story.
- **10.6 depends on 10.1, 10.3, 10.5**: OpenAPI export uses CI (10.1); Stripe prod key validation touches prod compose (10.2); documentation references the GDPR runbook (10.5); coverage gate uses CI (10.1).

**Parallelizable pairs** (if team capacity allows):
- 10.2 + 10.3 (different files, different reviewers)
- 10.4 + 10.5 (10.4 is shell/scripts; 10.5 is .NET/Angular)

**Critical path**: 10.1 → 10.5 → 10.6. That's 3 PRs. If we run 10.2 + 10.3 + 10.4 in parallel branches, the critical path is still 10.1 → 10.5 → 10.6 (~3 PRs of heavy work) + the parallel merges.

---

## Out of scope for Wave 10 (deferred)

Per the audit + the additional gaps identified above, the following are **explicitly deferred** to Wave 11+:

### From the original audit's B-tier (deferred per Wave 10 brief)
- **B1** — Full observability (OpenTelemetry traces, Prometheus metrics, custom dashboards). Wave 10 ships Sentry hooks only; OTel/Prom is Wave 11+.
- **B2** — External uptime monitoring (UptimeRobot, Better Stack). Wave 10 ships the `/health/*` endpoints; external monitoring is an ops decision.
- **B3** — WCAG a11y audit + axe-core integration. Wave 11 (mobile-first design benefits from a11y pass).
- **B4** — E2E tests in CI (Playwright). Wave 11 — once CI infra (10.1) exists, E2E is a natural follow-up.
- **B6** — Integration tests as a CI REQUIREMENT (they're local-only today). Actually **partially in Wave 10** via 10.1; full coverage is Wave 11.
- **B7** — Mutation testing (Stryker.NET). Wave 11+.
- **B8** — Performance / load testing (k6, Locust). Wave 11+ (after deploy story exists, 10.2).
- **B10** — i18n / multi-language runtime. Wave 11+. The FE is Spanish-first; i18n infrastructure is a refactor.
- **B12** — PWA / offline mode. Wave 11+.
- **B19** — Audit log CSV/JSON export. **Partially in Wave 10** via the user data export endpoint (covers GDPR Art. 20). Admin audit export is Wave 11+.
- **B20** — Multi-tenant AI rate limits. Wave 11+.
- **B21** — Real-time alerts via SignalR. Wave 11+.
- **B22** — Google Calendar integration. Wave 11+.
- **B23** — Cloud AI providers (OpenAI/Anthropic). Wave 11+.
- **B24** — Real broker integration (IBKR, MT5 native). Wave 11+.
- **B25** — Real virus scanner (ClamAV). Wave 11+.
- **B27** — Streaming tokens in FE (SSE). Wave 11+.
- **B28** — AI signal generation from scanner. Wave 11+.
- **B29, B30** — Image optimization, CDN. Wave 11+.

### From additional gaps (deferred)
- **G-A2** — Audit log tamper-evidence (hash chain). Wave 11+.
- **G-B1** — Feature flags / kill switches. Wave 11+ (architectural decision needed).
- **G-B3** — API versioning strategy. Wave 11+ (breaking change risk for v1.0 to add `/v1/` prefix; defer to v1.1).
- **G-B4** — FE global error boundary. Wave 11+.
- **G-B5** — DB connection pooling tuning. Wave 11+ (perf-driven; needs load test data first).
- **G-B6** — ORM N+1 detection. Wave 11+ (review-driven for now).
- **G-B7, G-B8** — Redis + MinIO backup strategies. **Partially in Wave 10** via 10.4 scripts; full automation is Wave 11+.
- **G-B9** — Customer support channel. Out of scope (operational decision).
- **G-B10** — Pricing tiers → Stripe Price ID mapping. Wave 11+ (needs real Stripe account).
- **G-B12** — Outbound webhook signing. Deferred until outbound webhooks exist.
- **G-B13** — FE bundle size budget enforcement in CI. **Partially in Wave 10** via 10.1 CI; enforcement gate is Wave 11+.
- **G-B14** — Contract testing (Pact). Wave 11+.
- **G-B15** — Visual regression testing. Wave 11+.
- **G-B16** — Penetration testing. Wave 11+ (external engagement).
- **G-B17** — Admin role hierarchy. Wave 11+ (RBAC refactor).
- **G-C1–G-C9** — All C-tier items (multi-region, chaos engineering, SOC 2, mTLS, DNSSEC, TDE, partitioning, etc.). Post-v1.0.

### Explicitly NOT in Wave 10 (out of scope per the audit's C-tier)
- All C-tier items: mobile native, copy trading, dark mode toggle, social auth, etc. Confirmed out of scope.

---

## Recommendation

**GO** for Wave 10 — proceed to sdd-propose.

### Rationale

1. **Scope is bounded**: 6 chained PRs × ~700 LOC avg = ~4,200 LOC + 8 new spec files. Wave 9 shipped 2,250 LOC + 30 scenarios in 5 PRs; Wave 10 is ~2× the work but spread over 6 PRs. Within the project's historical envelope.
2. **Critical path is clear**: 10.1 (CI) → 10.5 (GDPR) → 10.6 (docs). The 3-parallel branches (10.2, 10.3, 10.4) can run alongside, making the wall-clock calendar time ~4-6 weeks of focused work even though it's 6 PRs of code.
3. **No new module needed**: every slice touches existing modules (Host, Identity, Trading, Billing) + the docs/specs. No new csproj, no new folder structure, no new architectural decision (the 5 ADRs to add are documentation, not architecture).
4. **Defers are honest**: 30+ items deferred to Wave 11+. None of them is a v1.0 blocker. The user's "¿qué falta para culminar v1?" gets a credible answer: "Wave 10 ships 12 critical + 8 important + 8 additional gaps; v1.0.0 tag goes on the merge of 10.6".
5. **TDD-friendly**: every slice has clear RED → GREEN paths. The GDPR cascade (10.5) is the only one that requires a careful test fixture (cascading soft-delete across modules) — Wave 9's audit decorators + cascade precedents give us the template.

### Effort estimate

- **Calendar time** (1 dev, full-time): **4-6 weeks** per the orchestrator's preflight.
- **PR count**: 6 chained.
- **Tests added**: ~50 new BE + ~15 new FE (cookie consent + GDPR endpoints + coverlet gates + OpenAPI export).
- **New spec files**: 8 (per the table above).
- **New ADRs**: 5 (0005-0009).
- **New runbooks**: 4 (deploy, backup-recovery, disaster-recovery, gdpr-data-subject-request).

### Release target

- **v1.0.0-rc1** tag: after PR #4 merges (10.1-10.4 done — operational hardening complete).
- **v1.0.0** tag: after PR #6 merges (10.5 + 10.6 done — legal/compliance/docs complete).

### Risk-adjusted verdict

Wave 10 is **the right size**. Cutting scope further (e.g., deferring 10.5 GDPR to Wave 11) would leave v1.0 non-compliant with GDPR Art. 17/20 — that's a launch blocker in EU markets. Expanding scope (e.g., adding OTel/Prom to 10.6) would push calendar time past 6 weeks and require observability design decisions that should happen in a dedicated Wave 11 slice.

**Recommended next phase**: sdd-propose with 2 proposal.md files (10a "operations" for slices 10.1-10.4 + 10.6; 10b "compliance" for slice 10.5) — both in the same `openspec/changes/2026-08-18-wave10-v1-readiness/` directory. This keeps the change atomic at the OpenSpec layer while letting the proposal phase parallelize the two workstreams.

---

## Open questions

These questions need user answers before sdd-propose can write the proposal with confidence:

1. **LICENSE choice** (B13): MIT, Apache 2.0, AGPL, or proprietary "All rights reserved"? The audit doesn't specify. For a SaaS, MIT is unusual (gives away the code); AGPL is the "SaaS-safe" copyleft. Need a decision before 10.6 ships `LICENSE`.

2. **Welcome email copy** (G-A5): the engine ships in 10.5; the email body copy is user-supplied (or legal-supplied). Does the user have a draft welcome email, or do we ship a placeholder + flag a TODO?

3. **ToS + Privacy Policy copy** (A8): the engine + pages + cookie banner ship in 10.5; the legal COPY must come from a lawyer. Does the user have a legal team / draft text, or do we ship placeholder pages with `<!-- TODO: legal copy -->` markers + flag for legal review?

4. **Cookie consent scope** (G-A3): what counts as "non-essential" cookies? Today there are zero analytics / tracking cookies. The banner can be a no-op until v1.1 adds analytics. Confirm: ship a banner that records consent but has nothing to gate?

5. **GDPR deletion grace period** (10.5): 30 days is the proposal; some SaaS use 14 or 60. Confirm with user.

6. **CSP nonce strategy** (10.3): strict CSP with per-request nonce requires Angular template changes; relaxed CSP with `'unsafe-inline'` is easier but lower security. Confirm trade-off (recommend relaxed CSP + nonce fallback for v1.0; strict CSP in Wave 11+).

7. **Backup off-site target** (10.4): AWS S3, Backblaze B2, DigitalOcean Spaces, or self-hosted MinIO? Each has different cost + IAM complexity. Affects the runbook + the IAM runbook section.

8. **CI runner budget** (10.1): GitHub-hosted paid runners ($0.008/min) vs self-hosted runners (one-time VM cost). Self-hosted is cheaper long-term but adds ops overhead. Recommend GitHub-hosted for v1.0, evaluate self-hosted in Wave 11.

9. **OpenSpec split** (recommendation): 1 change dir vs 2 (10a + 10b). 2 lets parallel workstreams run; 1 keeps the audit-trail atomic. Confirm user preference.

10. **What ships in v1.0.0 vs v1.0.0-rc1?** The proposal assumes rc1 after PR #4 (operations done) and v1.0.0 after PR #6 (compliance + docs done). Confirm this is the user's mental model.

11. **Stripe production key** (A4): does the user have a Stripe account + test/live keys already? If yes, the validation step in 10.6 becomes "fail fast if the live key is missing" rather than "fail fast if the placeholder is present". Need the user's Stripe account status to write the spec precisely.

12. **Email sender (Mail__\*)** (G-A4): does the user have a transactional email provider (Postmark, SendGrid, Mailgun, AWS SES) account, or does Wave 10 ship "any SMTP server" + SPF/DKIM/DMARC docs for whichever they choose?

13. **`size:exception` precedent** — 6 consecutive waves with exception is unusual. The orchestrator should confirm with the user that `feature-branch-chain` + per-slice `size:exception` is acceptable for Wave 10 (vs breaking into 10-12 smaller PRs).

---

## Skill resolution

- **Skill loaded**: `sdd-explore` (loaded via `skill()` tool per task instruction).
- **Skill resolution**: `paths-injected` (the skill's SKILL.md was loaded and its instructions were followed end-to-end).
- **No additional skills required** for the explore phase. The sdd-propose, sdd-spec, sdd-design, sdd-tasks, sdd-apply, sdd-verify phases will each load their own skills in subsequent turns.

## Notes for sdd-propose

The proposal phase should:
1. **Split the change** into 2 proposal.md files: `10a-operations.md` (slices 10.1, 10.2, 10.3, 10.4, 10.6) and `10b-compliance.md` (slice 10.5). Both in the same `openspec/changes/2026-08-18-wave10-v1-readiness/` directory. The explore.md above documents BOTH; the proposals can scope separately.
2. **Reference this explore.md** as the source of truth for the 40+8 gap inventory.
3. **Address all 13 open questions** before writing specs — the proposal cannot fully scope GDPR + legal without legal copy + email provider + Stripe account status.
4. **Inherit the Wave 9 chain** (`feature/0a-identity-model` @ `49207e2`) as the base branch per `feature-branch-chain` strategy.
5. **Apply `size:exception` per slice** (Wave 5/6/7/8/9 precedent), with explicit per-PR justification in each proposal.

The sdd-spec phase should write **8 spec files** (per the table above), one per NEW capability. Each spec uses Given/When/Then scenarios per OpenSpec convention. Strict TDD applies from the spec phase forward.
