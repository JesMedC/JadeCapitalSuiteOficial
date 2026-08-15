# Subscription Administration Specification

## Purpose

Billing-owned subscription administration exposed through a narrowly authorized Admin API and UI.

## Requirements

### Requirement: Billing ownership and history

Billing MUST own authoritative plans, subscriptions, state transitions, and append-only history. Admin MUST only authorize requests and delegate subscription operations; PublicPortal pricing MUST remain non-authoritative marketing content.

#### Scenario: Successful mutation
- GIVEN a valid subscription and permitted transition
- WHEN an Admin changes tier, cancels, or extends a trial
- THEN Billing MUST atomically update authoritative state
- AND append history containing prior state, resulting state, action, actor, and timestamp

#### Scenario: Concurrent mutation
- GIVEN two Admins mutate the same subscription version
- WHEN both operations commit
- THEN exactly one MUST succeed and the stale operation MUST return a conflict
- AND history MUST contain only committed transitions in deterministic order

### Requirement: Valid subscription transitions

The system MUST permit tier changes only to an existing eligible plan, cancellation only from a cancellable state, and trial extension only for an active trial with a valid future end. Repeated cancellation and invalid/no-op transitions MUST NOT create duplicate history.

#### Scenario: Invalid transition
- GIVEN a missing plan, non-trial extension, expired/invalid date, cancelled subscription, or no-op request
- WHEN mutation is attempted
- THEN Billing MUST reject it without changing state or history

#### Scenario: History retrieval
- GIVEN committed transitions exist
- WHEN detail is requested
- THEN current state and complete history MUST be returned in stable newest-first order

### Requirement: Admin-only authorization

Every subscription administration API and route MUST require authenticated Admin authorization. Trader, anonymous, forced-change, suspended, or otherwise unauthorized identities MUST receive denial without subscription data or mutation side effects.

#### Scenario: Authorized Admin
- GIVEN an authenticated eligible Admin
- WHEN list, search, detail, tier change, cancellation, trial extension, or history is requested
- THEN the operation MUST be evaluated by Billing

#### Scenario: Unauthorized caller
- GIVEN any non-Admin or forced-change identity
- WHEN an Admin route or endpoint is requested
- THEN access MUST be denied before lookup or mutation
- AND no existence, owner, plan, or history information MUST leak

### Requirement: Narrow administration scope

Admin MUST expose only subscription list/search, detail/history, tier change, cancellation, and trial extension. It MUST NOT expose role changes, suspension/reactivation, impersonation, general user management, or Admin-triggered password reset.

#### Scenario: Prohibited operation
- GIVEN an Admin identity
- WHEN a prohibited identity-management operation is sought through Wave 0 surfaces
- THEN no route, control, or API capability MUST exist for it

### Requirement: Subscription list and detail experience

The Admin UI MUST preserve Angular 19 strict TypeScript, Signals, OnPush, and SCSS conventions. List/search and detail/history views MUST provide loading, error/retry, empty, populated, stale-conflict, and mutation-pending states without showing stale success.

#### Scenario: Loading, empty, and error states
- GIVEN list or detail data is pending, empty, missing, or failed
- WHEN the view renders
- THEN it MUST show the corresponding non-overlapping state
- AND errors MUST offer a safe retry while missing detail MUST not render prior data

#### Scenario: Mutation feedback
- GIVEN an Admin submits a valid or stale mutation
- WHEN the request is pending or completes
- THEN duplicate controls MUST be disabled and status announced
- AND success MUST refresh state/history while conflict MUST preserve input for review

### Requirement: Accessible responsive administration

Admin views MUST support desktop, tablet, and mobile use; labeled controls; logical keyboard order; visible focus; announced validation/status; and tables that remain understandable through responsive presentation.

#### Scenario: Keyboard and narrow viewport
- GIVEN keyboard-only use at a narrow viewport
- WHEN list, detail, or mutation controls are operated
- THEN all information and actions MUST remain reachable without horizontal page loss
- AND focus MUST move predictably after navigation, errors, and confirmations

### Requirement: Portal and architecture regressions

The change MUST preserve the .NET 10 modular-monolith dependency direction and MUST NOT alter PublicPortal or existing Trader domain behavior. Normal Trader users MUST retain existing journeys but MUST never gain Admin subscription access.

#### Scenario: Regression boundary
- GIVEN PublicPortal, normal Trader, or existing Trading APIs
- WHEN Wave 0 subscription administration is enabled
- THEN their existing content and behavior MUST remain unchanged
- AND Billing state MUST not be derived from PublicPortal pricing copy
