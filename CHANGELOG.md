# Changelog

All notable changes to JadeCapital Suite will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Wave 10 (2026-08-19)
#### Added
- CI/CD pipeline (GitHub Actions: lint-backend, test-backend, test-integration, test-frontend) + Dependabot + nightly vulnerability scan
- Production deployment story (docker-compose.prod.yml + multi-stage Dockerfile.prod + DockerSecretConfigurationProvider adapter)
- Security headers (CSP + HSTS + Permissions-Policy) + TLS via Caddy auto-TLS
- Backup strategy (pg_dump + Redis BGSAVE + MinIO mirror) + disaster recovery runbook
- GDPR Art. 17 cascade deletor pattern (per-module deletor + orchestrator + audit anonymization)
- 30-day hard-delete sweep BackgroundService
- LICENSE (proprietary), CHANGELOG.md, CONTRIBUTING.md, SECURITY.md, README.es.md
- 5 NEW ADRs (0005-0009): multi-tenant, audit decorator, Stripe integration, 90-day retention, bespoke vs generic decorators
- SEO basics (robots.txt + sitemap.xml + OG tags)
- Test coverage (coverlet 70% threshold)
- OpenAPI export to swagger.json in CI
- Stripe test-mode validation (IValidateOptions + scripts/stripe-test-smoke.sh)

## [v1.0.0-rc1] - 2026-08-19
### Wave 9 (2026-08-19)
#### Added
- 4 NEW Trading audit decorators (AIRiskAdvice, CoachingPrompt, ScannerFilter, AttachmentSweep)
- Admin audit query API (GET /api/admin/audit/events with cursor pagination + AdminOnly)
- 90-day audit retention BackgroundService
- 3 NEW canonical specs (audit-query-api, audit-retention-policy, soft-delete-audit extended)

## [v1.0.0-rc1] - 2026-08-17
### Wave 8 (2026-08-17)
#### Added
- 7 NEW audit decorators (Account, Instrument, Alert, TradeReview, PlannerSession, PreTradeChecklist, StripeCustomer)

## [v1.0.0-rc1] - 2026-08-15
### Wave 7 (2026-08-15)
#### Added
- 5 NEW audit decorators (Trade, Strategy, JournalEntry, ImportJob — extended, Tenant — extended)
- `DecoratedRepository<T>` refactor (moved to Shared.Infrastructure for cross-module reuse)
- Soft-delete audit consistent pattern (Created/Updated/Deleted events with cross-tenant Denied)

## [v1.0.0-rc1] - 2026-08-13
### Wave 6 (2026-08-13)
#### Added
- Multi-tenant model: tenants table + `tenant_id` JWT claim + TenantContext middleware + EF Core global query filter
- Tenants CRUD endpoints (POST /api/tenants + PATCH + GET + users subresource)
- Stripe gateway abstraction (`IStripeGateway` + `StripeHttpGateway` + `StubStripeGateway`)
- Webhook handler with signature verification + idempotency
- StripeCustomer + Subscription audit decorators
- SQLite in-memory test infra for integration tests
- BackfillTenantsHostedService for pre-Wave-6 NULL users

## [v1.0.0-rc1] - 2026-08-11
### Wave 5 (2026-08-11)
#### Added
- AI risk-advice + coaching-prompt aggregates
- AI provider interface (`IAIProvider` + `OllamaHttpClient`)
- CSV import pipeline (ImportJob aggregate + background status)
- Scanner filters CRUD
- Pre-trade checklist aggregate

## [v1.0.0-rc1] - 2026-08-09
### Wave 4 (2026-08-09)
#### Added
- Behavioral analytics (5 detection rules + emotionality buckets)
- Trade MFE/MAE approximation
- Rule-based coaching prompts (5 rules)
- Daily journal endpoints (CRUD + range)
- Market data quotes (with api-quotes rate limit)
- SignalR /hubs/quotes for realtime quote broadcast
- MinIO attachment storage (IAttachmentStorage + MinioInitializerHostedService)

## [v1.0.0-rc1] - 2026-08-07
### Wave 3 (2026-08-07)
#### Added
- Trader strategies CRUD + tag/untag trade
- Alerts list + ack endpoint + BackgroundService evaluation
- Planner sessions (create, update, list-by-week, status change)
- React Query for FE (later Angular signals)

## [v1.0.0-rc1] - 2026-08-05
### Wave 2 (2026-08-05)
#### Added
- Trade review endpoints (GET /api/trades/{id}/review + POST + PATCH)
- Position-size calculator endpoint
- Risk profile management (single-active profile per user)
- Watchlist enhancements
- Trade journal entry CRUD

## [v1.0.0-rc1] - 2026-08-03
### Wave 1 (2026-08-03)
#### Added
- Dashboard + Calendar (P&L summaries + heatmap)
- Trade/Account/Instrument aggregates
- Per-trade P&L recalc
- Active users background service

## [v1.0.0-rc1] - 2026-08-01
### Wave 0 (2026-08-01)
#### Added
- Initial Identity module (auth, JWT, refresh tokens rotativos, recovery flow + rotación forzada)
- Trading module skeleton (Trade + Account)
- Billing module skeleton (Subscription/Plan)
- Admin module skeleton (RequireAdminPolicyHandler)
- Shared.Kernel (Money, Currency, Symbol, Entity, AggregateRoot, ValueObject)
- Shared.Infrastructure (ValidationBehavior, EF Core helpers, PiiLogScrubber)
- docker compose dev stack (Postgres + Redis + MinIO + Mailpit)
- ADR 0001 (auto-confirm on register), 0002 (BackgroundService over Hangfire), 0003 (remove tier field)
