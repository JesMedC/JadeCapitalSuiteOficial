# GDPR Operations Runbook Specification

**Change**: 2026-08-19-wave11-gdpr-v1-readiness
**Wave**: 11 (GDPR v1 readiness — closure of Wave 10.5 narrower scope)
**Slice**: 11.4 — GDPR docs runbook (DSAR intake)
**Status**: NEW spec (no prior canonical — Wave 10.5 deferred the runbook; only the title in `gdpr-compliance/spec.md` line 124 referenced it)
**Strict TDD**: ACTIVE — the single Scenario here MUST be covered by a xUnit content-sanity test in slice 11.4

## Purpose

Document the manual GDPR Data Subject Access Request (DSAR) workflow for the ops team. The system MUST ship `docs/runbooks/gdpr-data-subject-request.md` covering: (1) DSAR intake via email (`privacy@jadecapital.com` or `support@jadecapital.com` — operator chooses), (2) identity verification (the user must prove they own the email — ops uses a manual verification process), (3) cascade deletion via `DELETE /api/users/me` (or via direct SQL for escalated requests), (4) 30-day grace period + restoration procedure (if the user requests restoration within 30 days, ops uses `users.ScheduledHardDeleteAt` + `users.Status` updates to reverse the soft-delete), (5) `HardDeleteSweepBackgroundService` hard-delete after 30 days, (6) audit trail pseudonymization via `GdprAuditAnonymizer`. The runbook is a manual workflow — no code is invoked by the user; only ops uses it. The xUnit test verifies the runbook's content + structure (smoke test for ops-facing docs).

## Requirements

### Requirement: DSAR runbook content + structure

The system MUST ship `docs/runbooks/gdpr-data-subject-request.md` (~80-150 LOC) with the following sections in this order:

1. **Overview** — what GDPR Art. 17 (right-to-be-forgotten) + Art. 20 (data portability) require + the 30-day grace period the system implements.
2. **DSAR intake** — how the user submits a DSAR (email to `privacy@jadecapital.com` + identity verification via signed-in email confirmation + photo ID for escalated cases). SLA: acknowledge within 72 hours, complete within 30 days (GDPR Art. 12).
3. **Cascade deletion** — the manual steps for ops to call `DELETE /api/users/me` (using the ops admin JWT, NOT the user's JWT — the user has already verified identity via email) OR direct SQL for escalated cases (`UPDATE identity.users SET status = 2, scheduled_for_hard_delete_at = now() + interval '30 days', is_deleted = true, deleted_at_utc = now(), deleted_by_user_id = id WHERE id = '<userId>';`).
4. **30-day grace + restoration** — if the user requests restoration within 30 days, ops runs: `UPDATE identity.users SET status = 0, scheduled_for_hard_delete_at = NULL, is_deleted = false, deleted_at_utc = NULL, deleted_by_user_id = NULL WHERE id = '<userId>';` + the per-module soft-delete reversal (run a new `RegisterUserHandler` re-call to re-create the user row — OR a dedicated `RestoreUserHandler` in a future wave).
5. **Hard-delete sweep** — `HardDeleteSweepBackgroundService` runs daily after the 30-day grace + physically purges rows. Ops monitors Serilog for `HardDeleteSweep: purged user {UserId}` log lines.
6. **Audit trail pseudonymization** — `GdprAuditAnonymizer` runs as part of `HardDeleteSweepBackgroundService.RunOnceAsync` (per-user transaction) + pseudonymizes `audit.events` rows. The audit trail is preserved (compliance) with `user_id = NULL` + `entity_id = 'deleted_user_<sha256>'` (deterministic hash for cross-reference).

The runbook MUST NOT contain `"TODO"` markers (it's an ops-facing doc — incomplete runbooks are a compliance risk). The runbook MUST include at least 3 concrete `psql` or `curl` command examples (one per major step). The runbook MUST link to `docs/PROJECT-STATUS.md` + `docs/adr/0009-gdpr-right-to-be-forgotten.md` for architectural context.

#### Scenario: xUnit — DSAR runbook exists + content sanity check

- GIVEN `docs/runbooks/gdpr-data-subject-request.md` exists in the repo
- WHEN a xUnit test `GdprOpsRunbookTests` reads the file
- THEN the file MUST contain the substring `"GDPR Art. 17"` (Art. 17 reference)
- AND the file MUST contain the substring `"GDPR Art. 20"` (Art. 20 reference)
- AND the file MUST contain the substring `"30-day grace"` (the grace period — exact wording may vary)
- AND the file MUST contain the substring `"privacy@jadecapital.com"` (the DSAR intake email — exact address is operator-chosen)
- AND the file MUST contain the substring `"HardDeleteSweep"` (the BackgroundService reference)
- AND the file MUST contain the substring `"GdprAuditAnonymizer"` (the audit anonymizer reference)
- AND the file MUST contain the substring `"psql"` or `"curl"` (a command example)
- AND the file MUST contain the substring `"0009-gdpr-right-to-be-forgotten"` (ADR reference)
- AND the file MUST NOT contain the substring `"TODO"` (no incomplete markers in an ops runbook)
- AND the file length MUST be > 1,500 characters (smoke test for substantive content)

## Cross-references

- Companion spec: `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/specs/gdpr-endpoint-coverage/spec.md` (the DELETE endpoint the runbook references)
- Companion spec: `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/specs/account-deletion-ui/spec.md` (the UI flow the runbook mirrors)
- ADR: `docs/adr/0009-gdpr-right-to-be-forgotten.md` (Wave 10.6 — the architectural decision the runbook documents)
- Source: `openspec/changes/2026-08-19-wave11-gdpr-v1-readiness/explore.md` §"New gaps identified" G-8

## Out of scope

- DSAR intake form beyond the manual runbook (FE form → ticket creation) — Wave 13+ (requires ticketing system integration)
- Automated DSAR SLA tracking (72h acknowledge + 30d complete) — Wave 13+ (requires `dsar_tickets` table + scheduler)
- Right-to-restrict-processing (GDPR Art. 18) runbook — Wave 12+ (distinct workflow)
- Right-to-object (GDPR Art. 21) runbook — n/a for v1.0 (no automated decision-making)
- Cross-border DSAR coordination (multi-region) — Wave 13+
