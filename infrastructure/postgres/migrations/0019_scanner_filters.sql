-- Migration 0017 — Trader Scanner (slice 4a, Wave 4).
--
-- Anade al schema `trading`:
--   * trading.scanner_filters — filtros guardados por el trader para correr
--     contra el universo de instrumentos. Una fila por (user_id, name) UNIQUE.
--     Wave 4a implementa el CRUD + ranking por historical R-R. Wave 4b
--     agregara la columna `instrument_metadata_24h` y la lectura via
--     IQuoteProvider para los filtros spread/volume 24h.
--
-- Reglas idempotente + additive only:
--   - CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS.
--   - UNIQUE per (user_id, name) — move semantics: cambiar el name es nuevo filter.
--   - FK cross-schema a identity.users(id) ON DELETE CASCADE: borrar el user
--     borra sus filters.
--   - active_hours JSONB: spec del Wave 4 dice "{mon:[{open,close}],...}".
--   - volatility_window SMALLINT 1..30 (codigos 1/7/30 = D1/W1/MN).
--   - min/max spread y min volume declarados pero no wired en 4a (Wave 4b).

BEGIN;

CREATE TABLE IF NOT EXISTS trading.scanner_filters (
    id              UUID         PRIMARY KEY,
    user_id         UUID         NOT NULL,
    name            VARCHAR(64)  NOT NULL,
    min_spread      NUMERIC(10,5) NULL CHECK (min_spread IS NULL OR min_spread >= 0),
    max_spread      NUMERIC(10,5) NULL CHECK (max_spread IS NULL OR max_spread >= 0),
    min_volume      NUMERIC(24,8) NULL CHECK (min_volume IS NULL OR min_volume >= 0),
    min_risk_reward NUMERIC(6,2) NULL CHECK (min_risk_reward IS NULL OR min_risk_reward >= 1.0),
    volatility_window SMALLINT    NOT NULL DEFAULT 7 CHECK (volatility_window BETWEEN 1 AND 30),
    active_hours    JSONB        NULL,
    is_active       BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT fk_scanner_filter_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE CASCADE,
    CONSTRAINT ck_scanner_min_spread_le_max
        CHECK (min_spread IS NULL OR max_spread IS NULL OR min_spread <= max_spread),
    CONSTRAINT uq_scanner_filter_user_name UNIQUE (user_id, name) DEFERRABLE INITIALLY DEFERRED
);

CREATE INDEX IF NOT EXISTS ix_scanner_filters_user_active
    ON trading.scanner_filters(user_id) WHERE is_active;

COMMENT ON TABLE trading.scanner_filters IS 'Trader scanner filters (slice 4a). UNIQUE per (user_id, name). Wave 4b wires spread/volume to live quotes via IQuoteProvider.';
COMMENT ON COLUMN trading.scanner_filters.active_hours IS 'JSONB con mapa de horarios activos por dia (e.g. {mon: [{open, close}]}).';

COMMIT;
