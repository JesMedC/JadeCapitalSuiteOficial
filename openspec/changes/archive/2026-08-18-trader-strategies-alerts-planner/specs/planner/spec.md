# Planner Specification

## Purpose

Weekly planner for trading sessions. The trader schedules when they intend to trade (lunes 10hs, miércoles 14hs, viernes 9hs), and at the end of the week sees planned vs actual: how many sessions were completed, skipped, or had unexpected trades.

Planner closes the loop "deliberate trading": without it, the trader opens the app and trades reactively. With it, the trader has a cadence and the system can compare intended vs done.

## Requirements

### Requirement: Create a planner session

The system MUST allow a user to create a `PlannerSession` with `date` (DATE), `planned_start_time` (TIME nullable), `planned_end_time` (TIME nullable), `symbol` (nullable), `notes` (≤ 500 chars). On create, `status` MUST default to `Planned`. The session is scoped to the user (no multi-user sessions in Wave 3).

#### Scenario: First session of the week

- GIVEN user A has 0 sessions for 2026-W33
- WHEN `POST /api/planner` with `date: "2026-08-17", planned_start_time: "10:00", planned_end_time: "12:00", symbol: "EURUSD"`
- THEN exactly one session MUST be persisted
- AND `status` MUST be `Planned`

#### Scenario: Validation — invalid time range

- GIVEN `planned_start_time: "12:00"` and `planned_end_time: "10:00"`
- WHEN the request is validated
- THEN the system MUST return `422` with `planner.end_before_start`

### Requirement: Update session status

The user MAY update a session's `status` (planned → completed / skipped / cancelled), `notes`, or `planned_*_time`. Modifying `date` creates a new session instead (move semantics). Cross-user edits MUST be impossible.

#### Scenario: Mark session completed

- GIVEN a `Planned` session for today
- WHEN `PATCH /api/planner/{id}` with `status: "Completed"` is called
- THEN the session MUST be updated
- AND `status` MUST be `Completed`

#### Scenario: Skip a session

- GIVEN a `Planned` session for today
- WHEN `PATCH /api/planner/{id}` with `status: "Skipped"` is called
- THEN the session MUST be updated
- AND `status` MUST be `Skipped`

#### Scenario: Cross-user edit rejected

- GIVEN user A's session
- WHEN user B calls `PATCH /api/planner/{id}`
- THEN the system MUST return 404

### Requirement: List by week

`GET /api/planner?week=2026-W33` MUST return all sessions for the calling user whose `date` falls within ISO week 33 of 2026 (Monday 2026-08-10 to Sunday 2026-08-16). Each session MUST include the comparison payload: `actualTradeCount` (closed trades on `date` for the same `symbol` if set, or all closed trades if `symbol` is null).

#### Scenario: List week with mixed sessions

- GIVEN user A has 3 sessions in 2026-W33: 1 planned (Mon), 1 completed (Wed), 1 skipped (Fri)
- WHEN `GET /api/planner?week=2026-W33` is called
- THEN the response MUST contain all 3 sessions in chronological order
- AND each session MUST include `actualTradeCount` (computed from `trading.trades`)

#### Scenario: Invalid week format

