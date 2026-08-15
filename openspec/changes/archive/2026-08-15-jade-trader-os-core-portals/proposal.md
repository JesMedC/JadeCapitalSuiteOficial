# Proposal: Jade Trader OS Core Portals — Wave 0

## Intent and Problem

Establish foundations for later waves. Password recovery is absent, subscriptions lack an owner, and Admin is a placeholder. Wave 0 closes these gaps without rewriting Trader or changing PublicPortal.

## Goals

- Provide password recovery, forced rotation, and session revocation.
- Make Billing authoritative for subscriptions.
- Restrict Admin to subscription administration.
- Protect PublicPortal and Trader with regressions.

## Scope Boundaries

**Changed:** Identity lifecycle/auth UI; Billing subscriptions; thin Admin API/UI; Host composition; SMTP.

**Unchanged:** Angular 19, strict TypeScript, Signals, OnPush, SCSS; .NET 10 modular monolith/Clean Architecture; PublicPortal; Trading and existing Trader features.

## Capabilities

### New Capabilities
- `identity-password-recovery`: Email a single-use, 24-hour temporary password; force change before portal access; reject the previous five passwords on every change; include temporary failures in the five-attempt lockout; revoke sessions after change; never log plaintext credentials. V1 email is Spanish-only, Jade-branded, neutral security copy. Use dedicated throttling.
- `subscription-administration`: Billing-owned plans, state, and history; Admin-only search/list, detail, tier change, cancellation, and trial extension.

### Modified Capabilities
- None; no baseline OpenSpec capabilities exist.

## Ownership and Approach

Identity owns credentials. Shared infrastructure supplies MailKit SMTP, Mailpit locally, and an in-memory test sender. Billing owns commercial invariants; Admin authorizes and delegates. Host wiring stays consistent. Public pricing remains marketing copy.

## Non-Goals and Later Waves

No roles, suspension, impersonation, general user management, admin-triggered reset, Stripe, Trader rewrite, or full roadmap claim. Separate Waves 1–5 retain Trader analytics/calendar/dashboard, Journal/Risk, Strategies/Alerts/Planner, Scanner/MarketData, and Imports/AI.

## Chained Delivery, Validation, Rollout, and Rollback

| Slice (≤400 lines) | Deliverable | Validate | Rollout / rollback |
|---|---|---|---|
| 0a | Identity rules/schema | Domain tests; migration rehearsal | Add schema; revert code, retain inert columns |
| 0b | Recovery API/email | Integration tests; build; log assertions | Enable SMTP; disable endpoints/sender |
| 0c | Forced-change UI | Frontend test/build; auth/Trader regression | After 0b; revert UI/guards |
| 0d | Billing/Admin API | Billing/authz tests; build | Add schema/API; unmap API, retain data |
| 0e | Admin UI | Frontend test/build; portal smoke tests | After 0d; unroute UI |

## Dependencies and Risks

Dependencies: SMTP credentials, Mailpit, PostgreSQL, Admin claims, and predecessor slices. Risks: credential disclosure, enumeration, lockout abuse, authorization leakage, migration drift, and scope creep. Mitigate with redacted logs, uniform responses, throttling, policy tests, additive migrations, and regressions.

## Success Criteria

- Recovery and password-history rules pass security-focused tests end to end.
- Non-Admin access is denied; Admin can administer subscriptions only.
- PublicPortal and existing Trader journeys remain behaviorally unchanged.
- Every chained slice stays within 400 authored changed lines and is independently reversible.
