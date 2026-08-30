# Strategies Specification

## Purpose

User-owned named strategies that describe the trader's setup: which instrument, on which timeframe, with what rules. Each trade MAY be tagged with at most one strategy; the analytics aggregate per strategy (count, winRate, totalPnl, expectancy, profitFactor) lets the trader identify which setups have edge.

Strategies are the substrate for the analytics in Wave 3a and feed `CurrentPriceNearStopRule` (Wave 3b) indirectly (via trades linkeados). The `Strategy` aggregate is independent of Trade (no navigation properties) — the link is a nullable FK on the Trade side.

## Requirements

### Requirement: One strategy per user per unique name

The system MUST allow at most one **active** strategy per `(user_id, name)` pair. Name is case-insensitive trimmed (e.g. "London Break" == "london break"). If a strategy is soft-deleted (`is_active = false`), a new one with the same name MAY be created (audit trail). Two simultaneous active strategies with the same name for the same user MUST NOT exist (partial unique index `ux_strategies_user_name_active`).

#### Scenario: First strategy created

- GIVEN an authenticated user with no strategies
- WHEN `POST /api/strategies` succeeds with `name = "London Break"`
- THEN exactly one active strategy MUST be persisted for that user and name
- AND the response MUST include the new strategy's id, instrument, timeframe, rules

#### Scenario: Duplicate name rejected

- GIVEN user A has an active strategy named "London Break"
- WHEN user A calls `POST /api/strategies` with `name = "London Break"`
- THEN the system MUST return `409 Conflict` with `strategy.duplicate_name`
- AND MUST NOT create a second row

#### Scenario: Cross-user isolation

- GIVEN user A has a strategy named "London Break"
- WHEN user B calls `GET /api/strategies`
- THEN the response MUST return ONLY user B's strategies (empty list, not user A's)

### Requirement: Tag a trade with a strategy

A user MAY assign at most one strategy to a trade via `PATCH /api/trades/{tradeId}/strategy` with body `{ strategyId: Guid }`. The trade MUST belong to the same user; the strategy MUST belong to the same user; the strategy MUST be active; the trade MAY be Open or Closed (post-close tagging is allowed for late annotations).

#### Scenario: Tag open trade

- GIVEN an open trade belonging to user A
- AND an active strategy belonging to user A
- WHEN `PATCH /api/trades/{id}/strategy { strategyId: ... }` succeeds
- THEN `trading.trades.strategy_id` MUST be set to that strategy id
- AND `updated_at` MUST be bumped

#### Scenario: Tag with foreign strategy rejected

- GIVEN user A's trade and user B's strategy
- WHEN `PATCH /api/trades/{id}/strategy` is called
- THEN the system MUST return `404 Not Found` (strategy doesn't belong to user A's namespace)
- AND MUST NOT mutate the trade

#### Scenario: Untag a trade

- GIVEN a trade with `strategy_id` set
- WHEN `PATCH /api/trades/{id}/strategy { strategyId: null }` is called
- THEN `trading.trades.strategy_id` MUST be set to NULL
- AND the trade remains otherwise intact

### Requirement: Soft-delete (set is_active=false)

`DELETE /api/strategies/{id}` MUST soft-delete the strategy by setting `is_active = false`. The strategy is filtered out of `GET /api/strategies` listing, but existing trades that reference it retain the FK (no cascade). Re-creating a strategy with the same name after soft-delete MUST be allowed.

#### Scenario: Soft-delete

- GIVEN user A has an active strategy "London Break"
- WHEN `DELETE /api/strategies/{id}` is called
- THEN the strategy's `is_active` MUST be `false`
- AND `GET /api/strategies` MUST NOT include it
- AND existing trades with `strategy_id = id` MUST retain the FK (no breakage)

#### Scenario: Re-create after soft-delete

- GIVEN user A soft-deleted "London Break"
- WHEN `POST /api/strategies { name: "London Break" }` is called
- THEN the system MUST create a new strategy (id is new)
- AND the user now has 2 rows in `trading.strategies` for the same name (one inactive, one active)

### Requirement: Strategy analytics aggregate

`GET /api/strategies/{id}/analytics` MUST return the following aggregate computed on-read from the trades linkeados:

```json
{
  "strategyId": "...",
  "tradeCount": 27,
  "winCount": 16,
  "lossCount": 11,
  "winRate": 0.5926,
  "totalPnl": "1234.56",
  "currency": "USD",
  "expectancy": "45.72",
  "profitFactor": "1.43",
  "avgMfe": "78.30",
  "avgMae": "-22.10",
  "lastTradeAt": "2026-08-15T14:32:00Z"
}
```

Formulas:
- `winRate = winCount / tradeCount` (decimal 0..1, 4 decimals).
- `expectancy = totalPnl / tradeCount` (per-trade average).
- `profitFactor = sum(winning_pnl) / abs(sum(losing_pnl))` (decimal 2 decimals; null if no losses).
- `avgMfe = mean(mfe_amount)` excluding nulls (decimal 2 decimals).
- `avgMae = mean(mae_amount)` excluding nulls (decimal 2 decimals; typically negative).

