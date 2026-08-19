# Email Deliverability Specification

**Change**: 2026-08-19-wave11-gdpr-v1-readiness
**Wave**: 11 (GDPR v1 readiness — closure of Wave 10.5 narrower scope)
**Slice**: 11.4 — Welcome email trigger + SPF/DKIM/DMARC runbook
**Status**: NEW spec (no prior canonical — Wave 10.5 deferred this)
**Strict TDD**: ACTIVE — every Requirement + Scenario here MUST be covered by tests in slice 11.4

## Purpose

Ensure transactional emails (welcome, password reset, account deletion confirmation) are delivered reliably in production. The system MUST send a welcome email on user registration, idempotent via `users.welcome_email_sent_at` (only send if NULL + 7-day suppression). The system MUST document the SPF / DKIM / DMARC DNS records the ops team must publish before v1.0 GA in `docs/runbooks/email-deliverability.md` + `docs/email-deliverability.md`. The Mailgun / SES / Mailpit integration MUST set the DKIM signing key from env (`Mail__DkimPrivateKey` + `Mail__DkimSelector`). The Mailpit dev profile MUST skip DKIM signing. Welcome email failure MUST NOT roll back the user registration (wrapped in `try { ... } catch { log + continue }`).

## Requirements

### Requirement: Welcome email idempotent via `users.welcome_email_sent_at`

The system MUST extend `RegisterUserHandler.HandleAsync` to send a welcome email AFTER the user row is persisted AND the `AddAsync` call returns successfully. The trigger MUST be wrapped in `try { ... } catch (Exception ex) { _logger.LogError(ex, "Welcome email failed for user {UserId}", user.Id); }` so email failure does NOT roll back the registration. The handler MUST check `user.WelcomeEmailSentAt` BEFORE calling `IEmailSender.Send`; if non-NULL, the send MUST be skipped (idempotent re-send protection). After a successful send, the handler MUST update `user.WelcomeEmailSentAt = clock.UtcNow` + call `_userRepository.UpdateAsync(user, ct)`. The 7-day suppression rule (`if user.WelcomeEmailSentAt != null && user.WelcomeEmailSentAt > UtcNow - 7d: skip`) MUST apply for re-registers (e.g., when a user requests a password reset and the system re-issues a token — the welcome email should NOT re-send within 7 days).

#### Scenario: Register triggers welcome email exactly once (idempotent)

- GIVEN a fresh user U1 with `WelcomeEmailSentAt = null`
- WHEN `RegisterUserHandler.HandleAsync` completes successfully
- THEN `IEmailSender.Send` MUST be called exactly once with `to = U1.Email`, `subject = "Welcome to Jade Capital"`, `body = "<minimal HTML + text>"` (the inline template)
- AND `user.WelcomeEmailSentAt` MUST equal `clock.UtcNow` ± 1 second
- AND `users.welcome_email_sent_at` MUST equal `clock.UtcNow` (persisted)
- AND `audit.events` MUST NOT contain an entry for the email send (the email is system-internal, not a user-mutation)

#### Scenario: Re-register within 7 days skips welcome email

- GIVEN U1 already registered 3 days ago + `WelcomeEmailSentAt = UtcNow - 3d` (sent previously)
- AND U1 re-registers via password recovery flow (creates a new user record via `RegisterUserHandler` re-call with the same email — separate path; or the recovery handler triggers a separate welcome email — verify the handler skips)
- WHEN the register or recovery handler runs
- THEN `IEmailSender.Send` MUST NOT be called for the welcome email (suppressed)
- AND `user.WelcomeEmailSentAt` MUST remain unchanged (no overwrite)

### Requirement: SPF / DKIM / DMARC DNS records documented for prod

The system MUST ship `docs/runbooks/email-deliverability.md` + `docs/email-deliverability.md` (resurrected from Wave 10.5 deferral) listing the exact DNS records the ops team must publish for the prod email domain (`jadecapital.com` or the operator's chosen domain) before v1.0 GA. The runbook MUST include:
- **1 SPF record** (`@ TXT "v=spf1 include:mailgun.org -all"` for Mailgun, or equivalent SES / SendGrid / Postmark include).
- **2 DKIM records** (`selector1._domainkey TXT "<DKIM public key 1>"` + `selector2._domainkey TXT "<DKIM public key 2>"` from the email provider's portal).
- **1 DMARC record** (`_dmarc TXT "v=DMARC1; p=quarantine; rua=mailto:dmarc@jadecapital.com; pct=100; adkim=s; aspf=s"`).
- DKIM key rotation cadence (recommended: every 12 months).
- Mailgun / SES / SendGrid / Postmark env-var mappings (`Mail__Host`, `Mail__Port`, `Mail__Username`, `Mail__Password`, `Mail__DkimPrivateKey`, `Mail__DkimSelector`, `Mail__FromAddress`).
- `mail-tester.com` target score ≥ 9/10 for deliverability pass.

#### Scenario: ops runbook exists + content sanity check

- GIVEN `docs/runbooks/email-deliverability.md` + `docs/email-deliverability.md` exist in the repo
- WHEN a xUnit test `EmailDeliverabilityDocsTests` reads both files
- THEN both files MUST contain the substring `"v=spf1"` (SPF record example)
- AND both files MUST contain the substring `"_dmarc"` (DMARC record reference)
- AND both files MUST contain the substring `"selector1._domainkey"` (DKIM record reference)
- AND `docs/runbooks/email-deliverability.md` MUST contain the substring `"p=quarantine"` (DMARC policy)
- AND the runbook MUST NOT contain `"TODO"` markers (it's the runbook, not legal copy)

(Note: no DNS dig at test time — the test is a doc-presence + content-sanity smoke test. The actual DNS records are published by ops at deploy time, not tested in CI.)

## Cross-references

- Companion spec: `openspec/specs/gdpr-compliance/spec.md` (the cookie consent + ToS acceptance requirements)
- Related Wave 11 spec: `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/specs/consent-acceptance/spec.md` (ToS acceptance columns migration)
- Source: `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/explore.md` §"Deferred items validation" item 5 + §"New gaps identified" G-2

## Out of scope

- Real-time email template management (e.g., WYSIWYG editor for legal/marketing) — post-v1.0
- Email open / click tracking (PII concern under GDPR) — out of scope; no tracking pixels ship
- Per-tenant email branding — Wave 12+ (requires `tenants.email_logo_url` column)
- Bounce / complaint webhook handling from email providers — Wave 12+ (requires `email_events` table)
- Email deliverability monitoring (e.g., automatic `mail-tester.com` runs in CI) — Wave 13+
