# Delta for gdpr-compliance — Wave 11 Consent + ToS Acceptance

**Change**: 2026-08-19-wave11-gdpr-v1-readiness
**Wave**: 11 (GDPR v1 readiness — closure of Wave 10.5 narrower scope)
**Slice**: 11.4 — Cookie consent + ToS + welcome email + docs
**Status**: DELTA — extends `openspec/specs/gdpr-compliance/spec.md` (Wave 10.5 canonical) with the 5 consent + ToS acceptance scenarios that were deferred from Wave 10.5 (cookie banner FE + ToS acceptance on register)
**Strict TDD**: ACTIVE — every Requirement + Scenario here MUST be covered by tests in slice 11.4

## MODIFIED Requirements

### Requirement: Cookie consent banner on Public Portal (GDPR ePrivacy — bottom-bar delta)

(Previously: the requirement existed in `openspec/specs/gdpr-compliance/spec.md` line 54–81 with 3 scenarios specifying "banner". Wave 11.4 ships the bottom-bar implementation + 3 jest unit tests + 1 BE integration test on `/api/auth/consent`.)

#### Scenario: xUnit (jest) — bottom-bar cookie consent banner appears on first visit

- GIVEN a fresh browser session with no `localStorage.jade.consent` entry
- WHEN the user loads `https://jadecapital.com/`
- THEN the bottom-bar component MUST be visible (CSS `position: fixed; bottom: 0; left: 0; right: 0`)
- AND it MUST NOT reflow the main app shell above-the-fold (verified by visual snapshot test)
- AND it MUST offer "Accept all" + "Essential only" buttons
- AND it MUST NOT block the main app shell from loading (non-blocking init — verified by `expect(appShell).toBeVisible()`)

#### Scenario: xUnit (jest) — user choice persists in localStorage + sync to BE

- GIVEN the bottom-bar is visible (no prior `localStorage.jade.consent`)
- WHEN the user clicks "Accept all"
- THEN `localStorage.jade.consent = { "choice": "all", "acceptedAt": "<iso8601>" }` MUST be set
- AND `POST /api/auth/consent` MUST be called with `{ "choice": "all", "consentIp": "<ip>" }` and return 200
- AND `users.cookie_consent_accepted_at` MUST equal `UtcNow` ± 1 second (verified via `IgnoreQueryFilters()`)
- AND `users.cookie_consent_choice` MUST equal `1` (AcceptAll enum value)
- AND the bottom-bar MUST NOT reappear on subsequent page loads (within the same browser session)

#### Scenario: xUnit (jest + BE) — analytics/tracking scripts gated by consent

- GIVEN `localStorage.jade.consent.choice === "all"`
- WHEN analytics scripts attempt to load (e.g., a hypothetical GA4 tag)
- THEN they MUST execute (gated by `CookieConsentService.canLoadAnalytics()` returning `true`)
- AND `document.querySelector('script[data-jade-analytics]')` MUST be present in the DOM

- GIVEN `localStorage.jade.consent.choice === "essential"`
- WHEN analytics scripts attempt to load
- THEN they MUST be blocked (`CookieConsentService.canLoadAnalytics()` returns `false`)
- AND `document.querySelector('script[data-jade-analytics]')` MUST NOT be present in the DOM

(Note: no analytics scripts ship in v1.0 — the gating is verified via a jest unit test on `CookieConsentService.canLoadAnalytics()`.)

### Requirement: ToS + Privacy Policy acceptance on register (delta)

(Previously: the requirement existed in `openspec/specs/gdpr-compliance/spec.md` line 83–98 with 2 scenarios. The columns (`terms_accepted_at`, `privacy_accepted_at`, `consent_ip`) did NOT exist in the schema. Wave 11.4 ships the migration `0039_add_consent_columns.sql` + the `RegisterUserCommand` extension + 2 BE tests.)

#### Scenario: xUnit — RegisterUserCommand requires AcceptTerms + AcceptPrivacy + ConsentIp

- GIVEN a `RegisterUserCommand` with `AcceptTerms = false, AcceptPrivacy = true, ConsentIp = "192.0.2.1"`
- WHEN the FluentValidation validator runs
- THEN the command MUST be rejected with `error.code = "auth.terms_required"` (HTTP 422)

- GIVEN a `RegisterUserCommand` with `AcceptTerms = true, AcceptPrivacy = false, ConsentIp = "192.0.2.1"`
- WHEN the FluentValidation validator runs
- THEN the command MUST be rejected with `error.code = "auth.privacy_required"` (HTTP 422)

- GIVEN a `RegisterUserCommand` with `AcceptTerms = true, AcceptPrivacy = true, ConsentIp = null`
- WHEN the FluentValidation validator runs
- THEN the command MUST be rejected with `error.code = "auth.consent_ip_required"` (HTTP 422)

#### Scenario: xUnit — acceptance timestamp + IP persisted on User record

- GIVEN a valid `RegisterUserCommand` with `AcceptTerms = true, AcceptPrivacy = true, ConsentIp = "192.0.2.1"` at T0
- WHEN `RegisterUserHandler.HandleAsync` completes successfully
- THEN `users.terms_accepted_at` MUST equal `T0` ± 1 second
- AND `users.privacy_accepted_at` MUST equal `T0` ± 1 second
- AND `users.consent_ip` MUST equal `"192.0.2.1"` (inet column)
- AND `audit.events` MUST contain a `User/Created` row with `user_id = newUser.Id`, `tenant_id = ITenantContext.Current`

## Cross-references

- Companion spec: `openspec/specs/gdpr-compliance/spec.md` (canonical Wave 10.5 — the cookie consent banner + ToS acceptance behavior)
- Related Wave 11 spec: `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/specs/account-deletion-ui/spec.md` (FE pattern for modal-on-settings-page)
- Related Wave 11 spec: `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/specs/email-deliverability/spec.md` (welcome email idempotency)
- Source: `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/explore.md` §"Deferred items validation" items 3, 4 + §"New gaps identified" G-4

## Out of scope

- Real legal copy for ToS / Privacy Policy (post-v1.0 — `docs/PROJECT-STATUS.md` §5 + `CONTRIBUTING.md` flag "DO NOT DEPLOY WITHOUT LEGAL SIGN-OFF")
- Per-version acceptance tracking (e.g., when ToS changes, re-prompt users) — Wave 12+ (requires `terms_version` column)
- Real-time consent revocation via email header (`List-Unsubscribe`) — Wave 12+
- WCAG 2.1 AA baseline on cookie banner (a11y) — Wave 12+
