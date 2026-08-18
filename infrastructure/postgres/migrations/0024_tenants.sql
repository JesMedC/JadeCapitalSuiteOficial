-- Migration 0024 — Tenants table (Wave 6, slice 6c.1).
--
-- Tabla identity.tenants: workspace aislado que agrupa usuarios (Personal /
-- Pro / Enterprise). El owner_user_id FK referencia identity.users(id); el
-- slug es UNIQUE para garantizar URLs estables y la posibilidad de lookup
-- por hostname (p.ej. "acme-capital" → acme-capital.jadecapital.app).
--
-- Columnas:
--   - id (UUID PK) — Guid del aggregate Tenant
--   - name (VARCHAR(120) NOT NULL) — display name; trimmed 2..120 chars
--   - slug (VARCHAR(64) NOT NULL) — URL-safe; regex [a-z0-9-]+; UNIQUE
--   - owner_user_id (UUID NOT NULL) — FK a identity.users(id) ON DELETE RESTRICT
--   - plan (SMALLINT NOT NULL) — TenantPlan enum: 0=Personal, 1=Pro, 2=Enterprise
--   - status (SMALLINT NOT NULL) — TenantStatus enum: 0=Active, 1=Suspended, 2=Archived
--   - created_at (TIMESTAMPTZ NOT NULL DEFAULT now())
--   - updated_at (TIMESTAMPTZ)
--
-- Indice unico (slug) garantiza idempotencia del slug — el handler de 6c.1
-- captura la violacion y la mapea a tenant.slug_taken (409).
--
-- Reglas idempotente + additive only:
--   - CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS.
--   - 2 CHECK constraints: plan BETWEEN 0..2, status BETWEEN 0..2 (defensa-in-depth).
--   - 1 UNIQUE index en slug; 1 regular index en owner_user_id para queries
--     tipo "list tenants by owner" (sera ListByOwnerAsync en 6c.1).
--   - FK a identity.users(id) ON DELETE RESTRICT — no se puede borrar un
--     user que sea owner de un tenant sin transferir ownership primero.

BEGIN;

CREATE TABLE IF NOT EXISTS identity.tenants (
    id              UUID PRIMARY KEY,
    name            VARCHAR(120) NOT NULL,
    slug            VARCHAR(64) NOT NULL,
    owner_user_id   UUID NOT NULL REFERENCES identity.users(id) ON DELETE RESTRICT,
    plan            SMALLINT NOT NULL,
    status          SMALLINT NOT NULL DEFAULT 0,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ,
    CONSTRAINT ck_tenants_plan CHECK (plan BETWEEN 0 AND 2),
    CONSTRAINT ck_tenants_status CHECK (status BETWEEN 0 AND 2),
    CONSTRAINT ux_tenants_slug UNIQUE (slug)
);

CREATE INDEX IF NOT EXISTS ix_tenants_owner_user_id
    ON identity.tenants (owner_user_id);

-- Trigger para mantener updated_at al dia en UPDATE. Reusa la funcion
-- billing.fn_set_updated_at() que existe desde la migration 0022; si no
-- existe todavia en una DB fresca (orden de migrations: 0022 -> 0023 ->
-- 0024), declaramos un fallback local.
CREATE OR REPLACE FUNCTION identity.fn_set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_tenants_updated_at ON identity.tenants;
CREATE TRIGGER trg_tenants_updated_at
    BEFORE UPDATE ON identity.tenants
    FOR EACH ROW EXECUTE FUNCTION identity.fn_set_updated_at();

COMMENT ON TABLE identity.tenants IS
    'Tenants (Wave 6, slice 6c.1). Isolated workspace that groups users; Personal/Pro/Enterprise.';

COMMENT ON COLUMN identity.tenants.slug IS
    'URL-safe slug; matches [a-z0-9-]+; UNIQUE — idempotency dedup key.';

COMMENT ON COLUMN identity.tenants.owner_user_id IS
    'FK to identity.users(id) ON DELETE RESTRICT. Permanent (transfer ownership is a future operation).';

COMMENT ON COLUMN identity.tenants.plan IS
    'TenantPlan enum: 0=Personal, 1=Pro, 2=Enterprise. CHECK constraint enforces range.';

COMMENT ON COLUMN identity.tenants.status IS
    'TenantStatus enum: 0=Active, 1=Suspended, 2=Archived. Transitions: Active → Suspended → Archived. No back-transitions.';

COMMIT;
