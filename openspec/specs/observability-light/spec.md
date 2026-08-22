# Observability Light Specification

**Change**: 2026-08-18-wave10-v1-readiness
**Wave**: 10 (v1 readiness)
**Slice**: 10.6 — Docs + observability-light + SEO + coverage + Stripe-verify
**Status**: NEW spec (no prior canonical)
**Strict TDD**: ACTIVE — every Requirement + Scenario here must be covered by tests in slice 10.6

## Purpose

Define the v1.0.0-rc1 observability surface — lightweight, but enough to diagnose prod incidents without paging the dev. The system MUST ship Serilog structured logs (already wired in Wave 0; this slice formalizes the PII-scrubbing contract + correlation id propagation). Sentry integration MUST be **optional and gated on env var presence** — when `Sentry__Dsn` is unset (dev / sandbox), no Sentry SDK is initialized and no error is logged. When set, BE + FE both capture unhandled exceptions. The `/health/live` + `/health/ready` endpoints (already in `Program.cs:401-409`) MUST be formalized with explicit liveness vs readiness semantics. The OpenAPI spec MUST be exported to `artifacts/swagger.json` in CI for downstream tooling (no manual export from dev machines).

## ADDED Requirements

### Requirement: Structured logging via Serilog — PII scrubber mandatory

The system MUST configure Serilog at `src/1.Api/JadeCapital.Host/Program.cs` to emit JSON logs with these properties on every entry: `correlation_id` (from `HttpContext.TraceIdentifier` or `Activity.Current?.TraceId`), `level`, `message_template`, `properties` (event-specific structured fields), `timestamp`. The system MUST install `PiiLogScrubber` as a Serilog `IDestructuringPolicy` + a `DelegatingSink` filter that removes these patterns BEFORE persistence: email addresses (`\S+@\S+\.\S+`), JWTs (`eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+`), password fields (`"password"\s*:\s*"[^"]+"`), credit-card numbers (Luhn-validated 13–19 digit sequences). A unit test MUST verify a log containing an email + a JWT emits the redacted form.

#### Scenario: log entry has correlation id, level, message, properties (no PII)

- GIVEN a request hits `POST /api/auth/register`
- WHEN Serilog emits a log entry
- THEN the JSON MUST contain `correlation_id = "<TraceIdentifier>"`, `level = "Information"`, `message_template = "User {UserId} registered."`, `properties = { "UserId": "<guid>" }`
- AND the entry MUST NOT contain `email = "<plaintext>"` (must be `email = "***"` or omitted)

#### Scenario: PiiLogScrubber removes PII patterns from all logs

- GIVEN a log entry `LogInformation("Sending email to {Email}", "user@example.com")`
- WHEN the entry passes through `PiiLogScrubber`
- THEN the emitted JSON MUST contain `"Email": "***"` (or the field MUST be removed entirely)
- AND a log entry containing a JWT MUST emit `"<redacted>"` or omit the JWT

### Requirement: Sentry integration — gated on env var presence

Backend Sentry MUST bind only `Sentry:Dsn` (`Sentry__Dsn`). Production frontend deployment MUST supply its DSN. Empty DSNs MUST disable either SDK. Both SDKs MUST disable default PII, exclude sensitive values, and retain release and stack context.

#### Scenario: Sentry DSN unset → silent skip

- GIVEN empty backend and frontend DSNs
- WHEN applications start
- THEN neither SDK MUST send events

#### Scenario: Sentry reports errors

- GIVEN production backend and frontend DSNs with a release
- WHEN either application captures an unhandled exception
- THEN Sentry MUST receive resolvable, release-tagged context without sensitive values

### Requirement: Optional PII-safe OpenTelemetry tracing

The Host MUST export OTLP traces only with an endpoint; otherwise export MUST be disabled silently. Spans MUST NOT contain credentials, cookies, bodies, or personal data.

#### Scenario: OTLP exports trace
- GIVEN a reachable OTLP endpoint
- WHEN an API request completes
- THEN its trace MUST include identity, route, method, status, and duration

