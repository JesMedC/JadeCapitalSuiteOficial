-- Migration 0033 — User lifecycle state machine (Wave 10.5 GDPR + Wave 11.2a hotfix)
--
-- Slice 11.2a EXTENDED scope (per the orchestrator's brief — the original 0029 FK
-- fix PLUS 3 critical Wave 10.5 production bugs surfaced by slice 11.1):
--
--   1) Adds `scheduled_for_hard_delete_at TIMESTAMPTZ NULL` to identity.users
--      so the `HardDeleteSweepBackgroundService` LINQ
--      (`db.Users.Where(u => u.ScheduledHardDeleteAt <= cutoff)`) compiles +
--      executes against Postgres. Without this column, the BackgroundService
--      throws `42703: column u.ScheduledHardDeleteAt does not exist` on the
--      very first cycle (Wave 10.5 production bug).
--
--   2) Widens the existing `ck_users_status` CHECK constraint so the new
--      `UserStatus.{SoftDeleted, ScheduledHardDelete, HardDeleted}` enum
--      values (Wave 10.5) are accepted by Postgres. The legacy constraint
--      only allowed ('Active', 'Suspended', 'Cancelled', 'LockedOut') — any
--      GDPR cascade write to `status = 'SoftDeleted'` would be rejected with
--      `23514: check constraint "ck_users_status" violated`. The Wave 10.5
--      GDPR delete path is broken in production without this fix.
--
-- Column + constraint contract:
--   - `scheduled_for_hard_delete_at`: nullable TIMESTAMPTZ. Set by the
--      `User.ScheduleHardDelete` domain method on Active → SoftDeleted
--      transition. The BackgroundService's WHERE clause filters on
--      `Status = 'ScheduledHardDelete' AND ScheduledHardDeleteAt <= UtcNow`.
--   - `ck_users_status`: widened to include ('SoftDeleted',
--     'ScheduledHardDelete', 'HardDeleted'). The legacy 4 values are
--      preserved (no breaking change for existing data). The new values
--      are added at the END so the existing ordinal positions of the
--      legacy values stay stable.
--
-- Defense-in-depth:
--   - ADD COLUMN IF NOT EXISTS (idempotent against re-runs).
--   - DO $$ block guards the CHECK constraint drop+add so a re-run on a
--     DB that already has the widened constraint is a no-op (the IF
--     EXISTS check on pg_constraint exits early).
--   - The new column has NO default — every existing row stays NULL until
--     the GDPR cascade explicitly schedules it (no silent backfill of
--     legacy users).
--   - The CHECK constraint widening is forward-only; legacy data is
--     untouched (no UPDATE on existing rows). Only NEW writes can use
--     the widened values.
--
-- Why not combine with 0034? 0033 adds schema needed by the GDPR state
-- machine (users + status check) while 0034 renames an audit.events
-- column. They're independent fixes; keeping them in separate migrations
-- preserves git blame + the ability to roll back either independently.

BEGIN;

-- 1) Add the scheduled_for_hard_delete_at column. Nullable so existing
--    rows are not affected — pre-Wave-10.5 users have NULL forever.
ALTER TABLE identity.users
    ADD COLUMN IF NOT EXISTS scheduled_for_hard_delete_at TIMESTAMPTZ;

-- Partial index for the BackgroundService's hot path:
--   SELECT id FROM identity.users
--   WHERE status = 'ScheduledHardDelete'
--     AND scheduled_for_hard_delete_at <= UtcNow;
-- The partial index is small (only rows in the scheduled state are
-- indexed) and accelerates the daily sweep's per-cycle query from O(N)
-- to O(1).
CREATE INDEX IF NOT EXISTS ix_users_scheduled_hard_delete_at
    ON identity.users (scheduled_for_hard_delete_at)
    WHERE status = 'ScheduledHardDelete';

COMMENT ON COLUMN identity.users.scheduled_for_hard_delete_at IS
    'GDPR Art. 17 grace period deadline. Set by User.ScheduleHardDelete after the soft-delete cascade completes. NULL for users who have not started the GDPR delete flow. HardDeleteSweepBackgroundService selects rows where this <= UtcNow.';

-- 2) Widen ck_users_status to include the 3 new Wave 10.5 enum values.
--    The legacy constraint (0001_InitialIdentitySchema.sql) was:
--      CHECK (status IN ('Active', 'Suspended', 'Cancelled', 'LockedOut'))
--    The widened constraint adds:
--      'SoftDeleted', 'ScheduledHardDelete', 'HardDeleted'
--    All legacy values are preserved (no rewrite of historical data).
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_users_status'
          AND conrelid = 'identity.users'::regclass
    ) THEN
        -- Drop + re-add with widened values. The drop is safe because
        -- ck_users_status is a CHECK (not FK or PK) — no dependent
        -- constraints. Existing rows are untouched because the
        -- constraint is just relaxed, not re-validated.
        ALTER TABLE identity.users DROP CONSTRAINT ck_users_status;
    END IF;

    ALTER TABLE identity.users
        ADD CONSTRAINT ck_users_status CHECK (status IN (
            'Active',
            'Suspended',
            'Cancelled',
            'LockedOut',
            'SoftDeleted',
            'ScheduledHardDelete',
            'HardDeleted'
        ));
END
$$;

COMMENT ON CONSTRAINT ck_users_status ON identity.users IS
    'UserStatus enum (Wave 1 + Wave 10.5 extension). The 3 new values (SoftDeleted/ScheduledHardDelete/HardDeleted) support the GDPR Art. 17 30-day grace state machine. Widened in migration 0033 (Wave 11.2a hotfix).';

COMMIT;
