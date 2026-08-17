# Alerts Specification

## Purpose

Automatic, persistent alerts evaluated by a periodic background service. Each rule inspects the user's recent trades and journal and emits 0..N alerts. Alerts are deduped (one per `RuleId` + `UserId` + `day`), persistent (trader can ack), and ordered by severity.

Alerts complement the synchronous `CoachingPrompts` (Wave 2d) which only render when the dashboard is open. Alerts persist and survive sessions, ensuring the trader sees them next time they open the app.

## Requirements

### Requirement: Pluggable alert rule registry

The system MUST evaluate user state against an `IAlertRule` registry. Each rule implements `string RuleId`, `int Priority`, `AlertSeverity` (defaults), and `IReadOnlyList<Alert> Evaluate(AlertContext ctx)`. New rules MAY be added without touching the rest of the system.

#### Scenario: Five initial rules registered

- GIVEN the application starts
- WHEN the registry is constructed
- THEN it MUST contain exactly these five rules: `NoTradesInDaysRule`, `DrawdownExceededRule`, `RRAverageBelowRule`, `CurrentPriceNearStopRule`, `OpenTradeOffPlanRule`
- AND the order MUST be stable (Priority ascending)

### Requirement: Five initial rules

#### `NoTradesInDaysRule`

- GIVEN the user's last closed trade was >= N days ago (default **5**, configurable per rule constant)
- AND the user had >= 1 closed trade in the previous 30 days
- WHEN evaluated
- THEN it MUST emit a `NoTradesInDays` alert with severity `low` and copy: "X días sin operar — ¿descanso intencional o falta de disciplina?"

#### `DrawdownExceededRule`

- GIVEN the user's current drawdown (peak-to-trough on closed trades' account equity) > Y% (default **5%**, configurable)
- WHEN evaluated
- THEN it MUST emit a `DrawdownExceeded` alert with severity `high` and copy: "DD actual > Y%. Revisa tu riesgo por trade y considerá reducir tamaño."

#### `RRAverageBelowRule`

- GIVEN the user's average R/R on the last 10 closed trades < Z (default **1.5**, configurable)
- WHEN evaluated
- THEN it MUST emit a `RRAverageBelow` alert with severity `medium` and copy: "Tu R/R promedio de los últimos 10 trades está por debajo de Z. ¿Estás entrando con poca ventaja?"

#### `CurrentPriceNearStopRule`

- GIVEN the user has >= 1 open trade with `StopLossPrice` (NOTE: Wave 3 inherits `Trade.Strategy` field; we use `EntryPrice` as proxy because Wave 3 has no `StopLossPrice` yet — explicit approximation)
- AND the open trade's `EntryPrice` (proxy) is within 1% of a "current price" (in Wave 3, no market data provider; use `EntryPrice` itself as proxy — the alert is informational, severity `low`)
- WHEN evaluated
- THEN it MUST emit a `CurrentPriceNearStop` alert with severity `low` and copy: "Tenés un trade abierto cerca de zona de entrada. (Wave 3: sin tick data real — alert informativo.)"

#### `OpenTradeOffPlanRule`

- GIVEN the user has a journal entry today with `premarket_plan` non-empty
- AND the user has >= 1 open trade today whose `Symbol` is NOT mentioned in the journal's tags or `premarket_plan` text
- WHEN evaluated
- THEN it MUST emit an `OpenTradeOffPlan` alert with severity `medium` and copy: "Operaste {Symbol} hoy, no estaba en tu plan. ¿Estabas siguiendo tu plan?"

### Requirement: Periodic evaluation

A `BackgroundService` MUST iterate every active user every 5 minutes (±30s jitter) and call `IAlertRegistry.EvaluateForUser(userId)`. The service MUST be registered as `IHostedService` in `Program.cs`. Iterations MUST be idempotent (dedup handled at insert).

#### Scenario: Background service runs

- GIVEN the API is running
- WHEN 5 minutes elapse
- THEN the BackgroundService MUST evaluate all active users
- AND any new alerts (not yet in DB for today) MUST be persisted

