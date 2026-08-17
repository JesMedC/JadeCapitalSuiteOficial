# Pre-Trade Checklist Specification

## Purpose

A pre-trade checklist is submitted alongside `OpenTrade` to confirm emotional state, setup quality, risk-reward expectation and confluence count. A failing checklist blocks the trade with a 422 carrying field-level reasons; an absent checklist preserves the legacy OpenTrade path.

## Requirements

### Requirement: Optional checklist on OpenTrade

`OpenTradeCommand` MUST accept an optional `PreTradeChecklistSubmission?` payload. When the payload is `null`, `OpenTrade` MUST behave exactly as before — no checklist persistence, no extra validation, no 422. When the payload is present, the handler MUST build a `PreTradeChecklist` aggregate, validate it, and either persist it together with the trade or reject the whole command.

#### Scenario: OpenTrade without checklist

- GIVEN an authenticated user submits a valid `OpenTradeRequest` without a checklist
- WHEN the handler runs
- THEN the trade MUST be opened exactly as today
- AND no row MUST be written to `trading.pre_trade_checklists`

#### Scenario: OpenTrade with valid checklist

- GIVEN an authenticated user submits a valid checklist payload (emotionality neutral, setup strong, RR ≥ target, confluences ≥ 3)
- WHEN the handler validates the checklist
- THEN the trade MUST be opened
- AND one `trading.pre_trade_checklists` row MUST be persisted with `trade_id` FK and the four submitted values

#### Scenario: OpenTrade with invalid checklist

- GIVEN an authenticated user submits a checklist with `confluencesCount = 1` and a poor `setupQuality`
- WHEN the handler validates the checklist
- THEN the trade MUST NOT be opened
- AND the response MUST be `422 Unprocessable Entity` with a `validation` error containing one entry per failing field
- AND no row MUST be persisted in either `trading.trades` or `trading.pre_trade_checklists`

### Requirement: Checklist field validation

`Emotionality` MUST be one of `Calm | Anxious | Neutral | Excited | Tilted`. `SetupQuality` MUST be one of `A | B | C | D` (A = highest quality). `RiskRewardAtEntry` MUST be `≥` the user's active risk profile `RiskRewardTarget` when a profile exists; if no profile exists the checklist MUST still be accepted when `RiskRewardAtEntry >= 1.0`. `ConfluencesCount` MUST be in `[1, 10]`. Failing any field MUST produce a 422.

#### Scenario: Setup quality out of enum

- GIVEN a checklist with `setupQuality = "Z"`
- WHEN the checklist is validated
- THEN the response MUST include a `validation` error for `setupQuality`
- AND the trade MUST NOT be opened

#### Scenario: Risk-reward below target

- GIVEN an active risk profile with `RiskRewardTarget = 2.0` and a checklist with `riskRewardAtEntry = 1.5`
- WHEN the checklist is validated
- THEN the response MUST include a `validation` error for `riskRewardAtEntry`
- AND the trade MUST NOT be opened

#### Scenario: Confluences below minimum

- GIVEN a checklist with `confluencesCount = 0`
- WHEN the checklist is validated
- THEN the response MUST include a `validation` error for `confluencesCount`
- AND the trade MUST NOT be opened

### Requirement: Cross-module profile lookup at OpenTrade

When a checklist is submitted, the handler MUST read the user's active risk profile through `IIdentityUserRiskProfileReader`. The handler MUST NOT take a hard dependency on `JadeCapital.Identity.Domain`. If the projection cannot find an active profile, the handler MUST skip the RR-vs-target check (treating `target = 1.0`) and MUST persist a `PreTradeChecklistSubmittedDomainEvent` with `hadActiveProfile = false`.

#### Scenario: No active risk profile

- GIVEN an authenticated user with no active risk profile submits a checklist with `riskRewardAtEntry = 1.5`
- WHEN the handler validates the checklist
- THEN the trade MUST be opened (assuming all other fields pass)
- AND the persisted checklist MUST record `risk_reward_target_used = 1.0`

### Requirement: Persistence and events

On a successful checklist submission the handler MUST persist the checklist row with `trade_id` FK, `user_id`, `submitted_at`, `emotionality`, `setup_quality`, `risk_reward_at_entry`, `risk_reward_target_used`, `confluences_count`. The handler MUST raise a `PreTradeChecklistSubmittedDomainEvent` carrying those values (without PII).

#### Scenario: Successful checklist persistence

- GIVEN a valid checklist submitted with `OpenTrade`
- WHEN the handler commits
- THEN the checklist row MUST exist with the FK to the new trade
- AND exactly one `PreTradeChecklistSubmittedDomainEvent` MUST be raised

### Requirement: Authorization and isolation

The checklist endpoint path MUST require an authenticated Trader identity and MUST scope the FK and lookup to the caller's user id. Cross-user checklist writes MUST be impossible.

#### Scenario: Cross-user write attempt

- GIVEN an authenticated user submits a checklist referencing another user's `tradeId`
- WHEN the handler runs
- THEN the response MUST be `404 Not Found`
- AND no row MUST be persisted