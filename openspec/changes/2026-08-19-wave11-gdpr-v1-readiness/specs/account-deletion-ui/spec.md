# Account Deletion UI Specification

**Change**: 2026-08-19-wave11-gdpr-v1-readiness
**Wave**: 11 (GDPR v1 readiness — closure of Wave 10.5 narrower scope)
**Slice**: 11.2b — `DELETE /api/users/me` + account deletion UI
**Status**: NEW spec (no prior canonical — Wave 10.5 deferred the UI; the BE endpoint is in `gdpr-endpoint-coverage/spec.md`)
**Strict TDD**: ACTIVE — every Requirement + Scenario here MUST be covered by tests in slice 11.2b

## Purpose

Provide the GDPR Art. 17 user-facing surface: an Angular "Delete my account" page accessible from the user settings area, with a confirmation modal that triggers `DELETE /api/users/me` and displays the 30-day grace period + cascade status. The page MUST be reachable via the settings tab in the new `frontend/src/app/features/settings/` feature area (Wave 11 introduces the settings feature module — Wave 7's `frontend/src/app/features/trader/settings/` was trader-scoped; Wave 11's settings area is the canonical user-account surface). The UI MUST NOT allow account restoration (the 30-day grace allows manual ops intervention via `docs/runbooks/gdpr-data-subject-request.md` — not via UI). The UI MUST be WCAG 2.1 AA compatible (semantic HTML, focus management, keyboard navigation) — full WCAG audit is Wave 12; this spec only requires the basics (alt text, ARIA labels, keyboard-reachable controls).

## Requirements

### Requirement: Account deletion page accessible from settings tab

The system MUST add a new feature module `frontend/src/app/features/settings/` (with `settings-routing.module.ts` + `settings.page.ts`) hosting the account-deletion page at `/settings/account-deletion` (lazy-loaded standalone route per Angular 19 standalone pattern). The page MUST be reachable from the main navigation (a "Settings" link in the top-right user menu — gated on authenticated user). The page MUST display: (a) the heading "Delete my account", (b) a red "Delete my account" button (Material design `mat-flat-button color="warn"` or equivalent), (c) a warning text "This action will start a 30-day grace period. After 30 days, all your data will be permanently deleted and cannot be recovered.", (d) a link to `docs/runbooks/gdpr-data-subject-request.md` (rendered as plain text in v1.0; proper help center is Wave 12+).

#### Scenario: settings tab visible after authentication

- GIVEN an authenticated user U1
- WHEN U1 clicks the "Settings" link in the top-right user menu
- THEN the URL MUST change to `/settings/account-deletion`
- AND the page MUST display the heading "Delete my account"
- AND the red "Delete my account" button MUST be visible + keyboard-reachable (tab order = button → link to runbook)

### Requirement: Confirmation modal triggers DELETE + shows cascade status

The page MUST open a confirmation modal when the user clicks "Delete my account". The modal MUST display: (a) the text "Are you sure? This will start a 30-day grace period. After 30 days, all your data will be permanently deleted.", (b) an input field requiring the user to type their email address (defense against accidental clicks — matches Wave 7's "type your password to confirm" pattern for destructive actions), (c) a "Cancel" button + a "Confirm deletion" button (the latter is disabled until the email input matches the current user's email). On "Confirm deletion" click, the modal MUST call `DELETE /api/users/me` (no body — the endpoint reads the authenticated user's id from `ITenantContext.CurrentUserId`). On a successful 202 response, the modal MUST close + the page MUST show a success banner: "Account scheduled for deletion. You have 30 days to restore by contacting support@jadecapital.com." On a 4xx/5xx response, the modal MUST show the error message inline.

#### Scenario: confirmation modal opens + requires email input

- GIVEN U1 on `/settings/account-deletion`
- WHEN U1 clicks "Delete my account"
- THEN a confirmation modal MUST open
- AND the modal MUST display the warning text + an email input field
- AND the "Confirm deletion" button MUST be disabled (input is empty)
- AND the "Cancel" button MUST close the modal without side effects

- WHEN U1 types their email `u1@example.com` in the input field
- THEN the "Confirm deletion" button MUST become enabled (input matches `currentUser.email`)
- AND if U1 types a different email, the button MUST remain disabled (verified via `expect(button.disabled).toBe(true)`)

#### Scenario: confirm deletion calls DELETE /api/users/me + shows cascade status

- GIVEN U1 has the modal open with `u1@example.com` typed in the email field
- WHEN U1 clicks "Confirm deletion"
- THEN `DELETE /api/users/me` MUST be called with no body (the endpoint reads the authenticated user's id)
- AND on 202 response, the modal MUST close + the page MUST show the success banner with the 30-day grace period text
- AND the page MUST show the cascade status: "Cascade initiated. <N> rows soft-deleted across <M> modules." (N + M come from the `UserCascadeDeleterOrchestrator.CascadeSoftDeleteAsync` return value + the count of registered deletors — surfaced via the `202 Accepted` response body or a follow-up `GET /api/users/me/cascade-status` endpoint)
- AND the page MUST show a link to `support@jadecapital.com` for restoration requests

- GIVEN the DELETE endpoint returns 500 (simulated server failure)
- WHEN U1 clicks "Confirm deletion"
- THEN the modal MUST show the error message inline ("Account deletion failed. Please try again or contact support.")
- AND the modal MUST remain open (the user can retry)

## Cross-references

- Companion spec: `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/specs/gdpr-endpoint-coverage/spec.md` (BE endpoint behavior + tests)
- Related Wave 11 spec: `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/specs/gdpr-ops-runbook/spec.md` (manual restoration procedure)
- Source: `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/explore.md` §"New gaps identified" G-7

## Out of scope

- Self-service account restoration within the 30-day grace period (manual ops only — `docs/runbooks/gdpr-data-subject-request.md` documents the SQL + email workflow)
- Pre-hard-delete warning email ("your account will be deleted in 7 days") — Wave 12+ (requires `pre_hard_delete_warning_sent_at` column + email template)
- Cascade status real-time updates (e.g., WebSocket push) — Wave 12+ (the current status is fetched once on page load)
- Bulk admin hard-delete UI — Wave 13+ (admin-triggered multi-user purge)
- WCAG 2.1 AA full audit — Wave 12+ (this spec only requires the basics: semantic HTML, keyboard-reachable, alt text)
