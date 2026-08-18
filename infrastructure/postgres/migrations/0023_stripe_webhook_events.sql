-- Migration 0023 — Stripe Webhook Events (Wave 6, slice 6a.2).
--
-- Tabla billing.stripe_webhook_events: append-only log de webhooks recibidos
-- desde Stripe. Cada evento llega con un event_id unico (evt_...). La UNIQUE
-- constraint sobre event_id es la pieza clave de la idempotencia: cuando
-- Stripe re-entrega un evento, el handler lo detecta por event_id y responde
-- 200 sin re-mutar el estado.
--
-- Columnas:
--   - id (UUID PK) — Guid del aggregate StripeWebhookEvent
--   - event_id (VARCHAR(64) NOT NULL) — Stripe `evt_...` id; UNIQUE dedup key
--   - event_type (VARCHAR(64) NOT NULL) — p.ej. "customer.subscription.created"
--   - payload_json (JSONB NOT NULL) — el raw body recibido (preservado verbatim)
--   - signature_header (VARCHAR(256)) — header `Stripe-Signature` para forensics
--   - received_at (TIMESTAMPTZ default now()) — cuando llego el request
--   - processed_at (TIMESTAMPTZ NULL) — cuando el handler termino (NULL = pending o failed)
--   - processing_error (VARCHAR(2000) NULL) — mensaje de error si fallo
--
-- Ademas (defensa-in-depth del rollback):
--   - ALTER TABLE billing.subscriptions ADD COLUMN stripe_subscription_id
--     VARCHAR(64) NULL + UNIQUE partial index. La columna la consume 6a.2 para
--     mapear webhook events → subscription local. Nullable en 6a.2 — las filas
--     pre-existentes (admin / seed) quedan en NULL hasta que un webhook las
--     actualice. Wave 7 podria volverla NOT NULL post-backfill.
--
-- Reglas idempotente + additive only:
--   - CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS.
--   - ADD COLUMN IF NOT EXISTS — upgrades sin locking.
--   - UNIQUE (event_id) — dedup a nivel DB.
--   - UNIQUE partial index sobre stripe_subscription_id (solo non-NULL) — la
--     columna puede ser NULL pero cuando existe, es unica.

BEGIN;

CREATE TABLE IF NOT EXISTS billing.stripe_webhook_events (
    id                  UUID PRIMARY KEY,
    event_id            VARCHAR(64) NOT NULL,
    event_type          VARCHAR(64) NOT NULL,
    payload_json        JSONB NOT NULL,
    signature_header    VARCHAR(256),
    received_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    processed_at        TIMESTAMPTZ,
    processing_error    VARCHAR(2000),
    CONSTRAINT ux_stripe_webhook_events_event_id UNIQUE (event_id)
);

CREATE INDEX IF NOT EXISTS ix_stripe_webhook_events_type
    ON billing.stripe_webhook_events (event_type);

-- Indice adicional sobre received_at para queries de admin / forensics
-- (listado newest-first de eventos recibidos en una ventana de tiempo).
CREATE INDEX IF NOT EXISTS ix_stripe_webhook_events_received_at
    ON billing.stripe_webhook_events (received_at DESC);

-- Defense-in-depth: stripe_subscription_id en billing.subscriptions (nullable).
-- La columna la popula el webhook handler cuando llega un customer.subscription.*
-- event. UNIQUE partial index para evitar duplicados cuando la columna es non-NULL.
ALTER TABLE billing.subscriptions
    ADD COLUMN IF NOT EXISTS stripe_subscription_id VARCHAR(64);

CREATE UNIQUE INDEX IF NOT EXISTS ux_subscriptions_stripe_subscription_id
    ON billing.subscriptions (stripe_subscription_id)
    WHERE stripe_subscription_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_subscriptions_stripe_subscription_id
    ON billing.subscriptions (stripe_subscription_id)
    WHERE stripe_subscription_id IS NOT NULL;

COMMENT ON TABLE billing.stripe_webhook_events IS
    'Stripe webhook event log (Wave 6, slice 6a.2). Append-only; re-delivery is no-op.';

COMMENT ON COLUMN billing.stripe_webhook_events.event_id IS
    'Stripe `evt_...` id. UNIQUE — idempotency dedup key.';

COMMENT ON COLUMN billing.stripe_webhook_events.payload_json IS
    'Raw JSON body received from Stripe. Preserved verbatim (no re-serialize).';

COMMENT ON COLUMN billing.stripe_webhook_events.signature_header IS
    'Original Stripe-Signature header (for forensics only — not the secret).';

COMMENT ON COLUMN billing.stripe_webhook_events.processed_at IS
    'When the handler completed dispatch. NULL = pending or failed (see processing_error).';

COMMENT ON COLUMN billing.stripe_webhook_events.processing_error IS
    'Error message if dispatch failed. NULL = success (or pending).';

COMMENT ON COLUMN billing.subscriptions.stripe_subscription_id IS
    'Stripe Subscription id (sub_...). Populated by webhook handler on customer.subscription.* events. Nullable in Wave 6 — Wave 7 may set NOT NULL post-backfill.';

COMMIT;