Only **closed** trades count. Open trades are excluded from analytics.

#### Scenario: Analytics with mixed winners/losers

- GIVEN a strategy with 10 closed trades: 6 winners (+500 total), 4 losers (-200 total), MFE avg 60, MAE avg -15
- WHEN `GET /api/strategies/{id}/analytics` is called
- THEN the response MUST show `winRate: 0.60, totalPnl: "300.00", expectancy: "30.00", profitFactor: "2.50", avgMfe: "60.00", avgMae: "-15.00"`

#### Scenario: Empty strategy (no trades)

- GIVEN a strategy with 0 closed trades
- WHEN `GET /api/strategies/{id}/analytics` is called
- THEN the response MUST show `tradeCount: 0, winRate: 0, totalPnl: "0.00", expectancy: "0.00", profitFactor: null, avgMfe: null, avgMae: null`

#### Scenario: All losers (no winners)

- GIVEN a strategy with 5 closed trades, all losers (-300 total)
- WHEN `GET /api/strategies/{id}/analytics` is called
- THEN `profitFactor` MUST be `0` (convention: no winners → 0; cannot divide by zero)

### Requirement: Validated lengths

`name` MUST be 1..64 chars (trimmed). `description` MUST be ≤ 1000 chars (nullable). `rules` MUST be ≤ 2000 chars (nullable). `symbol` MUST be either NULL (multi-symbol strategy) or a valid `Symbol` (referenced in `trading.instruments.symbol`). `timeframe` MUST be one of `M1, M5, M15, M30, H1, H4, D1, W1, MN` or NULL.

#### Scenario: Oversized name

- GIVEN a strategy with `name` length 65
- WHEN the request is validated
- THEN the system MUST return `422 Unprocessable Entity` with `strategy.name_too_long`

#### Scenario: Invalid timeframe

- GIVEN a strategy with `timeframe = "SECOND"`
- WHEN the request is validated
- THEN the system MUST return `422` with `strategy.invalid_timeframe`

### Requirement: Authenticated CRUD

All endpoints (`GET /api/strategies`, `GET /api/strategies/{id}`, `POST /api/strategies`, `PATCH /api/strategies/{id}`, `DELETE /api/strategies/{id}`, `GET /api/strategies/{id}/analytics`, `PATCH /api/trades/{tradeId}/strategy`, `GET /api/trades/{tradeId}/strategy`) MUST require an authenticated Trader identity. The response MUST include only the requesting user's strategies; cross-user reads MUST be impossible.

#### Scenario: Unauthenticated caller

- GIVEN an anonymous request
- WHEN any strategy endpoint is called
- THEN the system MUST return 401

## Data Model

```
trading.strategies
  id          UUID PK
  user_id     UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE
  name        VARCHAR(64) NOT NULL
  description VARCHAR(1000) NULL
  symbol      VARCHAR(20) NULL           -- nullable: multi-symbol strategy
  timeframe   SMALLINT NULL              -- 0..8 mapped to Timeframe enum
  rules       TEXT NULL                  -- <= 2000 chars, free-text
  is_active   BOOLEAN NOT NULL DEFAULT TRUE
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT now()

Indexes:
  ux_strategies_user_name_active UNIQUE (user_id, lower(name)) WHERE is_active = true
  ix_strategies_user_active     (user_id, is_active) WHERE is_active = true

trading.trades.strategy_id (FK additive nullable)
  strategy_id UUID NULL REFERENCES trading.strategies(id) ON DELETE SET NULL
  Index: ix_trades_user_strategy (user_id, strategy_id) WHERE strategy_id IS NOT NULL
```

`ON DELETE SET NULL` on the FK: if a strategy is hard-deleted (admin operation), trades keep the row but lose the link. Wave 3 only does soft-delete; this is a safety net.

## Endpoints

- `GET /api/strategies` — list active strategies (paginated, default 50).
- `GET /api/strategies/{id}` — single strategy detail.
- `POST /api/strategies` — create. Body: `{ name, description?, symbol?, timeframe?, rules? }`.
- `PATCH /api/strategies/{id}` — update any field. `name` change re-checks uniqueness.
- `DELETE /api/strategies/{id}` — soft-delete (`is_active = false`).
- `GET /api/strategies/{id}/analytics` — aggregate metrics.
- `PATCH /api/trades/{tradeId}/strategy` — `{ strategyId: Guid | null }`. (Defined in trade endpoints but enforced here.)

All require `RequireAuthorization` and `api-general` rate limit.

## Architecture

- **Trading.Domain** owns `Strategy` aggregate + `Timeframe` enum (in Shared.Kernel).
- **Trading.Application** owns handlers + `IStrategyRepository`.
- **Trading.Infrastructure** owns EF configuration + `StrategyRepository`.
- **Shared.Kernel** owns `Timeframe` enum (cross-module enum).
- **Trade aggregate** gains `StrategyId?` property (additive, no behavior change).
- **Analytics** is computed on read in `GetStrategyAnalyticsHandler` — no precomputation, no caching in Wave 3.
