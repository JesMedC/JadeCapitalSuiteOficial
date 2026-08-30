-- Migration 0025 — identity.users.tenant_id NULLABLE (Wave 6, slice 6c.1).
--
-- Adiciona la columna tenant_id como NULLABLE a identity.users. Es el primer
-- paso de la estrategia "ONE migration atómica" (user decision 2026-08-18):
--   - 6c.1 (esta migration): ADD COLUMN NULLABLE + FK + partial index.
--   - 6c.2: BackfillTenantsHostedService crea un Personal tenant por cada
--           usuario NULL y lo asigna.
--   - 6c.3: ALTER COLUMN SET NOT NULL (idempotente: solo aplica si todas las
--           filas ya tienen tenant_id — el CHECK del NOT NULL falla en DB si
--           hay NULLs).
--
-- Columna:
--   - tenant_id (UUID NULL) — FK a identity.tenants(id) ON DELETE RESTRICT
--
-- Defensa-in-depth:
--   - ADD COLUMN IF NOT EXISTS (PG 9.6+) — re-run safe.
--   - FK con ON DELETE RESTRICT — no se puede borrar un tenant que tenga
--     usuarios asignados (transfer ownership es una operacion futura).
--   - 1 partial index sobre tenant_id (solo non-NULL) — queries de filtro
--     per-tenant ("WHERE tenant_id = ?") no barren los NULLs pre-Wave-6.

BEGIN;

ALTER TABLE identity.users
    ADD COLUMN IF NOT EXISTS tenant_id UUID;

-- FK constraint: solo se agrega si la tabla identity.tenants existe
-- (0024 debe haber corrido antes). IF NOT EXISTS via DO block.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_users_tenant_id'
    ) AND EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'identity' AND table_name = 'tenants'
    )
    THEN
        ALTER TABLE identity.users
            ADD CONSTRAINT fk_users_tenant_id
            FOREIGN KEY (tenant_id)
            REFERENCES identity.tenants(id)
            ON DELETE RESTRICT;
    END IF;
END
$$;

-- Indice partial: solo filas con tenant_id non-NULL. Acelera el query
-- "WHERE tenant_id = ?" una vez que el backfill de 6c.2 haya asignado
-- todos los usuarios.
CREATE INDEX IF NOT EXISTS ix_users_tenant_id
    ON identity.users (tenant_id)
    WHERE tenant_id IS NOT NULL;

COMMENT ON COLUMN identity.users.tenant_id IS
    'Tenant membership (Wave 6, slice 6c.1). NULLABLE pre-backfill (6c.2); NOT NULL after slice 6c.3.';

COMMIT;
