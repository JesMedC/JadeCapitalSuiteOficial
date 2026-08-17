# Behavioral Analytics Specification

## Purpose

Server-side detection of trading behavior patterns that historically correlate with poor risk-adjusted returns: revenge trading, overtrading, tilt sequences, overconfidence after wins, and emotionality-tag aggregation. The output drives both the `/app/patterns` dashboard (this Wave) and coaching prompts (Wave 2d).

## Requirements

### Requirement: Five detection rules

The system MUST evaluate the user's closed trade history against five rules on demand. Each rule produces zero or more `BehavioralEvent` records with `RuleId`, severity, and human-readable description.

#### Scenario: Revenge trading after a loss

- GIVEN a closed losing trade followed within 30 minutes by a trade whose volume is ≥ 1.5× the losing trade's volume, in the same instrument class
- WHEN the analyzer runs
- THEN it MUST emit one `RevengeTrade` event with severity `medium` and message "Operación X con tamaño 1.5× la anterior perdedora — posible revenge trading"

#### Scenario: Overtrading day

- GIVEN a calendar day with N ≥ 12 closed trades where the user's 30-day daily mean is ≤ 5 trades/day
- WHEN the analyzer runs
- THEN it MUST emit one `OvertradingDay` event per qualifying day with severity proportional to (N / 30-day mean)

#### Scenario: Tilt sequence (3 consecutive losses)

- GIVEN 3 consecutive closed losing trades within a 90-minute window
- WHEN the analyzer runs
- THEN it MUST emit one `TiltSequence` event with severity `high`

#### Scenario: Overconfidence after wins (size escalation)

- GIVEN 2 consecutive closed winning trades followed by a trade with volume ≥ 2× the prior winner, in the same instrument class
- WHEN the analyzer runs
- THEN it MUST emit one `OverconfidenceAfterWin` event with severity `medium`

#### Scenario: Emotionality-tag aggregation

- GIVEN closed trades linked to pre-trade checklists with `emotionality` values
- WHEN the analyzer runs
- THEN it MUST return aggregated P&L grouped by emotionality bucket (1-2, 3, 4-5) and win-rate per bucket

### Requirement: Time-windowed queries

The endpoint MUST accept a `period` query parameter (`7d`, `30d`, `90d`, `all`) defaulting to `30d`. The analyzer MUST only consider trades whose `closed_at` falls within the window.

#### Scenario: Period filter 7d

- GIVEN trades A (closed 60 days ago) and B (closed 3 days ago), both qualifying for revenge trading
- WHEN `GET /api/trades/behavioral?period=7d` is requested
- THEN only B's events MUST be returned

### Requirement: Authenticated read

`GET /api/trades/behavioral?period=...` MUST require an authenticated Trader identity. The response MUST include only the requesting user's events; cross-user reads MUST be impossible.

#### Scenario: Unauthenticated caller

- GIVEN an anonymous request
- WHEN `GET /api/trades/behavioral` is called
- THEN the system MUST return `401 Unauthorized`

### Requirement: Deterministic, stateless evaluation

The analyzer MUST be a pure function of the trade history. The system MUST NOT persist `BehavioralEvent` rows in Wave 2 (deferred to Wave 3 if useful for trending); events are computed on read. The implementation MUST be deterministic: same input ⇒ same output.

#### Scenario: Repeat call returns same events

- GIVEN the user calls `GET /api/trades/behavioral?period=30d` twice with no intervening trades
- WHEN both calls complete
- THEN the response payload MUST be identical (modulo timestamps in the wrapper)

### Requirement: Empty history returns empty list

- GIVEN a user with no closed trades in the window
- WHEN the analyzer runs
- THEN the response MUST return `{ events: [], aggregations: { ...zeros... } }` with HTTP 200

## Data Model

No new schema in Wave 2 — events are computed on read. Wave 3 may introduce `trading.behavioral_events` for trending.

## Output Shape

```json
{
  "period": "30d",
  "userId": "...",
  "windowStart": "2026-07-17T00:00:00Z",
  "windowEnd":   "2026-08-17T00:00:00Z",
  "events": [
    {
      "ruleId": "RevengeTrade",
      "severity": "medium",
      "tradeIds": ["...", "..."],
      "occurredAt": "2026-08-12T14:32:00Z",
      "message": "Operación ... con tamaño 1.5× la anterior perdedora — posible revenge trading"
    }
  ],
  "aggregations": {
    "byEmotionality": {
      "low_1_2":  { "count": 12, "winRate": 0.33, "totalPnl": "-150.00" },
      "mid_3":    { "count": 28, "winRate": 0.61, "totalPnl": "+420.50" },
      "high_4_5": { "count": 8,  "winRate": 0.50, "totalPnl": "-80.20" }
    }
  }
}
```

## Endpoint

- `GET /api/trades/behavioral?period=7d|30d|90d|all` — returns the object above.
- `api-general` rate limit. RequireAuthorization.

## Thresholds (configurable constants in domain layer)

- Revenge ratio: 1.5×
- Revenge cooldown: 30 minutes
- Overtrading threshold: 12 trades/day with baseline ≤ 5
- Tilt window: 3 losses in 90 minutes
- Overconfidence ratio: 2.0×
- Overconfidence streak: 2 wins
