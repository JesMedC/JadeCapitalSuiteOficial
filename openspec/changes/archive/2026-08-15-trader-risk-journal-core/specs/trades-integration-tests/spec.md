# Trades Integration Tests Specification

## Purpose

End-to-end integration coverage for the Trader surface using Testcontainers (PostgreSQL, Redis, MinIO) and Respawn for database cleanup. The suite covers Open → Checklist → Close → Review → Attachments → Metrics against a real HTTP pipeline, and prevents regressions in the Wave 1 slices.

## Requirements

### Requirement: Containerized fixture

The integration suite MUST spin up PostgreSQL, Redis and MinIO via Testcontainers. Containers MUST be reused across tests (static, lazy) per the existing `JadeApiFactory` pattern. The database MUST be initialized with every hand-authored migration from `infrastructure/postgres/migrations/`. Each test MUST start from a clean schema state via Respawn.

#### Scenario: First test in the run

- GIVEN the suite starts with no containers running
- WHEN the first test requests the fixture
- THEN PostgreSQL, Redis and MinIO containers MUST start once
- AND the schema MUST be initialized via the migrations directory
- AND Respawn MUST reset the database before the test body runs

#### Scenario: Subsequent test in the run

- GIVEN the suite has run at least one test
- WHEN a later test requests the fixture
- THEN the existing containers MUST be reused
- AND Respawn MUST reset the database before the test body runs
- AND container startup MUST NOT block the test

### Requirement: Trade flow coverage

The suite MUST cover Open → Checklist → Close → UpdateNotes → Delete for a Trader identity. It MUST also assert that the dashboard summary, list, calendar and metrics endpoints return scoped data for that user only. Cross-user reads MUST be asserted impossible.

#### Scenario: Happy path

- GIVEN a registered Trader identity
- WHEN they open a trade with a valid checklist, close it, attach a review with one attachment, and request metrics
- THEN every step MUST succeed with the expected status code
- AND the final metrics payload MUST include the trade's contribution to expectancy, profit factor, max drawdown and the symbol stats group

#### Scenario: Cross-user isolation

- GIVEN two registered Trader identities with disjoint trade histories
- WHEN user A requests metrics and the trade list
- THEN no field, count or curve point from user B MUST appear

### Requirement: Checklist validation

The suite MUST cover at least three failing checklist scenarios: out-of-range `confluencesCount`, setup quality outside the enum, and risk-reward below the active profile target. Each MUST produce `422` with field-level reasons and MUST NOT persist a `trading.trades` row or a `trading.pre_trade_checklists` row.

#### Scenario: Failing checklist

- GIVEN an active risk profile with `RiskRewardTarget = 2.0`
- WHEN the user submits `POST /api/trades` with a checklist whose `riskRewardAtEntry = 1.5`
- THEN the response MUST be `422` with `code = "validation"`
- AND the database MUST have zero rows in `trading.trades` and `trading.pre_trade_checklists` for that attempt

### Requirement: Review and attachment coverage

The suite MUST cover the full attachment flow: request slot → upload bytes to MinIO via the presigned URL → call `complete` with the SHA-256 hash → confirm the row exists. It MUST also assert the backend never exposes an endpoint that proxies the bytes.

#### Scenario: Attachment happy path

- GIVEN a closed trade
- WHEN the user requests an attachment slot, uploads a small PNG to the presigned URL, and calls `complete`
- THEN the `trading.trade_attachments` row MUST exist with the matching `objectKey`, `contentType`, `sizeBytes`, `sha256`
- AND the MinIO object MUST be retrievable via a `GetObject` request from the test fixture

#### Scenario: Cross-user attachment attempt

- GIVEN user A owns a review with an attachment slot
- WHEN user B attempts to call `complete` for that attachment
- THEN the response MUST be `404 Not Found`
- AND the attachment row MUST remain in state `Pending`

### Requirement: Metrics coverage

The suite MUST cover the four periods (`7d`, `30d`, `90d`, `all`) and MUST assert the empty-period, all-open and mixed-period scenarios. The suite MUST NOT mock any metrics endpoint or service.

#### Scenario: Mixed-period metrics

- GIVEN a Trader identity with one trade closed today and another closed 100 days ago
- WHEN the user requests `?period=30d` and `?period=all`
- THEN the `30d` response MUST include only the recent trade
- AND the `all` response MUST include both
- AND no client-side computation MUST appear in the response payload

### Requirement: Determinism and isolation

Every test MUST be runnable in any order without depending on the side effects of another test. Each test MUST register its own user via `POST /api/auth/register` (Wave 0 endpoint) and MUST NOT share seed data. Respawn MUST run between tests; the suite MUST fail if Respawn is disabled.

#### Scenario: Independent runs

- GIVEN any two tests in the suite
- WHEN they run in either order
- THEN both MUST pass
- AND the database state from one MUST NOT influence the other