-- Migracion 0007 — Billing Subscriptions (slice 0e de jade-trader-os-core-portals).
--
-- Anade al schema `billing`:
--   * billing.plans                 — catalogo Admin-managed de planes offertables.
--   * billing.subscriptions         — agregado de la suscripcion por usuario. UNIQUE
--                                      (user_id) garantiza "una suscripcion por usuario"
--                                      a nivel DB. INDEX (status, updated_at DESC)
--                                      sirve listados Admin por estado con el mas
--                                      reciente primero.
--   * billing.subscription_history  — append-only. INDEX (subscription_id,
--                                      occurred_at DESC, id DESC) reproduce el
--                                      orden del helper OrderNewestFirst del dominio
--                                      para que detail-view no haga sort en memoria.
--
-- Invariantes:
--   - money es NUMERIC(24,8) (decimal-only; nunca float). El techo de Money.MaxAmount
--     del kernel es 10^16, con 8 decimales -> 24 digitos en Postgres NUMERIC.
--   - subscription status es VARCHAR (no enum integer) para legibilidad humana
--     sin acoplarse al ordinal del enum del dominio.
--   - History es append-only (no UPDATE permission declarada fuera de 0e).
--   - UNIQUE (user_id) en subscriptions evita doble-suscripcion por usuario.
--   - Optimistic concurrency: column `version` INTEGER; 0f anade el wiring de
--     SaveChanges + xmin o explicit update-where-version-matches.
--
-- Reglas de la migracion:
--   - Idempotente: CREATE TABLE / INDEX con IF NOT EXISTS.
--   - Solo DDL aditiva: NO DROP TABLE, NO DROP COLUMN, NO DELETE en datos.
--   - Sin plaintext: ningun DEFAULT textual que pueda interpretarse como credencial.
--
-- Origen de la fecha: 2026-08-13 (sprint Wave 0 / slice 0e).
--
-- ⚠ Naming collision warning: slice 0c also produced a migration in this slot
--   (`20260812_0007_RecoverySupersession.sql`). Two distinct `0007` files now
--   exist in the migrations directory. The Dockerfile invokes them in
--   chronological order; both apply independently to disjoint tables, so
--   there is no functional conflict. A future housekeeping pass may renumber
--   to a contiguous sequence.

BEGIN;

-- Schema dedicado para extraer el modulo a su propio servicio en el futuro.
CREATE SCHEMA IF NOT EXISTS billing;

