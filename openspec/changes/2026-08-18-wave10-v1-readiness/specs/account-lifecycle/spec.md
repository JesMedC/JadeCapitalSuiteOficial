# Account Lifecycle Specification

**Change**: 2026-08-18-wave10-v1-readiness
**Wave**: 10 (v1 readiness)
**Slice**: 10.5 — User lifecycle states + hard-delete BackgroundService (companion to `gdpr-compliance`)
**Status**: NEW spec (no prior canonical)
**Strict TDD**: ACTIVE — every Requirement + Scenario here must be covered by tests in slice 10.5

## Purpose

Define the user lifecycle state machine that drives the GDPR right-to-be-forgotten flow. A user progresses through four states: **Active** → **SoftDeleted** (immediately after `DELETE /api/users/me`) → **ScheduledHardDelete** (grace period set, default 30 days) → **HardDeleted** (rows physically purged by `HardDeleteSweepBackgroundService`). The state machine is enforced on `users.status` (NEW column) + the existing `IsDeleted` soft-delete flag. The BackgroundService MUST run daily, find users where `Status = 'ScheduledHardDelete' AND ScheduledHardDeleteAt <= UtcNow`, physically purge their rows (across Identity/Trading/Billing), and write ONE pseudonymized `audit.events` row per user for compliance trail. The service MUST be idempotent — a re-run on the same day MUST find 0 users to delete.

## ADDED Requirements

### Requirement: User lifecycle states — Active → SoftDeleted → ScheduledHardDelete → HardDeleted

The system MUST add a `status SMALLINT NOT NULL DEFAULT 0` column to `identity.users` (0=Active, 1=SoftDeleted, 2=ScheduledHardDelete, 3=HardDeleted) plus a `scheduled_for_hard_delete_at TIMESTAMPTZ NULL` column. The existing `is_deleted` BOOLEAN MUST remain as the soft-delete flag (backward-compat with `ISoftDelete`). The four-state machine MUST be enforced by:
- **Active**: `status = 0`, `is_deleted = false`, `scheduled_for_hard_delete_at IS NULL`
- **SoftDeleted**: `status = 1`, `is_deleted = true`, `scheduled_for_hard_delete_at IS NULL` (set briefly during the cascade before scheduling completes)
- **ScheduledHardDelete**: `status = 2`, `is_deleted = true`, `scheduled_for_hard_delete_at <= UtcNow + 30d`
- **HardDeleted**: NO rows in `identity.users` (physically purged); ONE pseudonymized `audit.events` row per purged user

A CHECK constraint MUST enforce `scheduled_for_hard_delete_at IS NOT NULL` when `status IN (2)` and `scheduled_for_hard_delete_at IS NULL` when `status IN (0)`.

#### Scenario: Active user has IsActive=true, Status='Active'

- GIVEN a freshly registered user U1
- WHEN the system persists U1
- THEN `users.status = 0` (Active), `users.is_deleted = false`, `users.scheduled_for_hard_delete_at IS NULL`

#### Scenario: SoftDeleted user has IsActive=false, Status='SoftDeleted', SoftDeletedAt set

- GIVEN U1 calls `DELETE /api/users/me`
- WHEN the cascade completes
- THEN `users.status = 1` (SoftDeleted), `users.is_deleted = true`, `users.deleted_at_utc = UtcNow`, `users.deleted_by_user_id = U1.Id` (self-delete recorded)

#### Scenario: ScheduledHardDelete user has Status='ScheduledHardDelete', ScheduledHardDeleteAt set, ≤ 30 days after SoftDeletedAt

- GIVEN U1 is SoftDeleted at T0
- WHEN the `DeleteUserHandler` completes the cascade
- THEN `users.status = 2` (ScheduledHardDelete), `users.scheduled_for_hard_delete_at = T0 + 30 days`
- AND the difference MUST be exactly 30 days ± 1 second

#### Scenario: HardDeleted user rows purged from DB (audit.events keeps pseudonymed entry)

