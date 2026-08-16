-- Migracion 0015a — Trader Strategies (slice 3a de 2026-08-18-trader-strategies-alerts-planner).
--
-- Anade al schema `trading`:
--   * trading.strategies — una strategy por user. Captura el setup que el
--     trader describe: instrumento (nullable = multi-symbol), timeframe
--     (nullable), reglas en texto libre (<= 2000 chars), description
--     (<= 1000 chars). Soft-delete via is_active = false. Uniqueness activo
--     es partial sobre (user_id, lower(name)) WHERE is_active para permitir
--     re-crear despues de un soft-delete (audit trail).
--   * trading.trades.strategy_id — FK nullable additive. El cross-schema FK
--     se crea en esta misma migration con ON DELETE SET NULL: si en algun
--     momento Wave 4+ hace hard-delete de una strategy, los trades
--     pre-existentes conservan la fila y pierden el link (safety net;
--     Wave 3 solo hace soft-delete).
--
-- Reglas de la migracion (idempotente, additive only):
--   - CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS.
--   - NO DROP, NO ALTER destructivo (rollback = DROP TABLE/COLUMN manual).
--   - Exactly-one-active-per-(user,name) invariant via PARTIAL UNIQUE INDEX.
--   - Soft-delete: is_active default TRUE. Re-crear con el mismo name esta
--     permitido (la partial UNIQUE solo enforce sobre is_active = TRUE).
--   - Timeframe SMALLINT con CHECK 1..10 (los valores 1..9 mapean a la
--     enum Timeframe; el rango [1,10] deja headroom para una futura
--     extension e.g. Y1=10 sin tocar la migration).
--   - El FK a identity.users es cross-schema y ON DELETE CASCADE: borrar
--     el usuario borra todas sus strategies (orphan rows serian basura).
--   - El FK de trades.strategy_id a strategies.id es ON DELETE SET NULL
--     (Wave 3 hace soft-delete, pero la safety net queda lista).
--
-- Origen de la fecha: 2026-08-18 (sprint Wave 3 / slice 3a).

BEGIN;

-- ============================================
-- trading.strategies
-- ============================================
CREATE TABLE IF NOT EXISTS trading.strategies (
    id              UUID            PRIMARY KEY,
    user_id         UUID            NOT NULL,
    name            VARCHAR(64)     NOT NULL,
    description     VARCHAR(1000)   NULL,
    symbol          VARCHAR(20)     NULL,
    timeframe       SMALLINT        NULL,
    rules           TEXT            NULL,
    is_active       BOOLEAN         NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ     NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ     NOT NULL DEFAULT now(),

    -- FK cross-schema a identity.users. Borrar el usuario borra sus strategies.
    CONSTRAINT fk_strategies_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE CASCADE,

    -- Timeframe 1..10 (rangos del enum Timeframe del Domain, mas headroom).
    CONSTRAINT ck_strategies_timeframe_range
        CHECK (timeframe IS NULL OR timeframe BETWEEN 1 AND 10),

    -- Longitudes maximas (defense in depth: la validacion a nivel
    -- Strategy.Create() los enforce tambien, pero la DB es la red final).
    CONSTRAINT ck_strategies_description_max_length
        CHECK (description IS NULL OR length(description) <= 1000),
    CONSTRAINT ck_strategies_rules_max_length
        CHECK (rules IS NULL OR length(rules) <= 2000)
);

-- Exactly-one-active-per-(user,name) via PARTIAL UNIQUE INDEX sobre
-- lower(name) (case-insensitive). Soft-deleted (is_active=false) rows
-- NO cuentan, asi que re-crear una strategy con el mismo name despues
-- de un DELETE esta permitido (audit trail).
CREATE UNIQUE INDEX IF NOT EXISTS ux_strategies_user_name_active
    ON trading.strategies (user_id, lower(name)) WHERE is_active = true;

-- Indice secundario sobre (user_id) WHERE is_active para el listado
-- principal del FE (GET /api/strategies).
CREATE INDEX IF NOT EXISTS ix_strategies_user_active
    ON trading.strategies (user_id) WHERE is_active = true;

-- ============================================
-- trading.trades.strategy_id (additive FK nullable)
-- ============================================
ALTER TABLE trading.trades
    ADD COLUMN IF NOT EXISTS strategy_id UUID NULL;

-- ADD CONSTRAINT no soporta IF NOT EXISTS en Postgres <=16; lo wrapeamos
-- en un DO block que chequea pg_constraint. Si Wave 4+ migra a PG 17+
-- podemos reemplazar por IF NOT EXISTS nativo.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_trades_strategy'
          AND conrelid = 'trading.trades'::regclass
    ) THEN
        ALTER TABLE trading.trades
            ADD CONSTRAINT fk_trades_strategy
                FOREIGN KEY (strategy_id) REFERENCES trading.strategies(id) ON DELETE SET NULL;
    END IF;
END $$;

-- Indice sobre (strategy_id) WHERE NOT NULL para queries de analytics
-- (GET /api/strategies/{id}/analytics carga los trades linkeados).
CREATE INDEX IF NOT EXISTS ix_trades_strategy
    ON trading.trades (strategy_id) WHERE strategy_id IS NOT NULL;

-- Comentarios.
COMMENT ON TABLE trading.strategies IS 'Trader-defined strategies (slice 3a). One row per user+name (active); soft-delete via is_active=false allows re-creation. Each strategy optionally tags one or more trades via trades.strategy_id.';
COMMENT ON COLUMN trading.strategies.id IS 'PK UUID.';
COMMENT ON COLUMN trading.strategies.user_id IS 'FK a identity.users(id) ON DELETE CASCADE. Borrar el usuario borra sus strategies.';
COMMENT ON COLUMN trading.strategies.name IS 'Nombre de la strategy. 1..64 chars, case-insensitive (uniqueness via lower(name)).';
COMMENT ON COLUMN trading.strategies.description IS 'Descripcion libre del setup. NULL permitido. <= 1000 chars.';
COMMENT ON COLUMN trading.strategies.symbol IS 'Simbolo del instrumento (e.g. EUR/USD). NULL = multi-symbol strategy. FK logica a trading.instruments(code) — no enforced en DB para no acoplar Wave 3 al seed de instrumentos.';
COMMENT ON COLUMN trading.strategies.timeframe IS 'Timeframe SMALLINT 1..10. 1=M1, 2=M5, 3=M15, 4=M30, 5=H1, 6=H4, 7=D1, 8=W1, 9=MN. 10 reservado para extension futura. NULL permitido.';
COMMENT ON COLUMN trading.strategies.rules IS 'Reglas de entrada/salida en texto libre. NULL permitido. <= 2000 chars.';
COMMENT ON COLUMN trading.strategies.is_active IS 'Soft-delete flag. Default TRUE. Partial UNIQUE index sobre (user_id, lower(name)) solo aplica cuando is_active=true, asi que re-crear despues de soft-delete esta permitido.';
COMMENT ON COLUMN trading.strategies.created_at IS 'Timestamp de creacion. Default = updated_at en INSERT.';
COMMENT ON COLUMN trading.strategies.updated_at IS 'Timestamp de la ultima modificacion.';

COMMENT ON COLUMN trading.trades.strategy_id IS 'FK additive nullable a trading.strategies(id) (slice 3a). ON DELETE SET NULL: si la strategy se borra fisicamente, los trades pierden el link pero conservan la fila. Wave 3 solo hace soft-delete.';

COMMIT;