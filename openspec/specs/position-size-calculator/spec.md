# Position Size Calculator Specification

## Purpose

A read-only calculator that derives the recommended trade volume from the user's active risk profile, the entry price and the planned stop-loss price. The calculator is informational: the user can still override `RiskPerTrade` per trade, and the calculated volume is never enforced server-side.

## Requirements

### Requirement: Volume from risk profile

Given an active risk profile with `capitalAmount C` in `capitalCurrency`, `riskPerTradePercent p` (0.01 ≤ p ≤ 5.00), an entry price `E` and a stop price `S` (both in the same quote currency), the calculator MUST return `volume = (C × p / 100) / |E - S|` in base units of the instrument. When `|E - S| = 0` the calculator MUST return a `validation` error with `code = "validation:stop_too_close"`. When no active risk profile exists the calculator MUST return `404` and MUST NOT default any field.

#### Scenario: Valid inputs

- GIVEN `C = 10000`, `p = 1.0`, `E = 1.1000`, `S = 1.0950`
- WHEN the calculator runs
- THEN `volume` MUST equal `(10000 × 0.01) / |1.1000 - 1.0950| = 100 / 0.0050 = 20000`
- AND the response MUST echo `capitalAmount`, `riskPerTradePercent`, `entryPrice`, `stopPrice` and `quoteCurrency`

#### Scenario: Stop equal to entry

- GIVEN `E = 1.1000` and `S = 1.1000`
- WHEN the calculator runs
- THEN the response MUST be `422 Unprocessable Entity` with `code = "validation:stop_too_close"`
- AND MUST NOT return a volume

#### Scenario: No active risk profile

- GIVEN an authenticated user with no active risk profile
- WHEN the calculator is invoked
- THEN the response MUST be `404 Not Found`
- AND MUST NOT default `riskPerTradePercent` to any value

### Requirement: Risk-per-trade override

The calculator MUST accept an optional `riskPerTradePercent` override. When the override is present it MUST be validated against `[0.01, 5.00]` and MUST be used in place of the profile value. When the override is absent the profile value MUST be used. The override is never persisted to the profile.

#### Scenario: Valid override

- GIVEN the profile has `riskPerTradePercent = 1.0`
- WHEN the calculator is invoked with `riskPerTradePercentOverride = 2.0`
- THEN the response MUST be computed with `p = 2.0`
- AND the profile MUST remain unchanged

#### Scenario: Out-of-range override

- GIVEN a request with `riskPerTradePercentOverride = 7.5`
- WHEN the calculator validates the request
- THEN the response MUST be `422` with a `validation` error for `riskPerTradePercentOverride`
- AND the profile MUST remain unchanged

### Requirement: Money and rounding invariants

All arithmetic MUST use `decimal` (NUMERIC(24,8) on Postgres). The calculator MUST reject inputs whose absolute magnitude would overflow NUMERIC(24,8) (10^16 cap per `Money.MaxAmount`). The response MUST round `volume` to the instrument's `decimalPlaces` (read from `trading.instruments`) without changing the underlying decimal precision.

#### Scenario: Decimal precision

- GIVEN `C = 10000`, `p = 0.01`, `E = 1.12345`, `S = 1.12300`, instrument `decimalPlaces = 2`
- WHEN the calculator runs
- THEN the unrounded `volume` MUST be `(10000 × 0.01) / 0.00045 ≈ 222222.2222...`
- AND the response MUST round `volume` to 2 decimal places (`222222.22`)
- AND the unrounded value MUST NOT be persisted (only the rounded value is reported)

### Requirement: Authorization and isolation

The calculator MUST require an authenticated Trader identity. The calculator MUST read the user's own profile only (via `IIdentityUserRiskProfileReader`). Cross-user reads MUST be impossible.

#### Scenario: Authenticated Trader

- GIVEN an authenticated Trader identity
- WHEN the calculator is invoked
- THEN the request MUST be evaluated against the caller's profile only

#### Scenario: Unauthenticated or restricted-scope caller

- GIVEN an anonymous or restricted-scope caller
- WHEN the calculator is invoked
- THEN access MUST be denied before any lookup