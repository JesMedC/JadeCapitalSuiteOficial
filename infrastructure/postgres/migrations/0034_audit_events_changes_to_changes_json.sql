-- Migration 0034 — Rename audit.events.changes to changes_json (Wave 11.2a hotfix)
--
-- Slice 11.2a EXTENDED scope — fixes the Wave 10.5 production bug where the
-- GDPR audit anonymizer (`GdprAuditAnonymizer.AnonymizeUserAsync`) issues
-- `UPDATE audit.events SET changes_json = {0} ...` against a column the
-- migration 0030 + AuditEventConfiguration call `changes`. On a real Postgres
-- deployment the sweep cycle fails at runtime with
--   `42703: column "changes_json" of relation "events" does not exist`.
--
-- The fix aligns three places that drifted apart in Wave 10.5:
--   1) Migration 0030 created the column as `changes` (JSONB).
--   2) AuditEventConfiguration maps `ChangesJson` → `changes` (snake_case).
--   3) GdprAuditAnonymizer SQL uses `changes_json` in SET clause.
--
-- The `changes_json` name is the canonical one used by:
--   - design.md §2.2 (line 209): INSERT INTO audit.events changes_json ...
--   - gdpr-compliance/spec.md (multiple references): changes_json
--   - account-lifecycle/spec.md (line 50): changes_json
--   - proposal.md §7.2 #2: changes_json payload
--   - 11.1 anonymizer tests: changes_json
--   - Wave 10.5 production anonymizer SQL: SET changes_json = {0}
--
-- Migration 0030's `changes` is the Wave 10.5 typo (the snake_case
-- convention was NOT followed consistently across the table — other
-- columns are already snake_case). This forward-only migration
-- corrects the typo so the column matches the production anonymizer +
-- spec canon + test fixtures.
--
-- Defense-in-depth:
--   - ALTER TABLE ... RENAME COLUMN is idempotent against re-runs via
--     a DO $$ guard (information_schema.columns check).
--   - The column's data type (JSONB) is preserved — no rewrite.
--   - COMMENT ON COLUMN is reapplied so the docs reflect the new name.
--   - No FK / index changes needed (the column has no index — Wave 6
--     chose to keep the payload column un-indexed because the
--     compliance trail is queried by entity_id / user_id, not by
--     payload content).

BEGIN;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'audit'
          AND table_name = 'events'
          AND column_name = 'changes'
    ) THEN
        ALTER TABLE audit.events RENAME COLUMN changes TO changes_json;
    END IF;
END
$$;

COMMENT ON COLUMN audit.events.changes_json IS
    'JSONB diff payload: { "field": { "before": ..., "after": ... } }. NULL for Created events. The GdprAuditAnonymizer overwrites this column with { "reason": "gdpr_hard_delete", "original_user_id_hash": "<sha256-hex>" } on hard-delete. Renamed from `changes` in migration 0034 (Wave 11.2a hotfix — Wave 10.5 typo correction).';

COMMIT;