#### Scenario: Service survives transient errors

- GIVEN a rule throws an exception during evaluation
- WHEN the BackgroundService catches it
- THEN the service MUST log the error and continue with the next user
- MUST NOT crash the host

### Requirement: Acknowledge

`PATCH /api/alerts/{id}/ack` MUST set `acknowledged_at = now()` and return the updated alert. After ack, the alert MUST be filtered out of `GET /api/alerts?activeOnly=true`. Acked alerts MUST remain in `GET /api/alerts` (default = all) for audit trail.

#### Scenario: Ack an active alert

- GIVEN an alert with `acknowledged_at IS NULL`
- WHEN `PATCH /api/alerts/{id}/ack` is called
- THEN `acknowledged_at` MUST be set
- AND the alert MUST NOT appear in `GET /api/alerts?activeOnly=true`

#### Scenario: Ack an already-acked alert (idempotent)

- GIVEN an alert with `acknowledged_at = T1`
- WHEN `PATCH /api/alerts/{id}/ack` is called again
- THEN the response MUST return 200 with the same `acknowledged_at = T1`
- AND the operation MUST be idempotent (no error)

### Requirement: Active alerts listing

`GET /api/alerts?activeOnly=true` MUST return only alerts with `acknowledged_at IS NULL` AND `expires_at IS NULL OR expires_at > now()`. Default `activeOnly = false` returns all alerts (including acked and expired).

#### Scenario: Active-only filter

- GIVEN 3 alerts: 1 active, 1 acked, 1 expired
- WHEN `GET /api/alerts?activeOnly=true` is called
- THEN the response MUST contain only the 1 active alert

#### Scenario: All alerts (audit)

- GIVEN the same 3 alerts
- WHEN `GET /api/alerts` is called (no filter)
- THEN the response MUST contain all 3

### Requirement: Severity ordering

Alerts MUST be ordered by severity descending: `high` first, then `medium`, then `low`. Within a severity, newer alerts MUST come first. Pure SQL: `ORDER BY severity DESC, created_at DESC`.

#### Scenario: Multiple alerts

- GIVEN alerts: 1 medium (created today), 1 high (created today), 1 low (created yesterday)
- WHEN `GET /api/alerts` is called
- THEN the order MUST be: high → medium → low

### Requirement: PII-safe copy

Alert copy MUST NOT include absolute P&L amounts (no `+$150.00`, no `-50%`, no specific USD figures). The copy MAY use qualitative language ("DD > 5%", "sin trades hace 5 días") but MUST NOT include raw amounts, percentages, or instrument-specific amounts.

#### Scenario: Copy redaction

- GIVEN a `DrawdownExceededRule` evaluation with `totalPnl = -$450`
- WHEN the alert copy is generated
- THEN the rendered title and body MUST NOT contain "-$450" or "450 USD"
- AND MUST contain language like "DD > 5%" instead

### Requirement: Cross-user isolation

All alert endpoints MUST scope to the calling user's `UserId`. The repository MUST filter by `user_id` on every read. There is no admin endpoint in Wave 3.

#### Scenario: Unauthenticated caller

- GIVEN an anonymous request
- WHEN `GET /api/alerts` is called
- THEN the system MUST return 401

#### Scenario: User B reads user A's alert

- GIVEN user A has alert "abc"
- WHEN user B calls `GET /api/alerts/abc`
- THEN the system MUST return 404 (alert doesn't exist in user B's namespace)

### Requirement: Expiry

Alerts with `expires_at < now()` MUST be filtered out of `?activeOnly=true`. The BackgroundService MAY auto-expire (set `expires_at`) alerts older than 30 days (configurable) to bound table growth. Wave 3 implements expiry at read time (no scheduled cleanup).

#### Scenario: Expired alert filtered

- GIVEN an alert with `expires_at = "2026-08-01"` (past)
- WHEN `GET /api/alerts?activeOnly=true` is called (today is 2026-08-16)
- THEN the alert MUST NOT appear

#### Scenario: Future-dated alert remains active