#### Scenario: OTLP is disabled safely
- GIVEN no OTLP endpoint
- WHEN the Host handles requests
- THEN no exporter MUST run; requests MUST succeed

#### Scenario: Sensitive data excluded
- GIVEN a request carries sensitive values
- WHEN its spans export
- THEN those values MUST NOT appear in attributes or events

### Requirement: Health endpoints — /health/live + /health/ready

The system MUST expose two endpoints (already in `Program.cs`):
- `GET /health/live` → returns 200 with `{ "status": "alive" }` if the process is up (no dependency checks)
- `GET /health/ready` → returns 200 only when Postgres + Redis + MinIO are reachable; returns 503 with `{ "status": "unhealthy", "checks": { "postgres": "unhealthy" } }` otherwise

The `MapHealthChecks` MUST register custom `IHealthCheck` implementations for Postgres (`SELECT 1`) + Redis (`PING`) + MinIO (`/minio/health/live`). The response MUST be JSON (not plain text) for machine readability.

#### Scenario: /health/live returns 200 (process up)

- GIVEN the API process is running
- WHEN `GET /health/live` is called
- THEN the endpoint MUST return HTTP 200 with `{ "status": "alive", "uptime": "<seconds>" }` (no DB calls)

#### Scenario: /health/ready returns 200 only when Postgres + Redis reachable

- GIVEN Postgres + Redis are healthy
- WHEN `GET /health/ready` is called
- THEN the endpoint MUST return HTTP 200 with `{ "status": "healthy", "checks": { "postgres": "healthy", "redis": "healthy", "minio": "healthy" } }`

- GIVEN Postgres is unreachable
- WHEN `GET /health/ready` is called
- THEN the endpoint MUST return HTTP 503 with `checks.postgres = "unhealthy"`

### Requirement: OpenAPI export to swagger.json in CI

The CI workflow (defined in `ci-infrastructure` spec) MUST add a step to the `test-integration` job: `dotnet swagger tofile --output artifacts/swagger.json`. The `artifacts/` directory MUST be uploaded as a workflow artifact with a 30-day retention. The artifact MUST contain the full OpenAPI 3.0 spec for every endpoint in `src/1.Api/JadeCapital.Host/`.

#### Scenario: CI artifact contains openapi.json with all endpoints

- GIVEN the CI workflow runs on a PR
- WHEN the `openapi-export` job step completes
- THEN the workflow MUST upload `artifacts/swagger.json` (or `openapi.json`) as a downloadable artifact
- AND the file MUST list every endpoint declared via `MapGet`/`MapPost`/`MapPut`/`MapDelete`/`MapPatch` in the Host project
- AND the file MUST be valid JSON (parsable)

## Cross-references

- Closes gaps B1 partial (Sentry hooks for exception capture only), B17 (OpenAPI export artifact)
- Related Wave 10 spec: `openspec/changes/2026-08-18-wave10-v1-readiness/specs/ci-infrastructure/spec.md` (the OpenAPI export step is a job in that workflow)
- Related Wave 10 spec: `openspec/changes/2026-08-18-wave10-v1-readiness/specs/security-headers/spec.md` (CSP `report-uri` could feed Sentry — out of scope for v1.0)
- Source: `openspec/changes/2026-08-18-wave10-v1-readiness/explore.md` §B1, §B9, §B17

## Out of scope

- OpenTelemetry traces (OTLP exporter, Wave 11+, gap B1 — needs sampling + exporter design)
- Prometheus metrics endpoint (`/metrics`, Wave 11+ — requires metric naming + scrape config)
- Custom Grafana / Datadog dashboards (Wave 11+ — depends on metrics/traces)
- External uptime monitoring (UptimeRobot / Better Stack — ops decision, Wave 11+, gap B2)
- Real User Monitoring (RUM) via Sentry (Sentry SDK can do this; out of scope for v1.0 to keep the FE bundle small)
- Log aggregation pipeline (Loki / Datadog Logs — Wave 11+; v1.0 ships logs to stdout only)
- PagerDuty / OpsGenie alerting on `/health/ready` failures (ops decision, Wave 11+)
