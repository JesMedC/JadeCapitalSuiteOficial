# Delta for CI Infrastructure

## ADDED Requirements

### Requirement: Playwright gate

CI MUST gate five Playwright journeys on migrated real services; any failure MUST fail CI.

#### Scenario: Registration with consent
- GIVEN accepted consent and an unregistered email
- WHEN UI registration succeeds
- THEN the account MUST persist and authenticate

#### Scenario: Login and session
- GIVEN an active account
- WHEN UI login succeeds and reloads
- THEN its session MUST remain authenticated

#### Scenario: Create, list, and open trade
- GIVEN an authenticated account
- WHEN its trade is created, listed, and opened
- THEN persisted values MUST match in detail

#### Scenario: Authenticated GDPR export
- GIVEN an authenticated account with data
- WHEN UI export succeeds
- THEN the download MUST contain its data

#### Scenario: Account deletion grace period
- GIVEN an authenticated active account
- WHEN UI deletion is confirmed
- THEN pending status and grace boundary MUST appear
