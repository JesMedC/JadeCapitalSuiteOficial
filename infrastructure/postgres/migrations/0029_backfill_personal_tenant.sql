-- Migration 0029 — Personal Tenant + User Backfill (Wave 6, slice 6c.2 + Wave 11.2a FK fix).
--
-- Segundo paso de la estrategia "ONE migration atómica" (user decision 2026-08-18):
--   - 6c.1 (0025_users_tenant_id.sql): ADD COLUMN NULLABLE + FK + partial INDEX.
--   - 6c.2 (este archivo): crear UN Personal tenant + asignar todos los
--           usuarios NULL al mismo. Transaccional + idempotente.
--   - 6c.3 (0026_NOT_NULL_tenant_id.sql — slice 6c.3): ALTER COLUMN SET NOT NULL.
--
-- El runner en código (BackfillTenantsRunner) hace lo mismo que este script
-- para in-place upgrades de DBs pre-Wave-6. El script es para greenfield
-- deploys y para que el migrate.Dockerfile sea self-contained.
--
-- ============================================
-- Wave 11.2a EXTENDED scope — FK defect fix
-- ============================================
--
-- Original bug (Wave 6c.2 + surfaced in Wave 11.1 apply-progress):
--   When `identity.users` is empty (fresh DB), the migration INSERTs the
--   Personal tenant with `owner_user_id = '00000000-0000-0000-0000-000000000002'`
--   but the sentinel user with that id does NOT exist yet → FK constraint
--   `fk_tenants_owner_user_id` rejects the INSERT → fresh-DB apply fails.
--
-- Fix (per Wave 11 explore.md §1 + proposal.md §7.2 #10 = "Option B"):
--   In ONE atomic transaction:
--     1) INSERT the sentinel user (id `00000000-0000-0000-0000-000000000002`)
--        FIRST. Sentinel has role=System, email=system@anonymized.local,
--        password_hash="!" (a single-char placeholder — the user can never
--        authenticate because the password is unguessable noise).
--     2) INSERT the Personal tenant with `owner_user_id` pointing to the
--        sentinel. The FK now resolves because the sentinel exists.
--     3) UPDATE existing NULL users to the Personal tenant (idempotent).
--     4) Re-apply 0028's NOT NULL constraint on `identity.users.tenant_id`
--        (idempotent — exits early on existing NOT NULL columns).
--
-- Defense-in-depth:
--   - All operations in ONE BEGIN/COMMIT — atomic. If the sentinel INSERT
--     or the Personal tenant INSERT fails, the NOT NULL re-apply is never
--     attempted (fresh-DB stays consistent).
--   - INSERT ... ON CONFLICT (slug) DO NOTHING: re-run safe (0024 created
--     `ux_tenants_slug`).
--   - INSERT ... ON CONFLICT (id) DO NOTHING on the sentinel: re-run safe
--     (existing DBs that already passed 0029 see the sentinel INSERT as
--     a no-op).
--   - UPDATE ... WHERE tenant_id IS NULL: idempotent — second run modifies
--     0 rows.
--   - DO $$ guard on the NOT NULL re-apply: skips when the column is
--     already NOT NULL (idempotent on existing DBs).
--
-- Sentinel user — GDPR + GDPR cascade safety:
--   - id `00000000-0000-0000-0000-000000000002` — sentinel sentinel
--     (Wave 6 used `...0001` for a different purpose; we use `...0002`
--     to avoid clashing with any existing dev data).
--   - role = 1 (UserRole.Trader — see src/2.Modules/Identity/JadeCapital.Identity.Domain/Users/UserRole.cs).
--     The UserRole enum does NOT have a System value; the sentinel uses
--     Trader (the smallest non-Admin role). The role is irrelevant
--     because the sentinel cannot authenticate.
--   - email = 'system@anonymized.local' — anonymized domain; never receives
--     email, never logs in.
--   - password_hash = '!' — a single-char placeholder; not a real PBKDF2
--     hash. The sentinel CANNOT authenticate (no real user will ever type
--     the password `!` AND email `system@anonymized.local` — the login
--     flow has additional rate limits + lockout guards).
--   - tenant_id = the Personal tenant's id (set in step 2). The sentinel
--     lives in the Personal tenant like every other Wave 6 user.
--   - status = 'Active' — the sentinel is not deleted. It exists to
--     satisfy the FK and is otherwise inert.
--   - GDPR cascade safety: the sentinel is EXEMPT from the GDPR cascade
--     because it never owned any real data (no refresh_tokens, no risk
--     profiles, no trading aggregates, etc.). It also lacks the
--     `ScheduledHardDelete` state machine — the BackgroundService's WHERE
--     clause (`Status = 'ScheduledHardDelete'`) won't match the sentinel.
--
-- Why this is safe + necessary:
--   - On a FRESH DB: the sentinel is the first row in `identity.users`,
--     so no real user data depends on the sentinel's email/hash.
--     The anonymized domain + bogus hash make it impossible to log in.
--   - On an EXISTING DB: the sentinel INSERT is a no-op
--     (ON CONFLICT (id) DO NOTHING), the Personal tenant INSERT is a no-op
--     (ON CONFLICT (slug) DO NOTHING), the backfill UPDATE finds 0 NULL rows
--     (because 0028 already set tenant_id NOT NULL), and the NOT NULL
--     re-apply exits early. The migration is a safe no-op on existing DBs.
--
-- ============================================
-- Audit trail (Wave 11 G-1 closure)
-- ============================================
--
-- The sentinel user creation emits ONE `audit.events` row directly via
-- raw SQL (the AuditDbContext is not available in a migration context).
-- The row records the sentinel's creation event for compliance:
--   - entity_type = 'User'
--   - entity_id = sentinel id
--   - action = 0 (AuditAction.Created)
--   - user_id = NULL (system actor — no human triggered this)
--   - changes = {"sentinelUser": true, "reason": "GDPR FK defect fix — Wave 11.2a"}

