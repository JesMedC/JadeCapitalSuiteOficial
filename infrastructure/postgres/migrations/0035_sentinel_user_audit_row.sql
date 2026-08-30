-- Migration 0035 — Sentinel user audit row (Wave 11.2a hotfix)
--
-- Slice 11.2a — G-1 closure. Records ONE `audit.events` row for the
-- sentinel user creation event so the compliance trail captures the
-- Wave 11.2a GDPR FK defect fix.
--
-- <para>
-- This migration MUST run AFTER migration 0030 (which creates the
-- <c>audit.events</c> table) — placing the INSERT here (instead of in
-- 0029) avoids the <c>42P01: relation "audit.events" does not exist</c>
-- error that would fire if 0029 tried to INSERT into audit.events
-- before migration 0030 ran.
-- </para>
--
-- Defense-in-depth:
--   - INSERT with gen_random_uuid() (the row id is fresh each run).
--   - ON CONFLICT on the (entity_type, entity_id, occurred_at) index is
--     not strictly enforced — multiple re-runs will append multiple
--     audit rows. The idempotency primitive is the
--     WHERE action=0 AND entity_type='User' AND entity_id=...
--     pre-check + the LIMIT 1. This keeps re-run safe at the cost of
--     one row per run (acceptable: the sentinel is created once and
--     the audit row is informational).
--   - The sentinel's email + role are captured in the changes_json
--     payload for compliance readability (the row's entity_id is the
--     sentinel's UUID, not human-readable).

BEGIN;

-- Idempotent INSERT: only insert if no existing audit row matches the
-- sentinel's (entity_type, entity_id, action) tuple. Re-runs append 0
-- rows (no duplicate).
INSERT INTO audit.events (
    id, entity_type, entity_id, action, tenant_id, user_id,
    changes_json, occurred_at
)
SELECT
    gen_random_uuid(),
    'User',
    '00000000-0000-0000-0000-000000000002'::uuid,
    0,                  -- AuditAction.Created
    NULL,               -- no tenant (the sentinel is system-level)
    NULL,               -- no user (the system actor)
    '{"sentinelUser": true, "reason": "GDPR FK defect fix — Wave 11.2a", "email": "system@anonymized.local", "role": 1, "displayName": "System Account"}'::jsonb,
    now()
WHERE NOT EXISTS (
    SELECT 1 FROM audit.events
    WHERE entity_type = 'User'
      AND entity_id = '00000000-0000-0000-0000-000000000002'::uuid
      AND action = 0
);

COMMIT;
