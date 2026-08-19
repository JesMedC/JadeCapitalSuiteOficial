# GDPR Compliance Specification

**Change**: 2026-08-18-wave10-v1-readiness
**Wave**: 10 (v1 readiness)
**Slice**: 10.5 — Legal + GDPR right-to-be-forgotten + data portability + cookie consent + ToS
**Status**: NEW spec (no prior canonical)
**Strict TDD**: ACTIVE — every Requirement + Scenario here must be covered by tests in slice 10.5

## Purpose

Ship the GDPR compliance surface for Jade Capital Suite's EU launch. The system MUST expose `DELETE /api/users/me` (GDPR Art. 17 right-to-be-forgotten with a 30-day grace period) that anonymizes + soft-deletes the user across all 17 user-owned aggregates and schedules hard-delete after the grace expires. The system MUST expose `GET /api/users/me/export` (GDPR Art. 20 data portability) that streams a JSON archive of all user-owned entities — `audit.events` is excluded (compliance trail), `stripe_webhook_events` is excluded (system). A cookie consent banner MUST appear on the Public Portal's first visit, persist the choice in `localStorage` + the `users.cookie_consent_accepted_at` column, and gate analytics/tracking scripts (none ship in v1.0 — banner records consent for future-proofing). The system MUST require ToS + Privacy Policy acceptance on registration with the timestamp persisted on `users.terms_accepted_at`. Transactional email deliverability MUST be ensured via documented SPF/DKIM/DMARC DNS records + idempotent welcome email on register.

## ADDED Requirements

### Requirement: DELETE /api/users/me triggers GDPR Art. 17 cascade

The system MUST expose `DELETE /api/users/me` at `src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/UserEndpoints.cs` requiring `Authorize` + the calling user matches the path `{id = "me"}`. The endpoint MUST call `DeleteUserHandler` which: (1) anonymizes `users.email = "deleted-{userId:N}@anonymized.local"`, `display_name = "Deleted User"`, sets `IsDeleted = true`, `ScheduledHardDeleteAt = UtcNow + 30 days`, (2) invokes `IUserCascadeDeletor.CascadeAsync(userId, ct)` which soft-deletes every row where `user_id = userId` across Identity/Trading/Billing (skipping `audit.events` and `stripe_webhook_events`), (3) revokes all `refresh_tokens` for the user, (4) writes an `audit.events` row with `entity_type = "User"`, `action = "Deleted"`, `changes = { "AnonymizedEmail": { ... } }`. The endpoint MUST return HTTP 202 Accepted with `{ "gracePeriodDays": 30, "hardDeleteScheduledAt": "<iso8601>" }`.

#### Scenario: DELETE returns 202 Accepted + starts cascade

