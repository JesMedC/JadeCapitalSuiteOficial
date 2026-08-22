# Delta for Observability Light

## Scope Guard

This delta MUST NOT reimplement shipped Sentry hooks, accessibility/axe, AI options DI, welcome-email options, GA tagging, Stripe host validation, or client-IP capture.

## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: Sentry integration — gated on env var presence

Backend Sentry MUST bind only `Sentry:Dsn` (`Sentry__Dsn`). Production frontend deployment MUST supply its DSN. Empty DSNs MUST disable either SDK. Both SDKs MUST disable default PII, exclude sensitive values, and retain release and stack context.
(Previously: backend naming and frontend production activation were inconsistent.)

#### Scenario: Sentry DSN unset → silent skip
- GIVEN empty backend and frontend DSNs
- WHEN applications start
- THEN neither SDK MUST send events

#### Scenario: Sentry reports errors
- GIVEN production backend and frontend DSNs with a release
- WHEN either application captures an unhandled exception
- THEN Sentry MUST receive resolvable, release-tagged context without sensitive values
