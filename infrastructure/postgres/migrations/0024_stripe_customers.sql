-- Migration 0022 — Stripe Customers mapping (Wave 6, slice 6a.1).
--
-- Tabla billing.stripe_customers: mapeo uno-a-uno entre identity.users y un
-- Stripe Customer id. Una fila por usuario (idempotente en CreateOrGet).
--
-- Columnas:
--   - id (UUID PK) — Guid del aggregate StripeCustomer
--   - user_id (UUID NOT NULL) — FK a identity.users(id) ON DELETE CASCADE
--   - stripe_customer_id (VARCHAR(64) NOT NULL) — id de Stripe (cus_...)
--   - email (VARCHAR(320) NOT NULL) — email del usuario en el momento del create
--   - display_name (VARCHAR(120)) — nombre opcional
--   - created_at (TIMESTAMPTZ default now())
--   - updated_at (TIMESTAMPTZ default now())
--
-- Reglas idempotente + additive only:
--   - CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS.
--   - ADD COLUMN IF NOT EXISTS (para upgrades de instancias existentes).
--   - FK cross-schema a identity.users(id) ON DELETE CASCADE.
--   - CHECK basico de formato email (LIKE '%_@_%.__%' — defensa minima).
--   - UNIQUE (user_id) — un solo Stripe Customer por user.
--   - UNIQUE (stripe_customer_id) — idempotencia via constraint del DB.

BEGIN;

CREATE TABLE IF NOT EXISTS billing.stripe_customers (
    id                    UUID PRIMARY KEY,
    user_id               UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
    stripe_customer_id    VARCHAR(64) NOT NULL,
    email                 VARCHAR(320) NOT NULL,
    display_name          VARCHAR(120),
    created_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ck_stripe_customers_email CHECK (email LIKE '%_@_%.__%'),
    CONSTRAINT ux_stripe_customers_user UNIQUE (user_id),
    CONSTRAINT ux_stripe_customers_stripe_id UNIQUE (stripe_customer_id)
);

-- Indice adicional por created_at para queries de reporting / admin list.
CREATE INDEX IF NOT EXISTS ix_stripe_customers_created_at
    ON billing.stripe_customers (created_at DESC);

-- Trigger para mantener updated_at al dia en UPDATE.
CREATE OR REPLACE FUNCTION billing.fn_set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_stripe_customers_updated_at ON billing.stripe_customers;
CREATE TRIGGER trg_stripe_customers_updated_at
    BEFORE UPDATE ON billing.stripe_customers
    FOR EACH ROW EXECUTE FUNCTION billing.fn_set_updated_at();

COMMENT ON TABLE billing.stripe_customers IS
    'Stripe Customer mapping (Wave 6, slice 6a.1). One row per user; idempotent create.';

COMMENT ON COLUMN billing.stripe_customers.stripe_customer_id IS
    'Stripe Customer id (cus_...). UNIQUE para idempotencia.';

COMMIT;
