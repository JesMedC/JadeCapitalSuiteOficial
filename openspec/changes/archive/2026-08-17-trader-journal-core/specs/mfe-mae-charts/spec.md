# MFE/MAE Charts Specification

## Purpose

Per-trade Maximum Favorable Excursion (MFE) and Maximum Adverse Excursion (MAE) measures how much the trade moved in the trader's favor (MFE) or against them (MAE) before being closed. Useful for understanding "did I leave money on the table?" and "could I have stopped out earlier?".

Wave 2 uses a deterministic approximation from the trade's own `entry_price`, `exit_price`, and `pnl_amount` (no external market data provider). Wave 4 will replace with real tick-level data.

## Requirements

### Requirement: Per-trade MFE and MAE

The system MUST compute and persist `mfe_amount` and `mae_amount` for each closed trade on close. For Wave 2, the calculation uses a deterministic approximation rule. The values MUST be `NUMERIC(24,8)` (money), nullable (open trades have no MFE/MAE).

#### Scenario: Winning trade

- GIVEN a closed trade with `entry_price = 1.0850`, `exit_price = 1.0900`, `pnl_amount = +0.005` per unit, direction = Long
- WHEN the calculator runs
- THEN `mfe_amount = 0.005` (MFE ≥ P&L for winners), `mae_amount = 0` or negative (MAE ≤ 0; approximation cannot know the low without tick data)
- AND both fields MUST be persisted on the trade row

#### Scenario: Losing trade

- GIVEN a closed trade with `entry_price = 1.0900`, `exit_price = 1.0850`, `pnl_amount = -0.005` per unit, direction = Long
- WHEN the calculator runs
- THEN `mae_amount = -0.005` (MAE ≤ P&L for losers in magnitude), `mfe_amount = 0` or positive (approximation cannot know the high without tick data)
- AND both fields MUST be persisted

### Requirement: MFE/MAE history endpoint

`GET /api/trades/{tradeId}/mfe-mae` MUST return the per-trade MFE/MAE plus the user's aggregate histograms for `direction × {winners, losers}`.

#### Scenario: Closed trade MFE/MAE read

- GIVEN a closed trade with persisted MFE/MAE
- WHEN `GET /api/trades/{id}/mfe-mae` is requested
- THEN the response MUST include `mfeAmount`, `maeAmount`, `direction`, and the user's aggregate histograms
- AND HTTP 200

#### Scenario: Open trade MFE/MAE

- GIVEN an open trade
- WHEN `GET /api/trades/{id}/mfe-mae` is requested
- THEN the response MUST return `null` for both fields with HTTP 200 (MFE/MAE only meaningful post-close)

#### Scenario: Cross-user isolation

- GIVEN user A's trade id
- WHEN user B requests the MFE/MAE
- THEN the response MUST be 404 (NOT 403, to avoid leaking trade existence)

### Requirement: MFE/MAE recomputation on update

When a trade's `exit_price` or `pnl_amount` changes (e.g. an adjustment), the system MUST recompute MFE/MAE atomically in the same transaction. The values MUST be the latest persisted values when read.

#### Scenario: Trade correction updates MFE/MAE

- GIVEN an admin or owner updates exit_price from `1.0850` to `1.0900`
- WHEN the update completes
- THEN `mfe_amount` MUST reflect the new `pnl_amount`

### Requirement: Aggregated histograms

The endpoint MUST return aggregate histograms of MFE and MAE magnitudes across the user's history, bucketed by `direction` (Long/Short) × `outcome` (winner/loser). Buckets: `mfe_long_winners`, `mfe_long_losers`, `mfe_short_winners`, `mfe_short_losers`, and same four for MAE.

#### Scenario: Empty history returns empty histograms

- GIVEN a user with 0 closed trades
- WHEN `GET /api/trades/{id}/mfe-mae` is requested (any tradeId, even invalid → 404)
- AND the user has no closed trades
- THEN aggregate histograms MUST contain 0 entries with 0 count

## Data Model (migration 0014)

```
ALTER TABLE trading.trades
  ADD COLUMN IF NOT EXISTS mfe_amount NUMERIC(24,8) NULL,
  ADD COLUMN IF NOT EXISTS mae_amount NUMERIC(24,8) NULL,
  ADD COLUMN IF NOT EXISTS mfe_currency CHAR(3) NULL,
  ADD COLUMN IF NOT EXISTS mae_currency CHAR(3) NULL;
```

Idempotent and additive. Nullable: open trades have no MFE/MAE.

## Approximation Algorithm (deterministic, Wave 2 only)

```python
direction_is_long = (trade.direction == 'Long')
is_winner = (trade.pnl_amount > 0)
exit_beat_entry = (trade.exit_price > trade.entry_price)  # Long: favorable
                                                    # Short: unfavorable

if direction_is_long:
    favorable_price_diff = exit_price - entry_price
    adverse_price_diff   = entry_price - exit_price
else:  # Short
    favorable_price_diff = entry_price - exit_price
    adverse_price_diff   = exit_price - entry_price

# Wave 2 approximation: assume price moved monotonically.
# In reality, price oscillates. Without tick data we use:
# - If exit was favorable, MFE = P&L amount (best case)
# - If exit was adverse, MAE = |P&L amount|
# - The opposite field is set to 0 (unknown).

if exit_beat_entry if direction_is_long else not exit_beat_entry:
    mfe_amount = max(pnl_amount, 0)  # winner MFE
    mae_amount = 0                    # approximation
else:
    mae_amount = -abs(pnl_amount) if pnl_amount < 0 else 0  # loser MAE
    mfe_amount = 0                    # approximation
```

## Endpoint

- `GET /api/trades/{tradeId}/mfe-mae` — returns `{ tradeId, direction, isWinner, mfeAmount, maeAmount, currency, aggregate: {...} }`.
- `api-general` rate limit. RequireAuthorization.