-- ============================================
-- billing.plans
-- ============================================
CREATE TABLE IF NOT EXISTS billing.plans (
    id                          UUID            PRIMARY KEY,
    code                        VARCHAR(32)     NOT NULL,
    name                        VARCHAR(80)     NOT NULL,
    monthly_price               NUMERIC(24, 8)  NOT NULL,
    monthly_price_currency      VARCHAR(3)      NOT NULL,
    is_eligible_for_self_service BOOLEAN        NOT NULL,
    is_deprecated               BOOLEAN        NOT NULL,
    created_at                  TIMESTAMPTZ     NOT NULL,
    updated_at                  TIMESTAMPTZ,

    CONSTRAINT ck_plans_currency_code
        CHECK (monthly_price_currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_plans_monthly_price_nonnegative
        CHECK (monthly_price >= 0)
);

-- Plan catalog keyed by code (Admin-managed); one row per plan code.
CREATE UNIQUE INDEX IF NOT EXISTS ux_plans_code
    ON billing.plans (code);

-- ============================================
-- billing.subscriptions
-- ============================================
CREATE TABLE IF NOT EXISTS billing.subscriptions (
    id                      UUID            PRIMARY KEY,
    user_id                 UUID            NOT NULL,
    plan_code               VARCHAR(32)     NOT NULL,
    status                  VARCHAR(16)     NOT NULL,
    trial_ends_at           TIMESTAMPTZ,
    current_period_start    TIMESTAMPTZ     NOT NULL,
    current_period_end      TIMESTAMPTZ     NOT NULL,
    version                 INTEGER         NOT NULL DEFAULT 1,
    created_at              TIMESTAMPTZ     NOT NULL,
    updated_at              TIMESTAMPTZ,

    CONSTRAINT fk_subscriptions_plan
        FOREIGN KEY (plan_code) REFERENCES billing.plans(code)
        ON DELETE RESTRICT,

    CONSTRAINT ck_subscriptions_status
        CHECK (status IN ('Active', 'Trial', 'Cancelled', 'Expired')),

    CONSTRAINT ck_subscriptions_version_positive
        CHECK (version > 0),

    CONSTRAINT ck_subscriptions_period_range
        CHECK (current_period_end > current_period_start)
);

-- UNIQUE (user_id): at most one subscription per user. Slice 0f adds the
-- UNIQUE partial index variant for active-only subscriptions when the
-- product rule allows multiple historical subscriptions per user with
-- only one currently "live".
CREATE UNIQUE INDEX IF NOT EXISTS ux_subscriptions_user
    ON billing.subscriptions (user_id);

-- List/search index for Admin UI: filter by status, newest-first within
-- status. Mirrors the EF mapping order.
CREATE INDEX IF NOT EXISTS ix_subscriptions_status_updated_at_desc
    ON billing.subscriptions (status, updated_at DESC);

-- ============================================
-- billing.subscription_history
-- ============================================
CREATE TABLE IF NOT EXISTS billing.subscription_history (
    id                  UUID            PRIMARY KEY,
    subscription_id     UUID            NOT NULL,
    action              VARCHAR(24)     NOT NULL,
    prior_plan_code     VARCHAR(32)     NOT NULL,
    resulting_plan_code VARCHAR(32)     NOT NULL,
    prior_status        VARCHAR(16)     NOT NULL,
    resulting_status    VARCHAR(16)     NOT NULL,
    actor               VARCHAR(120)    NOT NULL,
    occurred_at         TIMESTAMPTZ     NOT NULL,
    version             INTEGER         NOT NULL,
    reason              VARCHAR(500),
    prior_trial_ends_at TIMESTAMPTZ,
    new_trial_ends_at   TIMESTAMPTZ,
    created_at          TIMESTAMPTZ     NOT NULL,
    updated_at          TIMESTAMPTZ,

    CONSTRAINT fk_subscription_history_subscription
        FOREIGN KEY (subscription_id) REFERENCES billing.subscriptions(id)
        ON DELETE CASCADE,

    CONSTRAINT ck_subscription_history_action
        CHECK (action IN ('TierChanged', 'Cancelled', 'TrialExtended')),

    CONSTRAINT ck_subscription_history_version_positive
        CHECK (version > 0)
);

-- Detail-view newest-first ordering. MATCHES the domain's
-- SubscriptionHistoryEntry.OrderNewestFirst exactly so reads don't sort in
-- memory. (occurred_at DESC, id DESC) — the id is the Guid tie-breaker.
CREATE INDEX IF NOT EXISTS ix_subscription_history_subscription_occurred_id_desc
    ON billing.subscription_history (subscription_id, occurred_at DESC, id DESC);

-- Comentarios idempotentes. COMMENT ON no soporta IF EXISTS; los envolvemos
-- en DO blocks para que los re-runs sean silenciosos no-ops.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'billing' AND table_name = 'plans'
    ) THEN
        EXECUTE 'COMMENT ON TABLE billing.plans IS ''Catalogo Admin-managed de planes offertables; plan_code es la clave natural referenciada por subscriptions.''';
    END IF;
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'billing' AND table_name = 'subscriptions'
    ) THEN
        EXECUTE 'COMMENT ON TABLE billing.subscriptions IS ''Aggregate root del bounded context Billing. Una suscripcion por usuario (UNIQUE user_id); transiciones atomicas con optimistic-concurrency via column version.''';
    END IF;
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'billing' AND table_name = 'subscription_history'
    ) THEN
        EXECUTE 'COMMENT ON TABLE billing.subscription_history IS ''Append-only. (subscription_id, occurred_at DESC, id DESC) sirve detail-view newest-first sin sort en memoria.''';
    END IF;
END
$$;

COMMIT;
