# Risk Profile Specification

## Purpose

Single-active, user-owned trading risk profile that captures capital, maximum tolerated drawdown, per-trade risk percentage and target risk-reward ratio. The profile is the authoritative input to the position-size calculator and the pre-trade checklist; it is the user's, never the admin's.

## Requirements

### Requirement: Single-active profile per user

The system MUST allow exactly one active risk profile per user at any time. A user MAY create a new profile only by superseding the active one in a single transaction. Two simultaneous active profiles for the same user MUST NOT exist and MUST be rejected by the persistence layer (partial unique index on `user_id WHERE is_active`).

#### Scenario: First profile creation

- GIVEN an authenticated user with no active risk profile
- WHEN `POST /api/risk-profile` succeeds
- THEN exactly one active profile MUST be persisted for that user
- AND the response MUST include the new profile id, capital, drawdown, risk-per-trade and RR target

#### Scenario: Superseding the active profile

- GIVEN an active profile exists for the user
- WHEN `PUT /api/risk-profile` is accepted
- THEN the previous active profile MUST be marked inactive in the same transaction
- AND the new profile MUST be the only active one for that user

#### Scenario: Concurrent supersede

- GIVEN two clients submit `PUT /api/risk-profile` for the same user
- WHEN both reach the persistence layer
- THEN exactly one MUST commit and the other MUST receive a `conflict` error
- AND the database MUST still hold exactly one active profile for that user

### Requirement: Validated profile fields

The system MUST validate every persisted field. `CapitalAmount` MUST be positive and stored as `NUMERIC(24,8)`. `CapitalCurrency` MUST be a 3-letter ISO 4217-like code. `MaxDrawdownPercent` MUST be in `[0.00, 50.00]`. `RiskPerTradePercent` MUST be in `[0.01, 5.00]`. `RiskRewardTarget` MUST be `≥ 1.0`. Out-of-range or malformed values MUST be rejected with a `validation` error that includes the field name.

#### Scenario: Out-of-range field

- GIVEN any field is outside its range (e.g. `RiskPerTradePercent = 7.5`)
- WHEN the request is validated
- THEN the system MUST return `422 Unprocessable Entity` with a `validation` error
- AND MUST NOT persist any partial profile

#### Scenario: Currency code shape

- GIVEN `CapitalCurrency` is not 3 uppercase letters
- WHEN the request is validated
- THEN the system MUST return a `validation` error
- AND MUST NOT persist the profile

### Requirement: Authenticated read and write

`GET /api/risk-profile` and `PUT /api/risk-profile` MUST require an authenticated Trader identity (RequireAuthorization; no Admin-only path, no anonymous read). The response MUST include only the requesting user's profile; cross-user reads MUST be impossible.

#### Scenario: Authenticated Trader reads profile

- GIVEN an authenticated Trader identity with an active profile
- WHEN `GET /api/risk-profile` is requested
- THEN the system MUST return the user's active profile or `404` if none exists
- AND MUST NOT include any other user's profile

#### Scenario: Unauthenticated or restricted-scope caller

- GIVEN an anonymous request or a forced-change / restricted-scope token
- WHEN `GET /api/risk-profile` or `PUT /api/risk-profile` is requested
- THEN the system MUST deny access before any lookup or persistence

### Requirement: PII and log redaction

The system MUST NOT log `CapitalAmount`, `CapitalCurrency`, or any other profile field at information level. Logs MUST remain at debug level or below for profile reads and writes, and the existing Wave 0 PII scrubber MUST cover profile payloads.

#### Scenario: Profile read or write logging

- GIVEN a profile read or write
- WHEN the handler logs the operation
- THEN only the user id and operation type MUST appear at info level
- AND no capital / currency / percentage values MUST leak

### Requirement: Cross-module read projection

Trading modules MUST read the active risk profile exclusively through the narrow `IIdentityUserRiskProfileReader` projection defined in `JadeCapital.Identity.Contracts.Projections`. The projection exposes only `CapitalAmount`, `CapitalCurrency`, `RiskPerTradePercent` and `RiskRewardTarget`. Trading MUST NOT depend on `JadeCapital.Identity.Domain`.

#### Scenario: Trading consumes the projection

- GIVEN Trading requests the active risk profile for a user
- WHEN the handler resolves the projection
- THEN the projection MUST return only the four allowed fields
- AND the Trading assembly MUST NOT reference `JadeCapital.Identity.Domain`