- GIVEN an alert with `expires_at = "2026-12-31"`
- WHEN `GET /api/alerts?activeOnly=true` is called today
- THEN the alert MUST appear

### Requirement: One alert per (RuleId, UserId, day) — dedup

The repository MUST enforce a UNIQUE constraint on `(rule_id, user_id, DATE(created_at AT TIME ZONE 'UTC'))` (UTC date for simplicity — acceptable because periodicity 5 min makes timezone slippage despreciable). If a rule fires twice the same day, the second insert MUST be a no-op (ON CONFLICT DO NOTHING).

#### Scenario: Same rule fires twice

- GIVEN user A has `NoTradesInDays` alert from today
- WHEN the BackgroundService re-evaluates 5 min later and the rule fires again
- THEN the DB MUST still have only one row for `(NoTradesInDaysRule, user A, today)`

#### Scenario: Different days

- GIVEN user A has `NoTradesInDays` alert from yesterday
- WHEN the BackgroundService fires the rule today
- THEN a NEW alert MUST be created for today

#### Scenario: Acked alert doesn't dedup with new active

- GIVEN user A has acked `NoTradesInDays` from today
- WHEN the BackgroundService fires the rule today
- THEN a NEW active alert MUST be created (the dedup is on `(rule_id, user_id, date)` regardless of `acknowledged_at` — but the INSERT check uses a WHERE clause that excludes acked? NO: dedup is unconditional on day. Acked alerts block new ones. UX consideration: documented in design.)

## Data Model

```
trading.alerts
  id              UUID PK
  user_id         UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE
  rule_id         VARCHAR(64) NOT NULL  -- e.g. "NoTradesInDaysRule"
  severity        SMALLINT NOT NULL     -- 1=low, 2=medium, 3=high
  title           VARCHAR(120) NOT NULL
  body            VARCHAR(500) NOT NULL
  cta_route       VARCHAR(120) NULL     -- e.g. "/app/journal"
  cta_label       VARCHAR(60) NULL      -- e.g. "Revisar pre-mercado"
  acknowledged_at TIMESTAMPTZ NULL
  expires_at      TIMESTAMPTZ NULL
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now()

Indexes:
  ux_alerts_user_rule_day UNIQUE (user_id, rule_id, ((created_at AT TIME ZONE 'UTC')::date))
  ix_alerts_user_active   (user_id, severity DESC, created_at DESC) WHERE acknowledged_at IS NULL
  ix_alerts_user_all      (user_id, created_at DESC)
```

The `ux_alerts_user_rule_day` is a partial unique index but Postgres doesn't support partial here because `(user_id, rule_id, date)` is the natural identity. We rely on `ON CONFLICT DO NOTHING` semantics on insert.

## Endpoints

- `GET /api/alerts?activeOnly=true|false` — list alerts. Default `activeOnly = false`.
- `GET /api/alerts/{id}` — single alert detail.
- `PATCH /api/alerts/{id}/ack` — set `acknowledged_at`.
- `POST /api/alerts/_internal/run-now` — DEV-ONLY: trigger the BackgroundService evaluation manually. Not exposed in prod profile.

All require `RequireAuthorization` and `api-general` rate limit.

## Architecture

- **Shared.Kernel** owns `IAlertRule` interface + `AlertContext` record + `Alert` record + `AlertSeverity` enum (mirrors `Coaching.Severity`).
- **Trading.Application** owns `AlertRegistry` + 5 rules + `AlertEvaluationHandler` (per-user).
- **Trading.Infrastructure** owns `AlertRepository` + `AlertEvaluationBackgroundService` (singleton hosted service).
- **Shared.Infrastructure** registers `IAlertRule` instances as singletons (same pattern as `ICoachingRule`).
- **Host wiring** (`Program.cs`): `AddAlertBackgroundService()` adds the hosted service. `AddAlertRuleRegistry()` adds the singleton registry.
- **Cross-language**: `AlertContext` is a Trading-only struct (carries Trade + JournalEntry + closedPnL). `Alert` is the wire-shape record that flows out to the API.