- GIVEN `week=2026-W99` (week 99 doesn't exist)
- WHEN the request is validated
- THEN the system MUST return 422 with `planner.invalid_week`

#### Scenario: Cross-user isolation

- GIVEN user A has sessions in 2026-W33
- WHEN user B calls `GET /api/planner?week=2026-W33`
- THEN the response MUST return user B's sessions (empty if user B has none)

### Requirement: Compare planned vs actual

For each session in the week response, the payload MUST include `comparison`:

```json
{
  "sessionId": "...",
  "status": "Completed",
  "actualTradeCount": 3,
  "actualClosedTradeCount": 2,
  "actualSymbols": ["EURUSD", "GBPUSD"],
  "followsPlan": true
}
```

- `actualTradeCount`: number of trades opened on `date` (open or closed).
- `actualClosedTradeCount`: subset that are closed.
- `actualSymbols`: distinct symbols traded on `date`.
- `followsPlan`: `true` if `status == Completed` AND `actualClosedTradeCount > 0` AND (session.symbol is null OR actualSymbols contains session.symbol).

#### Scenario: Completed session with matching trades

- GIVEN a Completed session for 2026-08-17 with symbol EURUSD
- AND 2 closed EURUSD trades on 2026-08-17
- WHEN `GET /api/planner?week=2026-W34` is called
- THEN the session's `comparison.followsPlan` MUST be `true`

#### Scenario: Skipped session

- GIVEN a Skipped session for 2026-08-17
- AND 0 trades on 2026-08-17
- WHEN the response is built
- THEN `comparison.followsPlan` MUST be `false` (skipped is not "followed plan")

#### Scenario: Off-plan trade

- GIVEN a Completed session for 2026-08-17 with symbol EURUSD
- AND 2 closed GBPUSD trades on 2026-08-17 (no EURUSD)
- WHEN the response is built
- THEN `comparison.followsPlan` MUST be `false`

### Requirement: Skip without deleting

`PATCH /api/planner/{id}` with `status: "Skipped"` MUST mark the session as skipped (audit trail preserved) rather than deleting it. The session remains queryable in `GET /api/planner` for the week. No hard-delete endpoint in Wave 3.

#### Scenario: Skipped session remains

- GIVEN a Planned session
- WHEN the user marks it Skipped
- THEN the session MUST remain in `GET /api/planner?week=...`
- AND `status` MUST be `Skipped` (not deleted)

### Requirement: Validated inputs

`date` MUST be a valid DATE in [1900-01-01, 2100-12-31]. `planned_start_time` and `planned_end_time` MUST be valid TIME values (HH:MM 24h). `planned_end_time` MUST be > `planned_start_time` if both set. `status` MUST be one of `Planned, Completed, Skipped, Cancelled`. `notes` MUST be ≤ 500 chars.

#### Scenario: Notes too long

- GIVEN `notes` length 600
- WHEN the request is validated
- THEN the system MUST return 422 with `planner.notes_too_long`

#### Scenario: Invalid status

- GIVEN `status: "Pending"`
- WHEN the request is validated
- THEN the system MUST return 422 with `planner.invalid_status`

### Requirement: Authenticated CRUD

All endpoints (`GET /api/planner`, `POST /api/planner`, `PATCH /api/planner/{id}`) MUST require an authenticated Trader identity. The response MUST include only the requesting user's sessions; cross-user reads MUST be impossible.

#### Scenario: Unauthenticated caller

- GIVEN an anonymous request
- WHEN any planner endpoint is called
- THEN the system MUST return 401

## Data Model

```
trading.planner_sessions
  id                 UUID PK
  user_id            UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE
  date               DATE NOT NULL
  planned_start_time TIME NULL
  planned_end_time   TIME NULL
  symbol             VARCHAR(20) NULL            -- nullable: general session
  status             SMALLINT NOT NULL DEFAULT 0 -- 0=Planned, 1=Completed, 2=Skipped, 3=Cancelled
  notes              VARCHAR(500) NULL
  created_at         TIMESTAMPTZ NOT NULL DEFAULT now()
  updated_at         TIMESTAMPTZ NOT NULL DEFAULT now()

Indexes:
  ix_planner_user_date  (user_id, date)
  ix_planner_user_status (user_id, status)
```

The `comparison` payload is computed on read in the handler via JOIN with `trading.trades WHERE user_id = X AND closed_at::date = session.date`. No additional columns on `planner_sessions` for actual trades.

## Endpoints

- `GET /api/planner?week=YYYY-Www` — list sessions for the week with comparison payload.
- `POST /api/planner` — create a session. Body: `{ date, planned_start_time?, planned_end_time?, symbol?, notes? }`.
- `PATCH /api/planner/{id}` — update session. Body: `{ status?, planned_start_time?, planned_end_time?, notes? }`. `date` change = create new (not allowed in update).
- `DELETE /api/planner/{id}` — hard delete (admin use only; no documented user endpoint in Wave 3).

All require `RequireAuthorization` and `api-general` rate limit.

## Architecture

- **Trading.Domain** owns `PlannerSession` aggregate + `PlannerStatus` enum (in Shared.Kernel).
- **Trading.Application** owns handlers + `IPlannerSessionRepository`.
- **Trading.Infrastructure** owns EF configuration + `PlannerSessionRepository`.
- **Shared.Kernel** owns `PlannerStatus` enum (cross-module enum).
- **Comparison logic** lives in `GetPlannerSessionsByWeekHandler` — pure computation, no persistence.
- **Week derivation**: ISO 8601 week-of-year. Use `DateTimeOffset` + System.Globalization.UTC `ISOWeek.GetWeekOfYear` helper. No DB-side WEEKOFYEAR (Postgres `date_part('week', ...)` is ISO 8601 by default in postgres).
- **Time storage**: TIME (no TZ) — the user means "10:00 local". Render in user's timezone at the API boundary.
