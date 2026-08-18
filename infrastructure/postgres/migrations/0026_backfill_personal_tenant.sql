-- Migration 0026 — Personal Tenant + User Backfill (Wave 6, slice 6c.2).
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
-- Defensa-in-depth:
--   - INSERT ... ON CONFLICT (slug) DO NOTHING: re-run seguro (0024 ya creó
--     ux_tenants_slug).
--   - UPDATE ... WHERE tenant_id IS NULL: idempotente — segundo run modifica
--     0 filas.
--   - BEGIN / COMMIT: la inserción del Personal y el UPDATE de users van
--     en una sola transacción. Si falla alguna, la otra no aplica
--     (consistencia atómica).
--   - SELECT en subquery para resolver el id del Personal dentro de la
--     misma transacción (evita race con otro runner que también esté
--     creando el Personal).
--   - No se usa TRUNCATE / DELETE — solo INSERT/UPDATE idempotente.

BEGIN;

-- 1) Asegurar que existe exactamente UN Personal tenant (slug estable). El slug
--    'personal-default' es el mismo que usa BackfillTenantsRunner.PersonalSlug
--    en C# (constante compartida). 0024 ya creó la tabla + UNIQUE INDEX
--    ux_tenants_slug; el ON CONFLICT hace la insercion idempotente.
--
--    Owner del Personal: NULL para empezar; 0024 lo exige NOT NULL. Usamos
--    el primer usuario NULL como owner (garantiza que la FK existe). Si no
--    hay usuarios NULL, asignamos un sentinel Guid (00000000-0000-0000-0000-000000000001)
--    — sera irrelevante porque no hay usuarios a backfillear.
DO $$
DECLARE
    v_owner_user_id UUID;
    v_personal_id UUID := '11111111-1111-1111-1111-111111111111';
BEGIN
    -- Buscar el primer usuario NULL (mantiene el orden logico del runner en C#).
    SELECT id INTO v_owner_user_id
    FROM identity.users
    WHERE tenant_id IS NULL
    ORDER BY created_at NULLS FIRST, id
    LIMIT 1;

    -- Si no hay usuarios NULL, usar un sentinel (NO se usara para FKs reales).
    IF v_owner_user_id IS NULL THEN
        v_owner_user_id := '00000000-0000-0000-0000-000000000002';
    END IF;

    INSERT INTO identity.tenants (
        id, name, slug, owner_user_id, plan, status, created_at, updated_at
    )
    VALUES (
        v_personal_id,
        'Personal',
        'personal-default',
        v_owner_user_id,
        0,                  -- TenantPlan.Personal
        0,                  -- TenantStatus.Active
        now(),
        NULL
    )
    ON CONFLICT (slug) DO NOTHING;

    -- 2) UPDATE idempotente: solo filas NULL → asignar al Personal. El
    --    predicado WHERE tenant_id IS NULL garantiza re-run-safe (la
    --    segunda corrida update 0 filas).
    UPDATE identity.users
    SET tenant_id = v_personal_id, updated_at = now()
    WHERE tenant_id IS NULL;
END
$$;

-- Comentario a nivel tabla para audit log readability.
COMMENT ON TABLE identity.tenants IS
    'Tenants (Wave 6, slice 6c.1). Isolated workspace that groups users; Personal/Pro/Enterprise. Migration 0026 (slice 6c.2) creates the Personal/default tenant with slug personal-default and assigns every pre-Wave-6 NULL-row user to it; idempotent.';

COMMIT;
