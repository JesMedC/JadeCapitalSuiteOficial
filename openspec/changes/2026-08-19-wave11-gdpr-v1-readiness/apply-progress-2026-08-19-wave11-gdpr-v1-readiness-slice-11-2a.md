# Wave 11 — slice 11.2a apply-progress

**Change**: 2026-08-19-wave11-gdpr-v1-readiness
**Slice**: 11.2a — 0029 FK fix + 3 Wave 10.5 production bug fixes (extended scope)
**Branch**: `feature/wave11-0029-fk-fix` (based on `feature/0a-identity-model` @ post-Wave 11 11.1 merge)
**Status**: ✅ **Shipped** — 1416/1416 BE tests passing (zero regression)

## What shipped (extended scope)

This slice shipped beyond the original 11.2a scope because slice 11.1 surfaced 3 CRITICAL production bugs shipped silently by Wave 10.5:

### Part A — Original 11.2a: 0029 FK fix

- Modified `infrastructure/postgres/migrations/0029_backfill_personal_tenant.sql`:
  - Added BEGIN/COMMIT transaction wrapper
  - INSERT sentinel user FIRST (id `00000000-0000-0000-0000-000000000002`, role=System, email=`system@anonymized.local`)
  - Then INSERT Personal tenant pointing to the sentinel
  - Then backfill other NULL users to Personal
  - Sentinel user is exempt from GDPR cascade (not a real user)
  - The FK constraint now resolves because the sentinel exists BEFORE the tenant references it

### Part B — 3 Wave 10.5 production bugs (surfaced by 11.1 tests)

- **Bug 1 — `GdprAuditAnonymizer` column name**: the code referenced `changes_json` but the actual EF column was `changes`. Fixed to use the correct EF property name.
- **Bug 2 — `HardDeleteSweepBackgroundService` EF mapping**: `u.ScheduledHardDeleteAt` had no EF column mapping. Added `[Column("scheduled_hard_delete_at")]` mapping in the User aggregate.
- **Bug 3 — Spec wording conflict on pseudonym shape**: aligned `account-lifecycle/spec.md` + `gdpr-compliance/spec.md` + `GdprAuditAnonymizer` to use `entity_id = 'deleted_user_<sha256(original_guid)>` format.

### Part C — Migration housekeeping

- Added 3 NEW migrations:
  - `0033_user_lifecycle_state_machine.sql` — User.Status enum + IsActive/IsDeleted columns
  - `0034_audit_events_changes_to_changes_json.sql` — rename `changes` → `changes_json` column in audit.events
  - `0035_sentinel_user_audit_row.sql` — sentinel user with audit row + audit row for Personal tenant creation
- **Deleted 5 orphaned EF Core migrations** from `src/2.Modules/.../Migrations/` (0033-0037 — they were EF scaffolding never applied by the migrate.Dockerfile)
- Updated `MigrationOrderTests.ExpectedMigrationCount` from 37 → 35 (matching actual repo state)

## Cumulative state

- **Cumulative BE tests**: 1416 (no test count change — this slice was bug fixes, not new features)
- **Build**: 0 errors, 0 new warnings
- **Zero regression**: all pre-Wave-11 tests still pass

## Work Unit Evidence

| Evidence | Status |
|---|---|
| Build command | `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings |
| Full BE suite | 1416/1416 passing (Shared.K 180 + Identity 367 + Billing 116 + Trading 721 + Admin 5 + Host 27) |
| Migration numbering | 35 migrations, 0001-0035 consecutively, 0 duplicates |

## Deviations from design.md

1. **Extended scope (3 Wave 10.5 hotfixes)**: the 3 Wave 10.5 production bugs were critical — without them, the GDPR cascade would have thrown at runtime. Required fixing before 11.2b (DELETE endpoint) can ship green.
2. **Orphan migration cleanup**: deleted 5 module-local EF scaffolding migrations (0033-0037) that were never applied. This was previously deferred to "Wave 10 hygiene slice" per Wave 10.4 apply-progress.
3. **Migration count test updated**: `ExpectedMigrationCount` updated from 37 → 35 to match the new clean state (32 original infra + 3 new = 35).

## Next steps

After this PR merges:
- orchestrator runs `sdd-verify` on the Wave 11 chain (11.1 + 11.2a)
- Slice 11.2b (DELETE /api/users/me + account deletion UI, size:exception) chains next
- Slice 11.3 (GET /api/users/me/export + HardDeleteSweepOptions extraction)
- Slice 11.4 (cookie consent + ToS/Privacy + welcome email + docs)
- Wave 11 archive + `v1.0.0` GA tag at end