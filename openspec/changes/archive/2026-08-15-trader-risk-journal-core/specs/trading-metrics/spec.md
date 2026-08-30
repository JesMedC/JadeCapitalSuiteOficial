# Trading Metrics Specification

## Purpose

Server-side trading analytics for the authenticated user. The dashboard summary, expectancy, profit factor, payoff, SQN, max drawdown, equity curve, drawdown overlay and per-symbol stats are computed from `trading.trades` on every request — never mocked client-side.

## Requirements

### Requirement: Server-side metrics endpoint

The system MUST expose `GET /api/trades/metrics?period={7d|30d|90d|all}` that returns the metrics for the authenticated user over the requested window. The response MUST include every field listed below computed only from trades where `user_id = caller` and (when `period` is not `all`) `opened_at >= now - period`.

#### Scenario: Authenticated user with closed trades

- GIVEN an authenticated user with at least one closed trade in the last 30 days
- WHEN `GET /api/trades/metrics?period=30d` is requested
- THEN the response MUST include `expectancy`, `profitFactor`, `payoff`, `sqn`, `maxDrawdown`, `equityCurve[]`, `drawdownOverlay[]`, `symbolStats[]`, `closedTrades`, `openTrades`, `winRate`
- AND all numbers MUST be computed from the user's trades only

#### Scenario: Empty period

- GIVEN an authenticated user with zero trades in the window
- WHEN the metrics endpoint is requested
- THEN the response MUST return zero-valued fields, an empty `equityCurve`, an empty `drawdownOverlay` and an empty `symbolStats`
- AND MUST NOT return 404 or any error

#### Scenario: All-open period

- GIVEN an authenticated user whose window contains only `Open` trades
- WHEN the metrics endpoint is requested
- THEN closed-only metrics (expectancy, profit factor, payoff, SQN, win rate, max drawdown) MUST be zero
- AND `openTrades` MUST reflect the open count

### Requirement: Expectancy, profit factor and payoff

Expectancy MUST be `(grossWins - grossLosses) / closedTrades` over closed trades in the window. Profit factor MUST be `grossWins / grossLosses` when `grossLosses > 0`, otherwise zero. Payoff ratio MUST be `avgWin / avgLoss` when `avgLoss > 0`, otherwise zero. `grossWins` and `grossLosses` MUST be the sum of positive and absolute sum of negative realized PnL, respectively, in account currency.

#### Scenario: Expectancy with wins and losses

- GIVEN a window with closed trades summing to wins=600 and losses=400 across 10 trades
- WHEN the metrics are computed
- THEN expectancy MUST equal `(600 - 400) / 10 = 20.00`
- AND profit factor MUST equal `600 / 400 = 1.50`

#### Scenario: Zero losses edge case

- GIVEN a window with only winning trades
- WHEN the metrics are computed
- THEN profit factor and payoff MUST be reported as zero (not infinity, not `null`)
- AND expectancy MUST equal `grossWins / closedTrades`

### Requirement: SQN and max drawdown

SQN (System Quality Number) MUST equal `sqrt(closedTrades) * avgPnL / stdDevPnL` when `closedTrades >= 2` and `stdDevPnL > 0`; otherwise zero. Max drawdown MUST be the largest peak-to-trough drop of the running equity curve (cumulative closed PnL) within the window, returned as a non-positive number. Both MUST be deterministic given the same trade sequence.

#### Scenario: SQN computation

- GIVEN a window with closed trades producing `avgPnL = 25`, `stdDevPnL = 10`, `closedTrades = 16`
- WHEN the metrics are computed
- THEN `sqn` MUST equal `sqrt(16) * 25 / 10 = 10.00`

#### Scenario: Max drawdown with peaks and troughs

- GIVEN an equity curve `[0, 100, 50, 200, 80]`
- WHEN the metrics are computed
- THEN `maxDrawdown` MUST equal `-120` (the drop from 200 to 80)

### Requirement: Equity curve, drawdown overlay and symbol stats

`equityCurve[]` MUST be the running cumulative closed PnL sampled at every closed trade in the window, in `opened_at` order. `drawdownOverlay[]` MUST be the per-point underwater equity (`equityCurve[i] - max(equityCurve[0..i])`), all non-positive. `symbolStats[]` MUST group closed trades by symbol and report `trades`, `wins`, `winRate`, `netPnl`, `bestTrade`, `worstTrade` per symbol, sorted by `netPnl` descending.

#### Scenario: Symbol stats grouping

- GIVEN closed trades on EURUSD (3 wins, 1 loss) and BTCUSDT (1 win, 2 losses)
- WHEN the metrics are computed
- THEN `symbolStats[0]` MUST be the symbol with the higher `netPnl`
- AND every reported symbol MUST have its own win rate computed over its own closed trades

### Requirement: Authorization and isolation

The metrics endpoint MUST require an authenticated Trader identity and MUST scope every query to `user_id = caller`. No metrics endpoint may be reached anonymously, via restricted-scope token, or via Admin role without a user filter. The response MUST NOT include any other user's trade data even if the caller tries to influence the query string.

#### Scenario: Cross-user isolation

- GIVEN two users with disjoint trade histories
- WHEN user A requests metrics
- THEN only A's trades MUST be reflected in the response
- AND no field, count or curve point from user B MUST appear