BEGIN;

-- 0) Drop NOT NULL on tenant_id temporarily. Migration 0028 (which runs
--    BEFORE 0029 in file sort order) sets tenant_id NOT NULL. We MUST
--    allow the sentinel user to be inserted with a NULL tenant_id so
--    that the Personal tenant can be created + assigned AFTER (the
--    sentinel's tenant_id points to the Personal tenant, which doesn't
--    exist yet at step 1). The NOT NULL is re-applied at step 5.
--
--    The drop is idempotent: a re-run sees the column is already
--    nullable and exits early (no-op).
DO $$
DECLARE
    v_is_nullable TEXT;
BEGIN
    SELECT is_nullable INTO v_is_nullable
    FROM information_schema.columns
    WHERE table_schema = 'identity'
      AND table_name = 'users'
      AND column_name = 'tenant_id';

    IF v_is_nullable IS NULL THEN
        RAISE EXCEPTION '0029_sentinel: identity.users.tenant_id does not exist. '
            'Migration 0025 (0027_users_tenant_id.sql) must run first.';
    END IF;

    IF v_is_nullable = 'YES' THEN
        -- Already nullable — no need to drop. But this is unusual since
        -- 0028 should have set NOT NULL.
        RAISE NOTICE '0029_sentinel: tenant_id is already nullable; nothing to drop.';
        RETURN;
    END IF;

    ALTER TABLE identity.users ALTER COLUMN tenant_id DROP NOT NULL;
END
$$;

-- 1) INSERT the sentinel system user FIRST. Without this row existing,
--    step 2's INSERT into identity.tenants violates the FK
--    `fk_tenants_owner_user_id` (FK to identity.users.id ON DELETE RESTRICT).
--
--    The sentinel user is a permanent record. Documented in the table
--    COMMENT below. Cannot login (random unguessable PBKDF2 hash; in this
--    case the literal '!' placeholder — see header).
--
--    The sentinel's tenant_id is NULL at this point — it gets assigned to
--    the Personal tenant in step 3.
INSERT INTO identity.users (
    id, email, display_name, password_hash, role, status,
    email_confirmed_at, failed_login_count, session_version,
    attachment_quota_bytes, attachment_used_bytes,
    created_at, updated_at
) VALUES (
    '00000000-0000-0000-0000-000000000002',  -- sentinel id (FK target)
    'system@anonymized.local',                -- anonymized domain
    'System Account',                          -- public display
    '!',                                       -- NOT a real PBKDF2 hash; sentinel cannot login
    1,                                         -- UserRole.Trader (sentinel is not Admin — see header note)
    'Active',                                  -- UserStatus.Active (sentinel is not deleted)
    now(),                                     -- email_confirmed_at (sentinel auto-confirms)
    0,                                         -- failed_login_count
    1,                                         -- session_version (default)
    104857600,                                 -- attachment_quota_bytes = 100 MiB (default)
    0,                                         -- attachment_used_bytes (sentinel has no attachments)
    now(),                                     -- created_at
    now()                                      -- updated_at
)
ON CONFLICT (id) DO NOTHING;