- GIVEN an authenticated user U1
- WHEN U1 calls `DELETE /api/users/me`
- THEN the endpoint MUST return 202 with `{ "gracePeriodDays": 30, "hardDeleteScheduledAt": "<UtcNow+30d>" }`
- AND `users.is_deleted` MUST be `true`
- AND `users.scheduled_for_hard_delete_at` MUST be `UtcNow + 30d`
- AND every row in `trading.trades` WHERE `user_id = U1` MUST have `is_deleted = true`
- AND `audit.events` MUST contain a `User/Deleted` row with `user_id = U1` (the only allowed exception to `UserAuditDecorator.DeleteAsync`'s `NotSupportedException`)

#### Scenario: 30-day grace period elapses → hard delete

- GIVEN U1 called DELETE on day 0
- WHEN `HardDeleteSweepBackgroundService` runs on day 31
- THEN every row referencing `U1` MUST be physically purged from the DB
- AND `audit.events` MUST retain ONE pseudonymized row (`user_id = NULL`, `entity_id = U1`, `entity_type = "User"`, `action = "Deleted"`, `changes = { "hardDelete": { ... } }`)

### Requirement: GET /api/users/me/export streams user data (GDPR Art. 20)

The system MUST expose `GET /api/users/me/export` returning `application/json` with `Content-Disposition: attachment; filename="jadecapital-export-{userId}.json"`. The response MUST be a JSON object containing arrays per entity: `users`, `accounts`, `trades`, `journals`, `trade_reviews`, `strategies`, `alerts`, `planner_sessions`, `pre_trade_checklists`, `risk_profile`, `subscription`, `stripe_customer`, `settings`. The endpoint MUST exclude `audit.events` (compliance trail per Wave 9 spec) and `stripe_webhook_events` (system). The endpoint MUST require `Authorize` and return 403 for cross-tenant requests.

#### Scenario: export includes trades, journals, accounts, strategies, etc.

- GIVEN U1 has 5 trades, 3 journal entries, 2 strategies, 1 risk profile
- WHEN U1 calls `GET /api/users/me/export`
- THEN the JSON MUST contain `trades: [5 items]`, `journals: [3 items]`, `strategies: [2 items]`, `risk_profile: { ... }`, and arrays for every entity listed above
- AND the response MUST stream without buffering the entire dataset in memory

#### Scenario: export EXCLUDES audit.events

- GIVEN `audit.events` has 250 rows for U1
- WHEN U1 calls `GET /api/users/me/export`
- THEN the response MUST NOT contain an `audit_events` field
- AND the response MUST NOT contain a `stripe_webhook_events` field

### Requirement: Cookie consent banner on Public Portal (GDPR ePrivacy)

The system MUST render a cookie consent banner on first visit to any page under `frontend/src/app/features/public/**`. The banner MUST offer two choices: "Accept all" + "Essential only". The choice MUST persist in `localStorage` (`jade.consent = { choice, acceptedAt }`) AND be POSTed to `POST /api/auth/consent` which writes `users.cookie_consent_accepted_at` + `users.cookie_consent_choice`. The banner MUST NOT reappear after a choice is made (within the same browser). Analytics/tracking scripts MUST be gated on `localStorage.jade.consent.choice === "all"` (no scripts ship in v1.0, but the gating is wired).

#### Scenario: banner appears on first visit

- GIVEN a user with no prior `localStorage.jade.consent`
- WHEN the user loads `https://jadecapital.com/`
- THEN the cookie consent banner MUST be visible
- AND it MUST offer "Accept all" + "Essential only" buttons

#### Scenario: user choice persists in localStorage

- GIVEN the banner is visible
- WHEN the user clicks "Accept all"
- THEN `localStorage.jade.consent = { "choice": "all", "acceptedAt": "<iso8601>" }` MUST be set
- AND `POST /api/auth/consent` MUST return 200
- AND `users.cookie_consent_accepted_at` MUST be set in the DB

#### Scenario: analytics/tracking scripts gated by consent

- GIVEN `localStorage.jade.consent.choice === "all"`
- WHEN analytics scripts attempt to load
- THEN they MUST execute (no scripts in v1.0 — gating is verified by a unit test on the `CookieConsentService`)

- GIVEN `localStorage.jade.consent.choice === "essential"`
- WHEN analytics scripts attempt to load
- THEN they MUST be blocked (verified by a unit test)

### Requirement: ToS + Privacy Policy acceptance on register

The system MUST extend `RegisterUserCommand` with `AcceptTerms: bool` + `AcceptPrivacy: bool` + `ConsentIp: string?`. The validator MUST require both flags to be `true` or reject the command. The handler MUST persist `users.terms_accepted_at = UtcNow`, `users.privacy_accepted_at = UtcNow`, `users.consent_ip = <ip>` on success. The endpoint MUST return 422 with `error.code = "auth.terms_required"` if either flag is missing.

#### Scenario: user must accept ToS + policy before account creation

- GIVEN a registration request with `AcceptTerms = false`
- WHEN the handler validates the command
- THEN the endpoint MUST return 422 with `error.code = "auth.terms_required"`

#### Scenario: acceptance timestamp persisted on User record

- GIVEN a valid registration with `AcceptTerms = true, AcceptPrivacy = true`
- WHEN the user is created
- THEN `users.terms_accepted_at` and `users.privacy_accepted_at` MUST equal `UtcNow` ± 1 second
- AND `users.consent_ip` MUST equal the request IP

### Requirement: Email deliverability — SPF/DKIM/DMARC pass

The system MUST ship `docs/runbooks/setup-email-deliverability.md` listing the exact DNS records for the prod email domain: 1 SPF (`@ TXT "v=spf1 include:mailgun.org -all"` or equivalent for SES), 2 DKIM (selector1 + selector2 TXT records from the email provider's portal), 1 DMARC (`_dmarc TXT "v=DMARC1; p=quarantine; rua=mailto:dmarc@jadecapital.com"`). The Mailgun/SES integration MUST set the DKIM signing key from env (`Mail__DkimPrivateKey` + `Mail__DkimSelector`). The Mailpit dev profile MUST skip DKIM signing. The welcome email on register MUST be idempotent (skip if the same user already received a welcome email in the last 7 days, verified via `users.welcome_email_sent_at`).

#### Scenario: transactional emails have SPF/DKIM/DMARC pass

- GIVEN the DNS records from `setup-email-deliverability.md` are published
- WHEN a transactional email (password reset, welcome) is sent
- THEN `dig TXT jadecapital.com` MUST return the SPF record
- AND `dig TXT selector1._domainkey.jadecapital.com` MUST return the DKIM public key
- AND `dig TXT _dmarc.jadecapital.com` MUST return the DMARC record
- AND `mail-tester.com` MUST score ≥ 9/10 (full deliverability pass)

## Cross-references

- Closes gaps A8 (ToS + Privacy + Cookie), A9 (GDPR Art. 17), G-A1 (GDPR Art. 20), G-A3 (cookie consent), G-A4 (SPF/DKIM/DMARC), G-A5 (welcome email), B18 (account deletion UI)
- Related Wave 9 spec: `openspec/specs/soft-delete-audit/spec.md` (cascade pattern, `ISoftDelete` + `DecoratedRepository<T>`)
- Related Wave 10 spec: `openspec/changes/2026-08-18-wave10-v1-readiness/specs/account-lifecycle/spec.md` (the cascade + grace + sweep lives there)
- Source: `openspec/changes/2026-08-18-wave10-v1-readiness/explore.md` §A8, §A9, §G-A1, §G-A3, §G-A4, §G-A5

## Out of scope

- Legal copy for ToS / Privacy Policy (user / legal counsel supplies — placeholder pages ship with `<!-- TODO: legal copy -->` markers)
- Audit log tamper-evidence / hash chain (Wave 11+, gap G-A2)
- DSAR (Data Subject Access Request) intake form beyond the export endpoint — `docs/runbooks/gdpr-data-subject-request.md` documents the manual flow
- Right-to-restrict-processing (GDPR Art. 18) — distinct endpoint, Wave 11+
- Right-to-object (GDPR Art. 21) — n/a for v1.0 (no automated decision-making in scope)
- Real-time consent revocation via email header (`List-Unsubscribe`) — Wave 11+
