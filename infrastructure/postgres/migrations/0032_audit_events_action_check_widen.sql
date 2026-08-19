-- Migration 0029 — Widen the ck_audit_events_action CHECK constraint
-- (Wave 7, slice 7a.1).
--
-- The Wave 6 6d.1 migration 0027_audit_events.sql defined the constraint
-- as `action IN (0, 1, 2, 3)` — matching the AuditAction enum at the time
-- (Created=0, Updated=1, Deleted=2, Restored=3).
--
-- Wave 7 slice 7a.1 adds two new AuditAction values:
--   - Denied = 4  (cross-tenant access attempt)
--   - Failed = 5  (DeleteAsync NotSupportedException path)
--
-- This migration widens the CHECK constraint so the new typed audit
-- decorators (UserAuditDecorator, RiskProfileAuditDecorator in 7a.1;
-- StrategyAuditDecorator, TradeAuditDecorator in 7b.1; JournalEntryAudit
-- Decorator in 7b.2) can insert audit.events rows with the new action
-- values. Without the widening, the CHECK constraint rejects the new
-- inserts at the DB level — even though the application enum permits them.
--
-- ATOMICITY: this migration MUST land in the same PR as the
-- AuditAction enum extension (slice 7a.1 phase 1). If the enum gains
-- new values but the CHECK constraint is not widened, the decorators
-- fail to insert their audit rows on every operation. Pinning both
-- in a single PR prevents the broken-window state.
--
-- IDEMPOTENT: the DROP CONSTRAINT IF EXISTS clause makes re-runs safe.
-- The 0027 migration's constraint definition is OVERWRITTEN by this
-- migration's definition; running 0029 twice leaves the same final
-- constraint in both cases.
--
-- No data migration: the constraint widening is purely additive (no
-- existing rows have action IN (4, 5) — there are no rows to update).

BEGIN;

ALTER TABLE audit.events
    DROP CONSTRAINT IF EXISTS ck_audit_events_action;

ALTER TABLE audit.events
    ADD CONSTRAINT ck_audit_events_action
    CHECK (action IN (0, 1, 2, 3, 4, 5));

COMMENT ON CONSTRAINT ck_audit_events_action ON audit.events IS
    'AuditAction enum range (Wave 6 6d.1: 0=Created, 1=Updated, 2=Deleted, 3=Restored; Wave 7 7a.1: +4=Denied, +5=Failed). Idempotent re-run safe.';

COMMIT;