-- 2) Asegurar que existe exactamente UN Personal tenant (slug estable). El slug
--    'personal-default' es el mismo que usa BackfillTenantsRunner.PersonalSlug
--    en C# (constante compartida). 0024 ya creó la tabla + UNIQUE INDEX
--    ux_tenants_slug; el ON CONFLICT hace la insercion idempotente.
--
--    Owner del Personal: ahora SIEMPRE es el sentinel
--    (00000000-0000-0000-0000-000000000002) — inserted en paso 1. Esto
--    garantiza que el FK se satisface tanto en fresh DBs como en DBs
--    pre-existentes (donde el sentinel ya fue inserted por una corrida
--    previa de esta misma migration).
DO $$
DECLARE
    v_personal_id UUID := '11111111-1111-1111-1111-111111111111';
BEGIN
    INSERT INTO identity.tenants (
        id, name, slug, owner_user_id, plan, status, created_at, updated_at
    )
    VALUES (
        v_personal_id,
        'Personal',
        'personal-default',
        '00000000-0000-0000-0000-000000000002',  -- sentinel (created in step 1)
        0,                  -- TenantPlan.Personal
        0,                  -- TenantStatus.Active
        now(),
        NULL
    )
    ON CONFLICT (slug) DO NOTHING;

    -- 3) UPDATE idempotente: asigna el sentinel al Personal + limpia
    --    filas NULL heredadas (legacy pre-Wave-6 users). El predicado
    --    WHERE tenant_id IS NULL cubre ambos casos.
    UPDATE identity.users
    SET tenant_id = v_personal_id, updated_at = now()
    WHERE tenant_id IS NULL;
END
$$;

-- 4) Re-apply 0028's NOT NULL constraint on tenant_id (idempotent —
--    exits early if the column is already NOT NULL on existing DBs
--    that already passed 0028). This is the LAST step so the constraint
--    is re-applied AFTER the sentinel + all legacy NULL users have
--    been assigned to the Personal tenant.
DO $$
DECLARE
    v_null_count BIGINT;
    v_is_nullable TEXT;
BEGIN
    SELECT is_nullable INTO v_is_nullable
    FROM information_schema.columns
    WHERE table_schema = 'identity'
      AND table_name = 'users'
      AND column_name = 'tenant_id';

    IF v_is_nullable = 'NO' THEN
        RAISE NOTICE '0029_sentinel: tenant_id is already NOT NULL; skipping.';
        RETURN;
    END IF;

    -- Pre-check: must have 0 NULL rows (the step 3 UPDATE handles legacy
    -- + sentinel assignment). If there are still NULLs, the backfill
    -- failed — abort with a clear error.
    SELECT COUNT(*) INTO v_null_count
    FROM identity.users
    WHERE tenant_id IS NULL;

    IF v_null_count > 0 THEN
        RAISE EXCEPTION '0029_sentinel: % rows still have tenant_id IS NULL after backfill. '
            'Migration is broken; investigate before retrying.', v_null_count;
    END IF;

    ALTER TABLE identity.users ALTER COLUMN tenant_id SET NOT NULL;
END
$$;

-- Comentario a nivel tabla para audit log readability.
COMMENT ON TABLE identity.tenants IS
    'Tenants (Wave 6, slice 6c.1). Isolated workspace that groups users; Personal/Pro/Enterprise. Migration 0026 (slice 6c.2) creates the Personal/default tenant with slug personal-default and assigns every pre-Wave-6 NULL-row user to it; idempotent. Wave 11.2a hotfix: owner_user_id points to the sentinel system user (00000000-0000-0000-0000-000000000002) inserted at the start of this migration so the FK is satisfied on fresh DBs.';

COMMENT ON COLUMN identity.users.id IS
    'User GUID. Sentinel user 00000000-0000-0000-0000-000000000002 (role=Trader, email=system@anonymized.local) is a permanent FK target for the Personal tenant (created in 0029). Cannot login (password_hash = "!" placeholder).';

-- NOTE: the audit row for the sentinel user creation lives in migration
-- 0035 (after the audit.events table is created by migration 0030).
-- Inserting it here would fail with `42P01: relation "audit.events"
-- does not exist` because 0029 runs BEFORE 0030 in file sort order.

COMMIT;
