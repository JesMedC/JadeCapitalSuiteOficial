# Production Readiness Specification

**Change**: 2026-08-18-wave10-v1-readiness
**Wave**: 10 (v1 readiness)
**Slice**: 10.6 — Documentation + observability + SEO + coverage + Stripe-verify
**Status**: NEW spec (no prior canonical)
**Strict TDD**: ACTIVE — every Requirement + Scenario here must be covered by tests in slice 10.6

## Purpose

Close the documentation + SEO + coverage + Stripe-verify surface for v1.0.0-rc1. The repo MUST ship `LICENSE` (Proprietary / "Todos los derechos reservados" per user decision) + `LICENSE.es.md` (Spanish mirror) + `CHANGELOG.md` (Keep-a-Changelog 1.1.0 format covering Waves 0–9) + `CONTRIBUTING.md` (explicitly stating the repo is NOT open source) + `SECURITY.md` (vulnerability disclosure via `security@jadecapital.com`) + `README.es.md` (Spanish mirror of `README.md`). Five new ADRs MUST be published: `0005` multi-tenant, `0006` audit decorator pattern, `0007` Stripe gateway abstraction, `0008` audit retention policy, `0009` GDPR right-to-be-forgotten. SEO basics MUST ship: `sitemap.xml` + `robots.txt` + OG tags in `index.html`. Test coverage MUST be wired via `coverlet.collector` with a 70% line-coverage gate enforced in CI. Stripe production keys MUST be validated at startup via `IValidateOptions<StripeOptions>` to fail-fast on the placeholder OR missing live keys in non-Development environments; test-mode keys are accepted for v1.0.0-rc1 with a smoke script that exercises the full lifecycle.

## ADDED Requirements

### Requirement: LICENSE (Proprietary) at repo root

The system MUST publish `LICENSE` at `/home/nitro/Proyects/JadeCapitalSuiteOficial/LICENSE` containing the proprietary "All rights reserved" copyright for `Jade Capital S.L.` (or current legal entity), with year 2026. The file MUST include a clear statement that the code is NOT open source and that redistribution, modification, or commercial use without written permission is prohibited. A Spanish mirror `LICENSE.es.md` MUST be committed in the same PR with the equivalent translation. `CONTRIBUTING.md` MUST state explicitly that external PRs are not accepted.

#### Scenario: LICENSE file present with proprietary copyright

- GIVEN the repo HEAD includes `LICENSE`
- WHEN `head -20 LICENSE` runs
- THEN the file MUST contain "Copyright (c) 2026 Jade Capital S.L." AND "All rights reserved."
- AND `LICENSE.es.md` MUST contain the equivalent Spanish text: "Todos los derechos reservados"

### Requirement: CHANGELOG.md (Keep a Changelog format)

The system MUST publish `CHANGELOG.md` at the repo root following Keep-a-Changelog 1.1.0 conventions: sections `## [Unreleased]`, `## [1.0.0-rc1] - YYYY-MM-DD`, plus historical entries for each wave backfilled. Each version section MUST contain `### Added`, `### Changed`, `### Fixed`, `### Removed` subsections. The first entry (Wave 0) MUST be dated 2026-07-XX (project inception).

#### Scenario: CHANGELOG.md lists Wave 0-9 with sections

- GIVEN the repo HEAD includes `CHANGELOG.md`
- WHEN a developer reads the file
- THEN it MUST contain a `## [1.0.0-rc1]` section with `### Added` listing at minimum: "GDPR right-to-be-forgotten endpoint", "Data portability export", "TLS termination via Caddy", "Backup strategy + migration order fix", "Documentation bundle (LICENSE, ADRs 0005-0009)"
- AND historical sections MUST reference Waves 0–9

### Requirement: CONTRIBUTING.md documents dev setup + PR template

The system MUST publish `CONTRIBUTING.md` at the repo root covering: (a) prerequisites (.NET 10 SDK via `mise`, Node 20+, Docker), (b) `make dev` or equivalent to spin up Postgres + Redis + MinIO + Mailpit, (c) commit convention (Conventional Commits — `feat:`, `fix:`, `chore:`, `refactor:`), (d) test command (`dotnet test --nologo --verbosity minimal`), (e) PR template reference (link to `.github/pull_request_template.md`). The file MUST explicitly state the repo is NOT open source and external PRs are not accepted (proprietary LICENSE).

#### Scenario: CONTRIBUTING.md states repo is proprietary

