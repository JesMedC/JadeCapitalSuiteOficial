# Proposal: Wave 10 — v1 Readiness (6 slices, ~4,350 LOC, 8 NEW specs)

**Change**: `2026-08-18-wave10-v1-readiness`
**Branch**: `feature/0a-identity-model` @ `49207e2` (Wave 9 just archived — `chore(sdd): archive 2026-08-19-wave9-audit-finalization`)
**Stack**: ASP.NET Core 10 / .NET SDK 10.0.400 via `mise exec -- dotnet …`; Angular 19 / standalone + Signals + strict TS
**Persistence**: Postgres 16 + Redis 7 + MinIO (S3-compatible); modular monolith `1.Api/Host` + 5 modules (`Identity`, `Trading`, `Billing`, `Admin`, `PublicPortal`) + 2 Shared layers (`Kernel`, `Infrastructure`)
**Strategy**: `feature-branch-chain` (carries Wave 9's `feature/0a-identity-model`); `size:exception` per slice for 5 of 6 (Wave 5/6/7/8/9 precedent — 10.3 alone fits in 400-line budget)
**Mode**: hybrid (OpenSpec + engram)
**Strict TDD**: active (`openspec/config.yaml` `strict_tdd: true`)
**Baseline**: 1389 BE + 166 FE passing; all 11 prior waves (0–9) archived in `openspec/changes/archive/`
**Release target**: `v1.0.0-rc1` after slice 10.4 merges (operational hardening complete); `v1.0.0` after slice 10.6 merges (compliance + docs complete)

---

## 1. Intent

Wave 10 closes every **Critical (A)** gap from the v1-readiness audit (`openspec/changes/2026-08-18-wave10-v1-readiness/explore.md` §A — items A1–A10) plus a credible subset of **Important (B)** items needed for a launchable v1.0.0-rc1: legal documentation surface (ToS / Privacy / Cookie consent), GDPR Art. 17 right-to-be-forgotten + Art. 20 data portability, CI/CD + dependency scanning, production deployment story, security headers + TLS termination, backup + disaster-recovery scripts, observability-light hooks (Serilog + Sentry), SEO basics, test coverage gate, and 5 new ADRs documenting decisions that grew organically through Waves 6–9.

The user's question "¿qué falta para culminar v1?" resolves to this proposal: **6 chained PRs that ship every blocker for a public launch**, gated by an 800-line/PR review budget with `size:exception` per slice, totaling ~4,350 LOC and 8 NEW OpenSpec specs over a 4–6-week calendar window.

No new modules, no new architectural primitives — every slice touches existing modules (`Host`, `Identity`, `Trading`, `Billing`, `Admin`) plus `docs/` + `openspec/specs/`. Wave 10 is **operational + compliance hardening**, not new product features. Deep observability (OTel / Prometheus), WCAG a11y audit, Playwright E2E in CI, PWA / offline mode, multi-tenant AI rate limits, and broker integrations are explicitly deferred to Wave 11+.

---

## 2. Context

- **Source state**: all 11 waves (0–9) are archived in `openspec/changes/archive/`. The active changes dir is empty as of `49207e2`. The orchestrator has written `state.yaml` for Wave 10 and the `explore.md` (~487 lines, ~54 KB) is already in place.
- **Audit validation**: explore.md §"v1 gaps audit validation" confirms all 10 A-grade items are real gaps (✅ confirmed); 26 of 27 B-grade items are real (B6 partial — integration tests work locally but not in CI). 18 additional gaps (G-A1…G-A6, G-B1…G-B18, G-C1…G-C9) were identified by reading the actual code: 5 are A-grade (genuinely new v1 blockers not in the original audit), 9 are B-grade, 9 are C-grade (post-v1.0).
- **Wave 9 receipts available**: Soft-delete-audit spec covers 19 entities post-Wave 9 (15 from Wave 8 + 4 new + 1 SKIP reconciled — `IAuditEventQueryStore`, `IAdminAuditEndpoints`, `AuditRetentionBackgroundService` all live). Wave 9 hotfix PR #36 remediated 3 CRITICAL bugs found by `sdd-verify`; build is clean, baseline 1389+166 green.
- **Deferred list** (`docs/PROJECT-STATUS.md` §4): 12 items currently deferred. Wave 10 subsumes 5: real broker → v1.1, virus scanner → v1.1, cloud AI → v1.1, SSE → v1.1, multi-tenant AI rate limit → v1.1.
- **OpenSpec convention**: 8 NEW capabilities each get a full `openspec/specs/<slug>/spec.md` (no delta specs this wave — every slice introduces net-new behavior, not requirement edits to existing specs).

---

## 3. Scope (In)

### 3.1 Gaps closed per slice

| Slice | Boundary | Gaps closed |
|---|---|---|
| **10.1 — CI/CD + Dependabot** | `.github/workflows/ci.yml` (4 jobs: lint-backend, test-backend, test-integration, test-frontend) + `.github/workflows/nightly.yml` (OWASP ZAP + npm audit + `dotnet list package --vulnerable`) + `.github/dependabot.yml` + `pull_request_template.md` | **A1** no CI/CD · **A10** no vulnerability scanning · **B6 partial** integration tests in CI |
| **10.2 — Production deployment + secrets** | `Dockerfile.prod` (BE multi-stage, locked-mode restore, non-root, read-only FS, healthcheck) + `Dockerfile.prod` (FE) + `docker-compose.prod.yml` (no Mailpit, restricted Postgres/Redis exposure, edge TLS terminate) + secrets-management runbook (Vault / Doppler / AWS SM / Docker Secrets) | **A2** prod secrets management · **A5** no production deployment story |
| **10.3 — Security headers + TLS** | CSP + HSTS + Permissions-Policy + COEP/COOP/CORP in `nginx.conf` + Caddy `Caddyfile` (auto-TLS via Let's Encrypt) + `scripts/verify-security-headers.sh` | **A3** CSP + HSTS missing · **A7** no TLS termination |
| **10.4 — Backups + migration-order fix** | `scripts/backup-postgres.sh` + `backup-redis.sh` + `backup-minio.sh` + `restore-postgres.sh` + `wal-g` config + Wave 4e.D1 migration-order renumbering (38 files) + `docs/runbooks/backup-recovery.md` + `docs/runbooks/disaster-recovery.md` | **A6** no DB backup story · **B26** migration-order fix · **B16 partial** DR runbooks |
| **10.5 — Legal + GDPR + account lifecycle** | ToS + Privacy Policy pages + Cookie consent banner + `POST /api/auth/consent` + `DELETE /api/users/me` (right-to-be-forgotten with 30-day grace + cascade + BackgroundService hard-delete sweep) + `GET /api/users/me/export` (Art. 20 data portability, JSON) + `IUserCascadeDeletor` interface + welcome email on register + SPF/DKIM/DMARC DNS docs | **A8** ToS + Privacy + Cookie · **A9** GDPR Art. 17 · **G-A1** GDPR Art. 20 (data portability) · **G-A3** cookie consent banner · **G-A4** email deliverability (SPF/DKIM/DMARC docs) · **G-A5** welcome email · **B18** account deletion UI · **B16 partial** GDPR DSAR runbook |
| **10.6 — Docs + observability-light + SEO + coverage + Stripe-verify** | `LICENSE` (Proprietary / Todos los derechos reservados) + `CHANGELOG.md` (Keep-a-Changelog 1.1.0) + `CONTRIBUTING.md` + `SECURITY.md` + `README.es.md` + 5 new ADRs (0005–0009) + coverlet wired + 70% line coverage gate in CI + `sitemap.xml` + `robots.txt` + OG tags in `index.html` + Sentry hooks (BE: `Sentry.AspNetCore` 4.x; FE: `@sentry/angular` 8.x, gated on `Sentry__Dsn` env) + `IValidateOptions<StripeOptions>` startup validation against placeholder | **B5** coverage report · **B9** FE error reporting · **B11** SEO basics · **B13** LICENSE + CHANGELOG + CONTRIBUTING + SECURITY.md · **B14** README.es.md · **B15** ADRs · **B17** Stripe prod key validation (A4 wedge) |

**Cumulative**: **20 gaps closed** = all 10 A-grade (A1–A10) + 8 B-grade (B5, B6, B9, B11, B13, B14, B15, B16, B17, B18) + 5 additional A-grade (G-A1, G-A3, G-A4, G-A5) + B26 migration fix + **A4 (Stripe prod key validation) bundled into 10.6**.

### 3.2 Confirmed decisions (provided by user)

| # | Decision | Implication |
|---|---|---|
| 1 | **LICENSE = Propietaria / Todos los derechos reservados** | 10.6 ships `LICENSE` (English) + `LICENSE.es.md` (Spanish mirror) + a CONTRIBUTING.md that explicitly says the project is NOT open source. No CLA, no public contribution flow. |
| 2 | **GDPR deletion grace period = 30 días** | 10.5 implements `users.scheduled_for_hard_delete_at = UtcNow + 30 days`; `HardDeleteSweepBackgroundService` runs daily. 30 days matches the Stripe billing cycle for refund windows. |
| 3 | **Stripe test-mode only for v1.0.0-rc1** | 10.6 `IValidateOptions<StripeOptions>` rejects the placeholder key in any non-Development environment, but does NOT require live keys at startup. 10.6 ships a smoke-test script (`scripts/stripe-test-smoke.sh`) that exercises the full checkout → webhook → subscription lifecycle against `sk_test_*` keys. Moving to live keys is a deployment-time ops decision (Wave 11+). |

### 3.3 Slice-level deliverables

- **10.1** — 1 NEW spec (`ci-infrastructure`); ~5 yml files + 1 PR template; CI validates lint + BE tests + integration tests + FE tests + FE build on every PR.
- **10.2** — 1 NEW spec (`deployment-automation`); 2 `Dockerfile.prod` (BE + FE) + `docker-compose.prod.yml` + `infrastructure/production/README.md` (secrets runbook).
- **10.3** — 1 NEW spec (`security-headers`); nginx config delta + `infrastructure/caddy/Caddyfile` + 1 verification script.
- **10.4** — 1 NEW spec (`backup-strategy`); 4 shell scripts + wal-g config + 38-file migration renumbering + 2 runbooks.
- **10.5** — 2 NEW specs (`gdpr-compliance` + `account-lifecycle`); 3 new FE pages + 2 new BE endpoints (`DELETE /api/users/me`, `GET /api/users/me/export`) + `POST /api/auth/consent` + welcome email + cascade-delete BackgroundService + GDPR DSAR runbook.
- **10.6** — 1 NEW spec (`production-readiness`); 7 root files (LICENSE, CHANGELOG, CONTRIBUTING, SECURITY.md, README.es.md, sitemap.xml, robots.txt) + 5 ADRs + coverlet gate + OpenAPI export in CI + SEO meta tags + Sentry hooks (BE + FE) + `StripeOptions` validator + Stripe test-mode smoke script.

### 3.4 Cumulative LOC forecast

~4,350 LOC across 6 PRs (per explore.md §"Proposed slice breakdown" + Wave 9 precedent + sdd-tasks forecast methodology from Wave 7 verify-report SUGGESTION #2). Breakdown in §6.

---

## 4. Scope (Out)

### 4.1 Deferred B-grade gaps (Wave 11+ explicitly)

| # | Gap | Why deferred |
|---|---|---|
| **B1** | OpenTelemetry traces + Prometheus metrics + Datadog dashboards | Architectural decision needed (traces vs metrics vs sampling); needs a dedicated design slice. Sentry hooks ship in 10.6 for exception capture only. |
| **B2** | External uptime monitoring (UptimeRobot / Better Stack) | Operational decision (which provider, which regions). `/health/live` + `/health/ready` already exist; wiring them to external monitoring is a 5-minute ops task. |
| **B3** | WCAG a11y audit + axe-core integration | Deferred to Wave 11 (mobile-first design benefits from a dedicated a11y pass; doesn't gate v1.0.0-rc1 on internal-tooling criteria). |
| **B4** | Playwright E2E tests in CI | Depends on CI infra (10.1) existing first; Wave 11 picks it up after v1.0.0-rc1 ships. |
| **B7** | Mutation testing (Stryker.NET) | Code-quality improvement, not a v1.0.0 blocker. Wave 11+. |
| **B8** | Performance / load testing (k6 / Locust) | Depends on prod compose (10.2) existing first; Wave 11+ after first deploy. |
| **B10** | i18n runtime (@angular/localize + JSON message catalogs) | Refactor of the entire FE. Spanish-only is acceptable for an ES market launch. Wave 11+ when we open EN-speaking markets. |
| **B12** | PWA / offline mode | `@angular/service-worker` + ngsw-config.js + service-worker registration. Wave 11+. |
| **B19** | Audit log CSV/JSON export endpoint for admins | 10.5 ships the user-facing `GET /api/users/me/export` (Art. 20); admin audit export builds on top of the Wave 9 `IAuditEventQueryStore` and is Wave 11+. |
| **B20** | Multi-tenant AI rate limits (per-tenant quota on `/api/ai/*`) | Depends on `ITenantContext` quota policies (Wave 6 6c.2 + Wave 7); needs design decision on enforcement point. Wave 11+. |
| **B21** | Real-time alerts via SignalR | Deferred since Wave 7 (`openspec/specs/realtime/spec.md` quotes-only). Wave 11+. |
| **B22** | Google Calendar OAuth integration | Internal `/app/calendar` ships. OAuth is a Wave 11+ integration. |
| **B23** | Cloud AI providers (OpenAI / Anthropic SDKs) | Only Ollama is wired (`Program.cs:116-129`); provider abstraction is Wave 7+. |
| **B24** | Real broker integration (IBKR / MT5 native APIs) | CSV/MT4 importers ship; broker SDKs are Wave 11+ (per `PROJECT-STATUS.md:142`). |
| **B25** | Real virus scanner (ClamAV) | Deferred. |
| **B27** | Streaming tokens via SSE | Deferred since Wave 7. |
| **B28** | AI signal generation from scanner | Cross-Wave with B23. Wave 11+. |
| **B29** | Image optimization + preload | Wave 11+. |
| **B30** | CDN (Cloudflare / Fastly) | Wave 11+. |

### 4.2 Deferred additional gaps (Wave 11+)

| ID | Title | Notes |
|---|---|---|
| **G-A2** | Audit log tamper-evidence (hash chain) | SOC 2 / ISO 27001 prerequisite. v1.0 documents the trust model (DB-level role separation; row-level signing deferred). Wave 11+ with cryptographic chain. |
| **G-B1** | Feature flags / kill switches | Architectural decision needed. Wave 11+. |
| **G-B3** | API versioning strategy (`/api/v1/...` + sunset headers) | Breaking change risk for v1.0 → v1.1. Defer to v1.1. |
| **G-B4** | FE global error boundary | Angular `ErrorHandler` provider + white-screen replacement. Wave 11+. |
| **G-B5** | DB connection pooling tuning (Maximum Pool Size, Connection Idle Lifetime) | Needs load-test data first (B8 dependency). Wave 11+. |
| **G-B6** | ORM N+1 detection middleware | Code-review-driven for v1.0. Wave 11+. |
| **G-B7** + **G-B8** | Redis + MinIO backup automation (cron + off-site) | 10.4 ships the scripts; full cron automation is Wave 11+. |
| **G-B9** | Customer support channel (Intercom widget, `/help` page beyond FAQ) | Operational decision (which provider). Out of scope for engineering wave. |
| **G-B10** | Pricing tiers → real Stripe Price IDs | Needs a real Stripe account (test mode post-10.6, live mode post-v1.1). |
| **G-B11** | Audit log backup off-site | `audit.events` is in the same Postgres volume as the user data; 10.4's `backup-postgres.sh` covers it. Wave 11+ for off-site. |
| **G-B12** | Outbound webhook signing | No outbound webhooks exist today. Defer until a feature requires them. |
| **G-B13** | FE bundle size budget enforcement gate in CI | 10.1's CI runs the build (warnings surface); enforcement gate (build failure on > 1MB) is Wave 11+. |
| **G-B14** | Contract testing (Pact) | Drift risk; manual OpenAPI snapshot is sufficient for v1.0.0-rc1. Wave 11+. |
| **G-B15** | Visual regression testing | CI infra (10.1) doesn't have a headless browser yet. Wave 11+. |
| **G-B16** | Penetration testing history | External engagement (OWASP ZAP nightly in 10.1 is the baseline; full pentest is Wave 11+). |
| **G-B17** | Admin role hierarchy (super-admin vs tenant-admin) | RBAC refactor; current `ITenantContext.IsSuperAdmin` covers all v1.0 ops. Wave 11+. |
| **G-B18** | (already confirmed — NOT a gap) `IStripeWebhookEventRepository` idempotency table ships from Wave 6. ✅ | No action. |

### 4.3 Out of scope explicitly (post-v1.0)

All C-tier items: G-C1 multi-region / DR failover, G-C2 chaos engineering, G-C3 SOC 2 / ISO 27001 certification, G-C4 mTLS between services, G-C5 DNS CAA / DNSSEC, G-C6 encryption at rest (Postgres TDE), G-C7 audit log partitioning by month, G-C8 AI signal generation, G-C9 mobile native (iOS / Android).

All C-tier audit items: mobile native, copy trading, dark mode toggle, social auth, WhatsApp/Telegram bot, copy-trader marketplace, native desktop app, Slack integration, Discord bot, etc. — out of scope by definition (they're explicitly post-v1.0 in the original audit).

---

## 5. Approach

### 5.1 Per-slice approach (1–3 sentences each)

- **10.1 — CI/CD**: Author `.github/workflows/ci.yml` mirroring `dotnet test --nologo --verbosity minimal` from `openspec/config.yaml:35` (Wave 6/7/8/9 precedent). 4 parallel jobs (`lint-backend`, `test-backend`, `test-integration` with `services: postgres, redis` for Testcontainers, `test-frontend`). Dependabot config uses the GitHub-native week-grouped + auto-merge-for-patches pattern. Nightly OWASP ZAP + `dotnet list package --vulnerable` runs on a cron + workflow_run trigger so the merge-gate stays fast (<10 min target).
- **10.2 — Production deploy**: Split dev (existing `docker-compose.yml`) from prod (new `docker-compose.prod.yml`) via Compose profiles. `Dockerfile.prod` (BE) is a 4-stage build: `restore --locked-mode` → `publish /p:PublishTrimmed=false /p:SelfContained=false` → `runtime mcr.microsoft.com/dotnet/aspnet:10.0` (non-root) → final scratch with healthcheck. `Dockerfile.prod` (FE) builds `ng build --configuration production` then serves over nginx. Secrets managed via Docker Secrets (default), with a vendor-neutral runbook documenting Vault / Doppler / AWS Secrets Manager migration paths.
- **10.3 — Security headers**: CSP ships in **relaxed mode** for v1.0.0-rc1 (`default-src 'self'; script-src 'self' 'nonce-{per-request}'; style-src 'self' 'unsafe-inline'; ...`) — strict nonce-only is Wave 11+. HSTS ships preloaded (`Strict-Transport-Security: max-age=63072000; includeSubDomains; preload`). COEP / COOP / CORP set to `same-origin`. Caddy auto-TLS via Let's Encrypt DNS-01 challenge.
- **10.4 — Backups + migration-order fix**: `pg_dump` daily + `wal-g` hourly WAL archiving for PITR (RPO ≤ 1h). Redis `BGSAVE` hourly + cron copy. MinIO `mc mirror --remove` to a backup bucket nightly. 38-file migration renumbering uses `git mv` + a verifier script that runs Testcontainers `docker-entrypoint-initdb.d` against a fresh DB. RTO ≤ 4h documented in `disaster-recovery.md`.
- **10.5 — Legal + GDPR + account lifecycle**: Three-layer GDPR surface: (a) **legal pages** (placeholder copy with `<!-- TODO: legal copy -->` markers + i18n-ready structure; user/legal team supplies copy at deploy time); (b) **Right-to-Be-Forgotten**: `DELETE /api/users/me` → anonymize email/displayname + soft-delete cascade across 17 user-owned aggregates via `IUserCascadeDeletor` registered per module → schedule hard-delete after 30-day grace → `HardDeleteSweepBackgroundService` runs daily → emit an `AuditAction.Deleted` event (special carve-out from `UserAuditDecorator`); (c) **Data Portability**: `GET /api/users/me/export` streams a JSON archive of all user-owned entities (excludes `audit.events` per compliance trail + `stripe_webhook_events`). Welcome email on first register is idempotent (7-day suppression). SPF/DKIM/DMARC DNS records documented in `setup-email-deliverability.md`.
- **10.6 — Docs + observability + SEO + coverage + Stripe-verify**: `LICENSE` (proprietary, Spanish mirror), 5 new ADRs (0005 multi-tenant / 0006 audit decorator pattern / 0007 Stripe gateway abstraction / 0008 audit retention / 0009 GDPR right-to-be-forgotten), coverlet wired to all test csprojs with `--collect:"XPlat Code Coverage"` + 70% line coverage gate (incremented in 5% steps if current baseline < 70%). Sentry SDKs gate on `Sentry__Dsn`/`Sentry__DsnFrontend` env presence (skipped when absent — dev / sandbox). `IValidateOptions<StripeOptions>` fails startup on `ApiKey.StartsWith("sk_test_local_dev_placeholder")` OR `ApiKey.IsNullOrEmpty()` OR `WebhookSecret.IsNullOrEmpty()`, with `ValidateOnStart` (Wave 6 6c.2 `JwtOptions` precedent). Test-mode smoke script exercises Stripe Checkout → webhook → subscription create/cancel flow against `sk_test_*` keys.

### 5.2 Cross-cutting conventions

- **Strict TDD**: every slice follows RED → GREEN → REFACTOR; RED integration tests written against `IAuditLogger`/`IFeatureFlag`/etc. fakes (Wave 6/7/8/9 pattern).
- **Scrutor 4.2.2** continues to be the decorator registration mechanism where applicable (10.1 + 10.5 don't need it; the other slices don't either).
- **`size:exception` per slice**: 5 of 6 slices exceed the 400-line PR review budget. Per Wave 5/6/7/8/9 precedent (6 consecutive waves with exception is unusual — flag for `branch-pr` skill review). 10.3 alone (~150 LOC) is within budget.
- **Test fixture pattern**: SQLite-in-memory for unit tests, Testcontainers Postgres for integration tests (10.5's `DELETE /api/users/me` cascade needs the real transaction semantics).
- **No new module**: Wave 10 does not introduce `src/2.Modules/Legal/` or `src/2.Modules/Backup/`. GDPR + cascade-delete code lives in `Identity.Application` + `Identity.Infrastructure` (the canonical "user lifecycle" home); backup scripts live in `scripts/` + `infrastructure/`.

---

## 6. Slice Summary Table (Chained PR Strategy)

| PR # | Slice | Branch (proposed) | Title | Target LOC | Paths | Tests added | size:exception | Reviewer budget risk |
|---|---|---|---|---:|---:|---:|---|---|
| **#37** | **10.1** | `feature/wave10-ci-cd` | Wave 10.1 — CI/CD pipeline + Dependabot | ~800 | ~12 | ~5 (lint, dep-scan, dry-run, PR-template, workflow syntax) | **yes** (CI infra precedent Wave 5/6/8) | Medium |
| **#38** | **10.2** | `feature/wave10-deployment` | Wave 10.2 — Production deployment story + secrets management | ~800 | ~14 | ~4 (prod compose validate, Dockerfile hardened, secrets runbook parse, healthcheck passes) | **yes** | Medium |
| **#39** | **10.3** | `feature/wave10-security-headers` | Wave 10.3 — Security headers + TLS termination | ~150 | ~5 | ~3 (CSP nonce rotates, HSTS preload set, security-headers verify script green) | **no** | Low |
| **#40** | **10.4** | `feature/wave10-backups` | Wave 10.4 — Backup strategy + migration order fix | ~600 | ~12 | ~5 (PG backup/restore, Redis BGSAVE, MinIO mirror, migration rename idempotent, RPO/RTO doc) | **yes** | Medium |
| **#41** | **10.5** | `feature/wave10-gdpr` | Wave 10.5 — Legal docs + GDPR Art. 17 + Art. 20 + account deletion | ~1,200 | ~22 | ~10 (DELETE cascade, export streaming, grace period, welcome email idempotent, consent POST, cookie banner show/hide, 30-day sweep) | **yes** (heaviest slice; multi-module cascade) | **High** |
| **#42** | **10.6** | `feature/wave10-docs-observability` | Wave 10.6 — Docs + observability-light + SEO + coverage + Stripe-verify | ~800 | ~20 | ~6 (LICENSE parse, coverage gate enforce, sitemap served, OG meta tag rendered, Sentry guard skip-if-no-DSN, Stripe validator fails-fast) | **yes** | Medium |
| **Total** | **6 chained PRs** | | | **~4,350** | **~85** | **~33** | **5 of 6** | |

**Critical path**: 10.1 → 10.5 → 10.6 (3 PRs sequential). 10.2, 10.3, 10.4 can run in parallel feature branches off 10.1's HEAD if team capacity allows (`feature/wave10-deployment`, `feature/wave10-security-headers`, `feature/wave10-backups` all branch from `feature/wave10-ci-cd`).

**Chain integrity**: PR #37 (10.1) targets `feature/0a-identity-model`; PR #38 targets `feature/wave10-ci-cd`; ... ; PR #42 (10.6) targets `feature/wave10-gdpr`. No PR targets `main` directly.

**`size:exception` justification**: 6 consecutive waves (5/6/7/8/9/10) have required exceptions. The `branch-pr` skill should be consulted at PR #37 / #41 to confirm the reviewer can absorb this chain. Mitigation: merge slice-by-slice (no stacking); each slice ships behind its own PR review surface.

---

## 7. Architectural Decisions

### 7.1 Confirmed by user (3)

| # | Decision | Rationale |
|---|---|---|
| 1 | **LICENSE = Proprietary / "Todos los derechos reservados"** | JadeCapital Suite is commercial SaaS; no public contribution flow; user/legal team owns the Spanish mirror copy. CONTRIBUTING.md will explicitly state this repo is NOT open source and external PRs are not accepted. CLA is not required at this stage (small team). |
| 2 | **GDPR deletion grace period = 30 days** | Matches the monthly Stripe billing cycle (refund window). Long enough for "oops" recovery but short enough to comply with GDPR Art. 17's "without undue delay" clause. If the user requests account restoration within 30 days, the data is still soft-deleted (recoverable by re-registering with the same email + manual ops intervention). |
| 3 | **Stripe = test-mode only for v1.0.0-rc1** | No live keys are required for the release candidate. `IValidateOptions<StripeOptions>` fails-fast on the placeholder OR empty keys, and a smoke script (`scripts/stripe-test-smoke.sh`) exercises the full lifecycle. Live keys are a deployment-time ops decision: when the ops team configures `sk_live_*` + `whsec_*` env vars and removes `ValidateOnStart`-dev bypass, the validator's check moves from "fail on placeholder" to "fail on missing live key" without code change. |

### 7.2 Assumed (10 defaulted by explore.md + this proposal — reviewable in spec phase)

| # | Decision | Default | Rationale | Override path |
|---|---|---|---|---|
| 4 | **CSP nonce strategy** | **Relaxed CSP** for v1.0.0 (`default-src 'self'; script-src 'self' 'nonce-{per-request}'; style-src 'self' 'unsafe-inline'`). Strict nonce-only (no `'unsafe-inline'` even for styles) is Wave 11+. | Angular injects `<script>` tags at build time; nonce-only CSP requires Angular template + build orchestration changes. Relaxed CSP + per-request nonce is the dominant pattern in similar ASP.NET Core + Angular Stack Overflow snippets; achieves most of the security benefit (blocks injected scripts, allows own scripts) at trivial implementation cost. | User override: "do strict CSP in 10.3" → 10.3 budget grows from ~150 LOC to ~400 LOC. |
| 5 | **Backup off-site target** | **Self-hosted MinIO** (existing docker volume `minio-data`) for v1.0.0. The off-site "backup bucket" is a second MinIO container in a separate stack / DR site. AWS S3 / Backblaze B2 are v1.1+ candidates. | No AWS dependency, no IAM provisioning at launch time. The mc mirror command works identically against any S3-compatible target. | User override: "AWS S3 in 10.4" → runbook section grows by 1 page; IAM credentials management is the ops team's call. |
| 6 | **CI runner budget** | **GitHub-hosted free tier (2000 min/month)** for v1.0.0. 5 PRs/week × ~10 min CI + 1 nightly × ~30 min × 30 days = ~1400 min — within budget. Self-hosted runners when budget exceeds. | Existing dev pattern is GitHub Actions + ubuntu-latest. Sandbox constraint prefers not introducing a new runner registry. | User override: "self-hosted runner from day 1" → adds ops overhead to 10.1 setup. |
| 7 | **OpenSpec split** | **Single `proposal.md`** for Wave 10 (not split). 8 NEW specs land in `openspec/specs/<slug>/spec.md` (full specs, not deltas). | Explore.md initially suggested 2 (10a operations + 10b compliance). User decided to keep Wave 10 atomic for OpenSpec audit trail. | N/A — user already chose. |
| 8 | **v1.0.0 vs `v1.0.0-rc1`** | **`v1.0.0-rc1` after Wave 10 merge** (after PR #42). `v1.0.0` after ops team first deploy + smoke pass. | Tags the operational + compliance completeness milestone WITHOUT committing to a deploy-day. Follows Keep-a-Changelog 1.1.0 + SemVer pre-release convention. | User override: "ship v1.0.0 directly" → skip the rc1 tag. |
| 9 | **Email provider** | **Mailpit (dev) + Mailgun/SES (prod)**. `Mail__Host`/`Mail__Port` env vars remain the contract; the deployment runbook documents Mailgun / Amazon SES env-var mappings for the ops team. | Mailpit is already wired in `docker-compose.yml` (Wave 4). Changing the prod provider is a config swap, not a code change. | User override: "lock to Mailgun" → add `MailgunOptions` POCO + a Mailgun-specific message template; postpone Wave 11. |
| 10 | **`size:exception` per slice** | Accept for 5 of 6 slices (10.1, 10.2, 10.4, 10.5, 10.6) per Wave 5/6/7/8/9 precedent. Only 10.3 (~150 LOC) is within the 400-line PR budget. | 6 consecutive waves with exception is unusual but the operational/compliance nature of Wave 10 genuinely requires the breadth. Alternative — splitting 10.5 alone into 3 smaller PRs — would delay the rc1 tag by ~1 week per sub-PR. | User override: "split 10.5 into 2 PRs" → adds 1 chain link. |
| 11 | **GDPR preflight (within 10.5)** | 30-day grace period (confirmed); soft-delete cascade across 3 modules (Identity, Trading, Billing) via `IUserCascadeDeletor`; hard-delete sweep via `HardDeleteSweepBackgroundService` (daily tick, idempotent). User data export endpoint excludes `audit.events` (compliance trail per Wave 9 spec) + `stripe_webhook_events` (system). | GDPR Art. 17 + Art. 20 + Art. 15 (right of access) addressed by `GET /api/users/me/export`. Cookie consent banner accepts "all" or "essential-only"; no analytic cookies ship in v1.0 → both choices are functionally identical until Wave 11 adds tracking. | User override: "60-day grace" → trivial code change. |
| 12 | **Audit log hash chain** | **OUT of scope for Wave 10** (G-A2). Wave 10 documents the trust model (DB-level role separation; append-only via no UPDATE/DELETE permissions granted to the app's DB role) in an ADR + the `audit-retention-policy` spec. The cryptographic hash chain (each row embeds `prev_hash` of the previous row's `(EntityId + OccurredAt + Changes)`) is Wave 11+. | Implementing tamper-evidence correctly requires a design decision on hash algorithm (SHA-256 vs SHA-3 vs Merkle DAG), signature scheme (HMAC vs asymmetric), and key rotation; not appropriate to bundle into a v1.0.0-rc1 release. | User override: "add hash chain in 10.6" → 10.6 budget grows by ~400 LOC, may need a dedicated 10.7 slice. |
| 13 | **CSP nonce vs relaxed** | Same as #4 (CSP nonce strategy row). One decision, not two. | The two open questions in explore.md are a duplicate; folded here for clarity. | N/A — fold-in. |

---

## 8. Dependencies on Wave 9

Wave 10 builds directly on Wave 9's outputs. The proposal should be blocked if any of these Wave 9 receipts regress:

| Wave 9 artifact | Wave 10 dependency |
|---|---|
| `audit.events` table + 19-entity decorator coverage (Wave 6/7/8/9) | All 10.5 cascade-delete flows emit `AuditAction.Deleted` for the user + `AuditAction.Updated` (anonymized) for each cascaded soft-delete. |
| `IAuditEventQueryStore` (Wave 9 9b.1, `src/2.Modules/Admin/JadeCapital.Admin.Infrastructure/Persistence/AuditEventQueryStore.cs`) | NOT modified in Wave 10; remains the read surface. The user-facing export endpoint in 10.5 reads raw row data (NOT through this query store). |
| `AuditRetentionBackgroundService` (Wave 9 9b.1) | NOT modified in Wave 10; Wave 9's 90-day retention continues. The 30-day user-deletion hard-delete sweep in 10.5 is a SEPARATE BackgroundService with a different schedule (daily + grace-period based, not bulk-by-date). |
| `AuditAction.Deleted` enum value (reserved in Wave 6, used by Wave 9's `ImportJob` cascade) | 10.5's `DeleteUserHandler` reuses this value for the user's own soft-delete; the cascade emits `AuditAction.Updated` for the anonymization step on dependent aggregates. |
| `DecoratedRepository<T>` Scrutor helper (Wave 7 7a.0) | NOT modified; Wave 10 doesn't add new decorators. |
| `Testcontainers.PostgreSql` + `WebApplicationFactory` test fixture pattern (Wave 6 6f precedent) | 10.5's `DELETE /api/users/me` + cascade integration tests reuse this pattern (need real Postgres transaction semantics for cascade). Sandbox carry-forward WARNING: tests run via per-project test runs in this sandbox (Wave 5/6/7/8/9 precedent). |
| `soft-delete-audit` spec (Wave 6/7/8/9, `openspec/specs/soft-delete-audit/spec.md`) | 10.5 will reference the cascade pattern from this spec (`ImportJob` precedent) but does NOT modify the spec — GDPR cascade is a separate concern documented in the new `account-lifecycle` spec. |
| `AdminModuleRegistration` (NEW in Wave 9 9b.1) | NOT modified in Wave 10. The GDPR deletion + data-export endpoints live in `Identity.Application` (consistent with `IUserRepository` ownership) — they do NOT cross into the Admin module. |
| `Wave 9 hotfix PR #36` (3 CRITICAL bugs remediated: AdminAuditEndpoints wiring, query store auth header, retention idempotency) | Wave 10 carries forward the lessons (Testcontainers Postgres for admin endpoints, `RequireAuthorization("AdminOnly")` verified, BackgroundService isolation). |

**No Wave 9 artifact is replaced** by Wave 10. Wave 10 is purely additive (operational + compliance).

---

## 9. Chained PR Delivery Plan

| # | Branch | Base | Title | LOC | Rollback |
|---|---|---|---|---:|---|
| **#37** | `feature/wave10-ci-cd` | `feature/0a-identity-model` | Wave 10.1 — CI/CD pipeline + Dependabot | ~800 | `git revert` the slice. CI disabled. Devs run `dotnet test` locally. No production impact. |
| **#38** | `feature/wave10-deployment` | `feature/wave10-ci-cd` | Wave 10.2 — Production deployment story + secrets management | ~800 | `git revert` the slice. Prod compose + Dockerfile reverted. Dev workflow unchanged. |
| **#39** | `feature/wave10-security-headers` | `feature/wave10-deployment` | Wave 10.3 — Security headers + TLS termination | ~150 | `git revert` the slice. nginx.conf reverts to no CSP/HSTS (security posture regresses — CRITICAL NOT TO MERGE on a Friday). |
| **#40** | `feature/wave10-backups` | `feature/wave10-security-headers` | Wave 10.4 — Backup strategy + migration order fix | ~600 | `git revert` the slice. Migration filenames revert to original (idempotent verifier script can re-run). Backup cron removed. |
| **#41** | `feature/wave10-gdpr` | `feature/wave10-backups` | Wave 10.5 — Legal docs + GDPR + account deletion | ~1,200 | `git revert` the slice. `DELETE /api/users/me` + `GET /api/users/me/export` endpoints unmapped. Cascade logic reverted. GDPR compliance removed (flag for incident — do NOT merge prematurely). |
| **#42** | `feature/wave10-docs-observability` | `feature/wave10-gdpr` | Wave 10.6 — Docs + observability-light + SEO + coverage + Stripe-verify | ~800 | `git revert` the slice. LICENSE/CHANGELOG/etc. deleted. Sentry hooks removed. Coverlet gate removed. Stripe validator reverted to no-op. |

**`v1.0.0-rc1` tag**: lands after PR #40 (10.4) merges to `feature/0a-identity-model`. Operational hardening complete.

**`v1.0.0` tag**: lands after PR #42 (10.6) merges AND the ops team's first successful deployment + smoke-test pass.

**Pairing**: 10.5 is the largest slice (heavy), so PR #41 should not be merged at end-of-week (no rollback on a Friday). Other slices are reviewable on any day.

---

## 10. Risks

| # | Risk | Likelihood | Mitigation |
|---|---|---|---|
| 1 | **10.5 GDPR cascade-deleter blast radius** — 17 user-owned aggregates across 3 modules (Identity, Trading, Billing). A bug in `IUserCascadeDeletor` registration = user-visible data leak post-deletion. | Med | (a) Per-module integration test: register user → open trade → create journal → set strategy → DELETE → assert all rows have `IsDeleted=true` (with `IgnoreQueryFilters()`); (b) `IAuditLogger` emits 1 audit row per cascaded soft-delete so any missed registration is observable in `audit.events`; (c) the 30-day grace period is the recovery safety net. |
| 2 | **10.2 secrets management requires ops team buy-in** — Docker Secrets (default) vs Vault vs Doppler vs AWS Secrets Manager. The choice changes the runbook + the env-var injection pattern. | Low | Slice ships defaults + a vendor-neutral runbook; ops team picks at deploy time. Code does not hard-code a provider. |
| 3 | **10.4 migration-order fix touches 38 migrations** — renumbering is high-risk; one missed reference breaks the migrate service. | Med | (a) Use `git mv` with the renumbering mapper; (b) verifier script runs Testcontainers `docker-entrypoint-initdb.d` against a fresh DB; (c) `migrate.Dockerfile` is rewritten to be order-agnostic (`ls *.sql | sort` + dependency array); (d) Wave 5/6/7/8/9 precedent: migration tests via `psql -f` against a Testcontainers Postgres; (e) the Wave 4e.D1 fix has been deferred since Wave 4 — well-understood risk. |
| 4 | **10.6 LICENSE + ADRs + runbooks are docs, not directly TDD-testable** — strict TDD expects test-first; pure Markdown files are not testable. | Low | (a) Each doc has a corresponding validation script (LICENSE parse-check, runbook link-check, ADR frontmatter check); (b) CI runs these validations as part of the `lint-backend` job; (c) the `production-readiness` spec's Given/When/Then scenarios pin the existence of each doc as a runtime check (smoke test that loads /assets/LICENSE and asserts content). |
| 5 | **Wave 10 cumulative LOC ~4,350 — large change, 4–6 weeks** — calendar slippage if 10.5 hits reviewer-cycle delays. | Med | (a) Slice-by-slice delivery (not a single mega-PR); (b) PR #41 (10.5) has its own merged gate, not batched with 10.6; (c) `v1.0.0-rc1` tag is achievable after PR #40 even if 10.5/10.6 slip. |
| 6 | **`size:exception` precedent erosion** — 6 consecutive waves with exception (5/6/7/8/9/10). | Low | (a) Each slice explicitly justifies its exception in its PR description (per Wave 5/6/7/8/9 precedent); (b) `branch-pr` skill is consulted at PR #37 and PR #41; (c) 10.3's 400-line-budget compliance is the counter-example — keeps the discipline visible. |
| 7 | **CSP nonce propagation with Angular SSR** — per-request nonce requires passing the nonce to Angular's `main.ts` bootstrap via meta tag → template; if Angular lazy-loaded chunks escape the nonce, scripts are blocked. | Low | (a) Ship relaxed CSP (Decision #4); (b) verify with `scripts/verify-security-headers.sh` against an actual `ng build` + nginx; (c) Wave 11 hardens to strict CSP. |
| 8 | **GDPR right-to-be-forgotten + email provider (Mailgun / SES) timing** — the welcome email ships from Mail__ settings; SPF/DKIM/DMARC DNS records may not be set when v1.0.0-rc1 deploys. | Low | (a) 10.5 ships a `setup-email-deliverability.md` runbook that lists the exact DNS records; (b) welcome email is idempotent (7-day suppression) so re-sends don't double-spam; (c) email delivery failure is logged + does not block account registration. |
| 9 | **CI runner quota exhaustion at launch ramp** — 5 PRs/week × ~10 min + nightly × ~30 min = ~1400 min/month. Free tier = 2000 min. Headroom is 30%. | Low | (a) NuGet + npm cache keys hash to `Directory.Build.props` + `package-lock.json` (cache hit rate > 80%); (b) nightly runs on schedule, not on every push (saves 30× concurrency); (c) self-hosted runner migration is a Wave 11+ decision. |
| 10 | **Legal copy placeholder risk** — ToS / Privacy Policy pages ship with `<!-- TODO: legal copy -->` markers. A v1.0.0-rc1 launch with placeholder legal pages is a compliance risk in EU. | Med | (a) Flag in CONTRIBUTING.md + the changelog: "**LEGAL COPY PLACEHOLDER — DO NOT DEPLOY TO PRODUCTION WITHOUT LEGAL REVIEW**"; (b) the slice includes a checklist that the user must sign off before tagging v1.0.0; (c) `cookie-consent.page.ts` + `terms.page.ts` + `privacy.page.ts` all carry the placeholder banner until legal copy lands. |
| 11 | **Stripe prod key validation** blocks local dev if not properly bypassed — `IValidateOptions<StripeOptions>` could fail `ASPNETCORE_ENVIRONMENT=Development` if not gated. | Low | (a) Validator gates on `env.IsProduction() || env.IsStaging()` — Development skips validation; (b) smoke script uses `sk_test_*` keys; (c) `appsettings.Development.json` keeps the placeholder key, which is fine in Development. |
| 12 | **License + ADRs + runbooks in Spanish mirror drift** — `LICENSE.es.md`, `README.es.md`, etc. must stay in sync with English originals. | Med | (a) CI step `scripts/check-docs-sync.sh` validates lengths + headers + key phrases (no full translation); (b) both English and Spanish mirrors are committed in the same PR; (c) ADR numbering must match across languages (English + Spanish ADR 0005 are the same doc, translated). |
| 13 | **`HardDeleteSweepBackgroundService` runs against soft-deleted users** if scheduled correctly — a clock-skew bug could hard-delete a user before the 30-day grace expires. | Low | (a) `scheduled_for_hard_delete_at <= UtcNow` is the guard (not `IsDeleted && created_at < cutoff`); (b) the BackgroundService tests cover clock-skew (Wave 6 6d.2 `FakeClock` precedent); (c) AuditAction.Deleted emitted on hard-delete. |

---

## 11. Affected Areas (summary; per-slice detail in sdd-tasks)

| Area | Impact | Description |
|------|--------|-------------|
| `.github/workflows/ci.yml` + `.github/workflows/nightly.yml` + `.github/dependabot.yml` + `.github/pull_request_template.md` | **New** | CI infra (10.1) |
| `backend/Dockerfile.prod` + `frontend/Dockerfile.prod` + `docker-compose.prod.yml` + `infrastructure/production/README.md` | **New / Modified** | Prod deployment (10.2) |
| `infrastructure/nginx/nginx.conf` + `frontend/nginx.conf` + `infrastructure/caddy/Caddyfile` + `scripts/verify-security-headers.sh` + `scripts/setup-tls.sh` | **Modified** | Security headers + TLS (10.3) |
| `scripts/backup-postgres.sh` + `scripts/backup-redis.sh` + `scripts/backup-minio.sh` + `scripts/restore-postgres.sh` + `infrastructure/wal-g/config.yaml` + `infrastructure/postgres/migrations/0001_*..0038_*` (renumbered) | **New / Modified (high-risk)** | Backups + migration order (10.4) |
| `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Users/DeleteUser/DeleteUserHandler.cs` + `GetUserExport/GetUserExportHandler.cs` + `Consent/ConsentHandler.cs` + `IUserCascadeDeletor.cs` + `HardDeleteSweepBackgroundService.cs` + `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/DependencyInjection/IdentityModuleRegistration.cs` (extended) + `src/2.Modules/Trading/.../CascadeDeleters/ICascadeDeletor<Trade>.cs` (5+ typed) + `src/2.Modules/Billing/.../CascadeDeleters/...` (1 typed) | **New** | GDPR + cascade (10.5) |
| `frontend/src/app/features/public/legal/{terms,privacy,cookie-consent}.page.ts` + `frontend/src/app/features/trader/settings/settings.page.ts` (delete-account section) + `frontend/src/app/core/services/cookie-consent.service.ts` | **New / Modified** | Legal FE pages (10.5) |
| `LICENSE` + `LICENSE.es.md` + `CHANGELOG.md` + `CONTRIBUTING.md` + `SECURITY.md` + `README.es.md` + `frontend/src/index.html` (OG tags + sitemap link) + `frontend/src/assets/robots.txt` + `appsettings.Production.json` (StripeOptions validator) + `src/.../Host/JadeCapital.Host/Program.cs` (StripeOptions `ValidateOnStart`) + `Directory.Build.props` (coverlet) + `tests/**/*.csproj` (coverlet.collector) + `docs/adr/0005-..0009-*.md` + `docs/runbooks/{deploy,backup-recovery,disaster-recovery,gdpr-data-subject-request,setup-email-deliverability}.md` | **New / Modified** | Docs + ops + observability + SEO + coverage (10.6) |
| `openspec/specs/{ci-infrastructure,deployment-automation,security-headers,backup-strategy,gdpr-compliance,account-lifecycle,observability-light,production-readiness}/spec.md` | **New** | 8 NEW specs (sdd-spec phase) |

---

## 12. Migration Path

**One schema migration needed** (10.4's migration-order fix renumbers 38 files without changing semantics, plus 10.5's `users` table gains 3 columns, and 10.6 doesn't add new tables):

- 10.5: `users.scheduled_for_hard_delete_at TIMESTAMPTZ NULL` + `users.cookie_consent_accepted_at TIMESTAMPTZ NULL` + `users.consent_ip INET NULL` (3 nullable columns; idempotent; no data backfill — existing users get NULL + the next login captures consent).

Plus 10.4 renumbering + 10.2 seed-only changes (no DB schema, just file paths).

All migrations land through the standard EF Core mechanism + `docker-entrypoint-initdb.d/*.sql` (verified via Testcontainers).

**No data migration** is needed. No backfill of historical users for GDPR (existing data is forward-only compliant under the new policy — right-to-be-forgotten only fires on new DELETE requests).

**`audit.events` schema is NOT modified.** The `AuditAction.Deleted` enum value (reserved since Wave 6, used by Wave 9's `ImportJob` cascade) is reused for the user-deletion event. No new enum value.

---

## 13. Definition of Done

- [ ] **PR chain intact**: 6 PRs (#37–#42) merged in order. Each PR targets the previous PR's branch. No PR targets `main` directly.
- [ ] **All gaps closed**: every gap in §3.1 (20 gaps: A1–A10 + B5, B6 partial, B9, B11, B13, B14, B15, B16 partial, B17, B18, B26 + G-A1, G-A3, G-A4, G-A5) marked `[x]` in the change's `tasks.md` with a verification command.
- [ ] **8 NEW specs merged into `openspec/specs/`**: `ci-infrastructure`, `deployment-automation`, `security-headers`, `backup-strategy`, `gdpr-compliance`, `account-lifecycle`, `observability-light`, `production-readiness` — each with full Given/When/Then scenarios.
- [ ] **Build green**: `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 NEW warnings (vs Wave 9 baseline of 3 pre-existing CA2263).
- [ ] **Tests cumulative**: 1389 BE + 166 FE baseline + ~33 new BE + ~0 new FE (FE tests land in slice 10.5 for cookie consent banner; no FE test infra exists today; jest scaffold per `openspec/config.yaml:65-67`). Zero regressions across the entire BE suite.
- [ ] **Coverage gate**: coverlet wired + line coverage ≥ 70% enforced in CI. If baseline < 70%, raise the gate in 5% increments over Wave 10 (track in CHANGELOG).
- [ ] **GDPR cascade integration test**: register user → open trade → create journal → DELETE → assert all 17 user-owned aggregates have `IsDeleted=true` (with `IgnoreQueryFilters()`); assert `audit.events` contains 1 row per cascaded soft-delete + 1 `User/Deleted` row.
- [ ] **Migration-order verifier**: `scripts/verify-migration-order.sh` runs against a fresh Testcontainers Postgres + asserts all 38 migrations apply in dependency order.
- [ ] **CI green**: `.github/workflows/ci.yml` runs `lint-backend` + `test-backend` + `test-integration` + `test-frontend` on a sample PR; all 4 jobs pass.
- [ ] **Security headers present**: `scripts/verify-security-headers.sh` confirms CSP + HSTS + Permissions-Policy + COEP + COOP + CORP are set in production nginx.conf.
- [ ] **TLS auto-renewal works**: Caddy sandbox + dev `caddy trust` documented; prod DNS-01 challenge path documented.
- [ ] **Backup end-to-end**: `scripts/backup-postgres.sh` + `scripts/restore-postgres.sh` round-trip succeeds against Testcontainers Postgres (RPO ≤ 1h, RTO ≤ 4h documented in `disaster-recovery.md`).
- [ ] **Runbooks published**: 4 new runbooks (deploy, backup-recovery, disaster-recovery, gdpr-data-subject-request, setup-email-deliverability) live in `docs/runbooks/`.
- [ ] **5 ADRs published**: `docs/adr/0005-multi-tenant-architecture.md`, `0006-audit-decorator-pattern.md`, `0007-stripe-gateway-abstraction.md`, `0008-audit-retention-policy.md`, `0009-gdpr-right-to-be-forgotten.md`.
- [ ] **LICENSE + README mirrors**: `LICENSE` (proprietary English) + `LICENSE.es.md` (Spanish mirror) + `README.es.md` live at repo root; both languages share the same version number.
- [ ] **Sentry hooks gated**: BE + FE only initialize when `Sentry__Dsn`/`Sentry__DsnFrontend` env is set; dev / sandbox skip Sentry silently.
- [ ] **Stripe validator active**: `IValidateOptions<StripeOptions>` fails startup on `ApiKey.StartsWith("sk_test_local_dev_placeholder")` in Production/Staging environments; `appsettings.Development.json` bypasses.
- [ ] **`v1.0.0-rc1` tag** on `feature/0a-identity-model` HEAD after PR #40 merges.
- [ ] **Wave 10 archived**: `openspec/changes/2026-08-18-wave10-v1-readiness/` → `openspec/changes/archive/2026-08-18-wave10-v1-readiness/` after `sdd-archive` merges deltas into the 8 NEW specs.

---

## 14. After Approval: Next Steps

1. **sdd-spec phase**: write 8 NEW `openspec/specs/<slug>/spec.md` files. Each spec uses Given/When/Then per the OpenSpec convention; full Requirements + Scenarios per spec, totaling ~61 scenarios. Strict TDD applies from here forward.
2. **sdd-design phase**: write `openspec/changes/2026-08-18-wave10-v1-readiness/design.md` with sequence diagrams for the 4 cross-module flows: (a) GDPR DELETE cascade + grace + hard-delete sweep, (b) data export streaming, (c) CI merge-gate with Testcontainers, (d) Caddy TLS chain with DNS-01 challenge. Plus the 5 new ADRs as embedded decision records.
3. **sdd-tasks phase**: write `tasks.md` with per-slice phasing (RED tests → GREEN impl → wiring → validate). Forecast per Wave 7's methodology: `forecast = integration_scenarios + 2 * contract_scenarios_per_interface_surgery`. 5 of 6 slices need `size:exception`.
4. **sdd-apply phase**: 6 PRs chained to `feature/0a-identity-model`. Each slice runs through `sdd-attempt acquire` → apply → verify-report → merge. Reviewer acknowledges `size:exception` per slice (PR #37, #38, #40, #41, #42).
5. **sdd-verify phase**: full cumulative test suite + build + GDPR cascade integration test + migration-order verifier + security-headers verifier all pass. New CRITICAL bugs are remediated before the next slice merges (Wave 9 hotfix PR #36 precedent).
6. **sdd-archive phase**: move `openspec/changes/2026-08-18-wave10-v1-readiness/` → `openspec/changes/archive/2026-08-18-wave10-v1-readiness/`. The 8 NEW specs land in `openspec/specs/` (full merge, since all are NEW).
7. **Tag + release**: `v1.0.0-rc1` tag on `feature/0a-identity-model` HEAD post-archive. Ops team schedules first deploy + smoke pass.

---

## 15. Skill + Resolution

- **Skill loaded**: `sdd-propose` (loaded via `skill()` tool per task instruction).
- **Skill resolution**: `paths-injected` (the skill's SKILL.md was loaded and its instructions were followed end-to-end).
- **No additional skills required** for the propose phase. The sdd-spec, sdd-design, sdd-tasks, sdd-apply, sdd-verify, sdd-archive phases will each load their own skills in subsequent turns.

---

## 16. Cross-References

- `openspec/changes/2026-08-18-wave10-v1-readiness/explore.md` (487 lines, 54 KB — gap validation + 13 original open questions + 18 additional gaps)
- `openspec/changes/archive/2026-08-19-wave9-audit-finalization/proposal.md` (Wave 9 style reference)
- `openspec/config.yaml` (Strict TDD + project conventions)
- `openspec/changes/archive/2026-08-19-wave9-audit-finalization/verify-report-wave9-final.md` (3 CRITICAL bugs remediated — informs the Wave 10 risk model)
- `docs/PROJECT-STATUS.md` (12 deferred items, 5 of which Wave 10 unblocks)
- `docs/adr/` (4 existing ADRs; Wave 10 adds 0005–0009)
- `docs/runbooks/local-dev.md` (Wave 10 expands to 5 runbooks)