- GIVEN U1 has `status = 2` and `scheduled_for_hard_delete_at <= UtcNow`
- WHEN `HardDeleteSweepBackgroundService.RunOnceAsync` runs
- THEN NO row in `identity.users` with `id = U1.Id` MUST exist after the sweep
- AND every row in `trading.trades WHERE user_id = U1.Id` MUST be physically deleted (no soft-delete flag — purge)
- AND `audit.events` MUST contain exactly 1 pseudonymized row: `user_id = NULL`, `entity_id = U1.Id`, `entity_type = "User"`, `action = "Deleted"`, `changes = { "hardDeletedAt": { "before": null, "after": "<UtcNow>" }, "originalStatus": { "before": null, "after": "ScheduledHardDelete" } }`

### Requirement: HardDeleteSweepBackgroundService runs daily + idempotent

The system MUST define `HardDeleteSweepBackgroundService : BackgroundService` at `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Lifecycle/HardDeleteSweepBackgroundService.cs`. The service MUST run once per 24 hours (configurable via `HardDeleteSweepOptions.IntervalHours`, default 24). The first run MUST be delayed by `InitialDelay` (default 5 minutes — Wave 9 `AuditRetentionBackgroundService` precedent). The sweep logic MUST find users where `status = 2 AND scheduled_for_hard_delete_at <= UtcNow` (NOT `is_deleted AND created_at < cutoff` — clock-skew-safe), then for each user: (1) physically delete rows from `trading.*`, `billing.*`, `identity.refresh_tokens`, (2) physically delete the `identity.users` row, (3) write the pseudonymized `audit.events` row, (4) increment a Serilog counter `HardDeletedUsersCount`. The sweep MUST wrap each user in its own transaction so a single failure doesn't abort the entire batch.

#### Scenario: sweep finds users where Status='ScheduledHardDelete' AND ScheduledHardDeleteAt < UtcNow → hard delete

- GIVEN U1 (scheduled 30 days ago), U2 (scheduled 29 days ago), U3 (Active)
- WHEN `RunOnceAsync` runs at T0
- THEN U1 MUST be hard-deleted (1 `audit.events` pseudonymized row)
- AND U2 MUST NOT be hard-deleted (`scheduled_for_hard_delete_at > UtcNow`)
- AND U3 MUST NOT be touched

#### Scenario: idempotent re-run finds 0 users to delete

- GIVEN U1 was hard-deleted in the previous sweep run
- WHEN `RunOnceAsync` runs again immediately after
- THEN the query MUST return 0 rows (U1 is physically gone)
- AND NO further `audit.events` rows MUST be written
- AND the Serilog log MUST record `HardDeletedUsersCount = 0` at Debug level

## Cross-references

- Companion spec to `openspec/changes/2026-08-18-wave10-v1-readiness/specs/gdpr-compliance/spec.md` (the GDPR DELETE endpoint triggers this state machine)
- Related Wave 9 spec: `openspec/specs/soft-delete-audit/spec.md` (`ISoftDelete` interface + `DecoratedRepository<T>` cascade pattern)
- Related Wave 9 spec: `openspec/specs/audit-retention-policy/spec.md` (the 90-day audit retention runs independently — hard-deleted users still leave a 1-row pseudonymized trail)
- Source: `openspec/changes/2026-08-18-wave10-v1-readiness/explore.md` §A9, §G-A1

## Out of scope

- User self-restoration within the grace period (the 30-day window allows manual ops intervention via re-register + restore SQL — documented in `docs/runbooks/gdpr-data-subject-request.md`, NOT automated)
- Notification email to the user before hard-delete fires ("your account will be deleted in 7 days") — Wave 11+ (requires email template + a `pre_hard_delete_warning_sent_at` column)
- Multi-region hard-delete coordination (Wave 11+, gap G-C1)
- Anonymized-but-not-deleted tier (e.g., a "freeze my account" mode that pauses processing without deleting data) — Wave 11+
- Bulk admin hard-delete (admin-triggered purge of multiple users in one sweep) — Wave 11+
