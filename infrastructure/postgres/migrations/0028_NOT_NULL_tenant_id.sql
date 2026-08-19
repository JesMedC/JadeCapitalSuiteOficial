-- Migration 0026_NOT_NULL_tenant_id — Wave 6, slice 6c.3.
--
-- Tercer (y ultimo) paso de la estrategia "ONE migration atómica"
-- (user decision 2026-08-18):
--   - 6c.1 (0025_users_tenant_id.sql): ADD COLUMN NULLABLE + FK + partial INDEX.
--   - 6c.2 (0026_backfill_personal_tenant.sql): crea UN Personal tenant + asigna
--           todos los usuarios NULL al mismo. Transaccional + idempotente.
--   - 6c.3 (este archivo): ALTER COLUMN SET NOT NULL — cierra el ciclo.
--
-- Esta migration es IDEMPOTENTE: usa DO $$ ... $$ con un check explicito
-- sobre information_schema.columns.is_nullable para NO re-alterar una
-- columna que ya es NOT NULL. Re-correrla despues de un fallo parcial es
-- seguro (no-op si la columna ya es NOT NULL; ALTER si todavia es NULL).
--
-- Defensa-in-depth:
--   - DO $$ ... IF NOT is_nullable ... THEN ALTER ... END IF ... END $$:
--     idempotente contra re-ejecucion.
--   - Pre-check de NULL count: si hay NULLs despues del backfill de 6c.2,
--     esta migration FALLA rapido con un mensaje claro (no aplica el
--     NOT NULL silenciosamente — eso dejaria la columna sin constraint
--     y un INSERT NULL fallaria en produccion).
--   - NO usa ALTER TABLE ... ALTER COLUMN ... SET NOT NULL VALIDATE (forma
--     no-bloqueante de PG 12+): queremos el chequeo completo para no
--     diferir la validacion. La tabla identity.users es pequena (< 100k
--     filas esperado); el lock es aceptable.

BEGIN;

DO $$
DECLARE
    v_null_count BIGINT;
    v_is_nullable TEXT;
BEGIN
    -- 1) Chequear el estado actual de la columna. Si ya es NOT NULL, salir
    --    sin tocar nada (idempotente: re-correr es no-op).
    SELECT is_nullable INTO v_is_nullable
    FROM information_schema.columns
    WHERE table_schema = 'identity'
      AND table_name = 'users'
      AND column_name = 'tenant_id';

    IF v_is_nullable IS NULL THEN
        RAISE EXCEPTION '0026_NOT_NULL: identity.users.tenant_id does not exist. '
            'Migration 0025 must run first (ADD COLUMN tenant_id).';
    END IF;

    IF v_is_nullable = 'NO' THEN
        -- Already NOT NULL — nothing to do.
        RAISE NOTICE '0026_NOT_NULL: tenant_id is already NOT NULL; skipping.';
        RETURN;
    END IF;

    -- 2) Pre-check de NULL count. La 0026_backfill_personal_tenant.sql
    --    (6c.2) ya asigno todos los NULL al Personal tenant. Si quedan
    --    NULLs aqui, algo fallo en el backfill — fallar rapido.
    SELECT COUNT(*) INTO v_null_count
    FROM identity.users
    WHERE tenant_id IS NULL;

    IF v_null_count > 0 THEN
        RAISE EXCEPTION '0026_NOT_NULL: % rows still have tenant_id IS NULL. '
            'Migration 0026_backfill_personal_tenant.sql (6c.2) must run '
            'BEFORE this one to assign NULL users to the Personal tenant.',
            v_null_count;
    END IF;

    -- 3) Aplicar el ALTER. Acceso directo al catalogo: la columna ya
    --    existe, no hay NULLs, no hay FK violation porque todos los
    --    tenant_id referencian a identity.tenants.id.
    ALTER TABLE identity.users
        ALTER COLUMN tenant_id SET NOT NULL;

    RAISE NOTICE '0026_NOT_NULL: tenant_id is now NOT NULL (% rows revalidated).', v_null_count;
END
$$;

-- Indice completo (sin WHERE clause) — el partial INDEX de 0025 ya no es
-- necesario porque la columna es NOT NULL (no hay NULLs para excluir).
-- Lo dejamos; el partial sigue siendo valido y mas pequeno. Si el DBA
-- quiere reemplazarlo, lo hara en una migration futura.
--
-- Comentario: actualizar la documentacion de la columna para reflejar el
-- nuevo estado NOT NULL.
COMMENT ON COLUMN identity.users.tenant_id IS
    'Tenant membership (Wave 6, slice 6c.1 — added NULLABLE; 6c.2 — backfill; '
    '6c.3 — NOT NULL enforced). FK to identity.tenants(id) ON DELETE RESTRICT.';

COMMIT;