- GIVEN the repo HEAD includes `CONTRIBUTING.md`
- WHEN the file is read
- THEN it MUST contain a top-level notice: "This repository is proprietary software. External contributions are not accepted. See LICENSE."

### Requirement: SECURITY.md with disclosure email + timeline

The system MUST publish `SECURITY.md` at the repo root with: (a) supported versions table (only `main` and the latest release receive security backports), (b) `security@jadecapital.com` as the disclosure contact, (c) response timeline ("initial response within 72 hours", "status update within 7 days", "fix ETA within 30 days for HIGH/CRITICAL"). GitHub Security Advisories MUST be enabled.

#### Scenario: SECURITY.md includes disclosure email + timeline

- GIVEN the repo HEAD includes `SECURITY.md`
- WHEN a security researcher consults the file
- THEN it MUST list `security@jadecapital.com` as the contact
- AND it MUST list a response timeline ≤ 72h for first reply
- AND it MUST declare which versions receive backports

### Requirement: README.es.md (Spanish mirror of README.md)

The system MUST publish `README.es.md` at the repo root as a Spanish translation of `README.md`. Both files MUST share the same version badges + screenshot references + table of contents structure. A CI script `scripts/check-docs-sync.sh` MUST verify the section headings + image filenames match between the two files (no deep translation check — that's a human task).

#### Scenario: README.es.md structure matches README.md

- GIVEN the repo HEAD includes both `README.md` and `README.es.md`
- WHEN `scripts/check-docs-sync.sh` runs in CI
- THEN the script MUST extract H2 headings from both files + assert the heading set is identical
- AND image references + version badges MUST match exactly

### Requirement: 5 new ADRs (0005-0009)

The system MUST publish 5 new Architecture Decision Records in `docs/adr/`:
- `0005-multi-tenant-architecture.md` — `tenant_id` JWT claim + `TenantContextMiddleware` + EF query filter (Wave 6 6c.2)
- `0006-audit-decorator-pattern.md` — `DecoratedRepository<T>` + Scrutor `services.Decorate<...>` (Wave 6/7/8)
- `0007-stripe-gateway-abstraction.md` — `IStripeGateway` + Stub fallback (Wave 6 6a.1)
- `0008-audit-retention-policy.md` — 90-day retention `AuditRetentionBackgroundService` (Wave 9 9b.1)
- `0009-gdpr-right-to-be-forgotten.md` — GDPR Art. 17 cascade + 30-day grace + hard-delete sweep (Wave 10 10.5)

Each ADR MUST follow the standard format: Context → Decision → Consequences → Alternatives considered.

#### Scenario: 5 ADRs are present with required content

- GIVEN the repo HEAD includes `docs/adr/`
- WHEN `ls docs/adr/` runs
- THEN the directory MUST contain `0005-multi-tenant-architecture.md`, `0006-audit-decorator-pattern.md`, `0007-stripe-gateway-abstraction.md`, `0008-audit-retention-policy.md`, `0009-gdpr-right-to-be-forgotten.md`
- AND each MUST include a `## Consequences` section listing at least 3 trade-offs

### Requirement: SEO basics — sitemap.xml + robots.txt + OG tags

The system MUST ship:
- `frontend/src/assets/robots.txt` — `Disallow: /api/` + `Sitemap: https://jadecapital.com/sitemap.xml`
- `GET /sitemap.xml` endpoint in the API — returns XML listing every public route (`/`, `/pricing`, `/faq`, `/login`, `/register`) + lastmod dates
- `frontend/src/index.html` — MUST include `<meta property="og:title">`, `<meta property="og:description">`, `<meta property="og:image" content="https://jadecapital.com/assets/og-card.png">`, `<meta property="og:url">`, `<meta name="twitter:card" content="summary_large_image">`

#### Scenario: SEO assets are present + correct

- GIVEN a fresh `ng build`
- WHEN `dist/frontend/assets/robots.txt` is served at `/robots.txt`
- THEN it MUST contain `Sitemap: https://jadecapital.com/sitemap.xml`
- AND `GET /sitemap.xml` MUST return 200 with valid XML containing all public routes
- AND `index.html` MUST include the `og:title`, `og:description`, `og:image`, and `twitter:card` meta tags

### Requirement: Test coverage report via coverlet (70% line-coverage gate in CI)

The system MUST add `<PackageReference Include="coverlet.collector" Version="6.0.4" />` to every `*.UnitTests.csproj` + `*.IntegrationTests.csproj`. The CI workflow's `test-backend` job MUST run `dotnet test --collect:"XPlat Code Coverage" --results-directory ./artifacts/coverage`. The `ci.yml` MUST add a coverage gate step that fails the build if `coverlet`'s line coverage `< 70%`. If the baseline is `< 70%`, the gate MUST be raised in 5% increments over the Wave 10 lifecycle (tracked in CHANGELOG).

#### Scenario: CI fails when line coverage drops below 70%

- GIVEN the current line coverage is 72%
- WHEN a PR removes 5% of tested code
- AND CI runs the coverage gate
- THEN the build MUST fail with `Coverage 67% < 70% threshold`

### Requirement: Stripe test-mode validation (IValidateOptions<StripeOptions> + smoke script)

The system MUST implement `IValidateOptions<StripeOptions>` at `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Configuration/StripeOptionsValidator.cs`. The validator MUST fail startup (`ValidateOnStart`) when `env.IsProduction() || env.IsStaging()` AND any of these conditions hold: `ApiKey.IsNullOrEmpty()`, `ApiKey.StartsWith("sk_test_local_dev_placeholder")`, `ApiKey.Length < 32`, `WebhookSecret.IsNullOrEmpty()`. The dev `.env` MUST keep `STRIPE_SECRET_KEY=sk_test_local_dev_placeholder` and bypass validation via `ASPNETCORE_ENVIRONMENT=Development`. The system MUST publish `scripts/stripe-test-smoke.sh` that uses `sk_test_*` keys to exercise: create Checkout session → simulate `checkout.session.completed` webhook → verify subscription created → cancel via Portal → verify `customer.subscription.deleted` webhook received. The script MUST exit non-zero on any failure.

#### Scenario: dev .env with placeholder Stripe key fails-fast at startup (production env only)

- GIVEN `ASPNETCORE_ENVIRONMENT=Production` AND `STRIPE_SECRET_KEY=sk_test_local_dev_placeholder`
- WHEN the API starts
- THEN startup MUST fail with `OptionsValidationException: StripeOptions.ApiKey contains the dev placeholder`
- AND the host MUST exit with a non-zero code

- GIVEN `ASPNETCORE_ENVIRONMENT=Development` AND the same placeholder key
- WHEN the API starts
- THEN startup MUST succeed (Development skips validation)
- AND `StubStripeGateway` MUST be registered (per Wave 6 spec)

#### Scenario: scripts/stripe-test-smoke.sh runs checkout flow with test keys

- GIVEN `STRIPE_SECRET_KEY=sk_test_<real test key from user>` is set
- WHEN `scripts/stripe-test-smoke.sh` runs against a local API + Stripe test mode
- THEN the script MUST: create a Customer, create a Checkout session, simulate the webhook, verify the subscription is persisted, cancel via Portal, verify the cancellation webhook
- AND exit 0 on success, non-zero on any step failure

## Cross-references

- Closes gaps B5 (coverage report), B9 partial (Sentry hooks in 10.6 companion spec), B11 (SEO), B13 (LICENSE + CHANGELOG + CONTRIBUTING + SECURITY.md), B14 (README.es.md), B15 (ADRs), B17 (Stripe prod key validation — A4 wedge), A4 (Stripe prod key validation)
- Related Wave 10 spec: `openspec/changes/2026-08-18-wave10-v1-readiness/specs/observability-light/spec.md` (Sentry hooks + OpenAPI export)
- Related Wave 10 spec: `openspec/changes/2026-08-18-wave10-v1-readiness/specs/ci-infrastructure/spec.md` (CI runs coverlet gate + OpenAPI export step)
- Source: `openspec/changes/2026-08-18-wave10-v1-readiness/explore.md` §A4, §B5, §B11, §B13, §B14, §B15, §B17

## Out of scope

- Auto-translation of all docs to other languages (only English + Spanish ship — additional languages Wave 11+)
- License compliance tooling (FOSSA, Snyk License) — Wave 11+
- Doc linting (Vale, write-good) — Wave 11+ once the doc set stabilizes
- Storybook / component docs for the FE — Wave 11+
- ADR 0010+ for future decisions — lands as decisions are made
- `og:image` A/B testing + dynamic OG cards — Wave 11+
- Bundle size budget enforcement in CI (gap G-B13, partial in 10.1 CI — enforcement gate Wave 11+)
- Visual regression testing (Percy/Chromatic, Wave 11+, gap G-B15)
