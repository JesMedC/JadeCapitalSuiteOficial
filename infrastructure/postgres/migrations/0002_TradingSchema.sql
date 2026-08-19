-- Migracion: Trading schema inicial.
-- Sprint 1 Fase 1C. Tabla unica: trading.trades.
--
-- NOTA: se agrega la columna account_currency CHAR(3) NOT NULL ademas de
-- volume_currency. El spec de Fase 1C lista volume_currency como "account
-- currency" pero el aggregate Trade expone ambos como campos separados;
-- preservamos ambos para no perder data del dominio.
--
-- Schema dedicado para extraer el modulo a su propio servicio en el futuro.

BEGIN;

CREATE SCHEMA IF NOT EXISTS trading;

-- ============================================
-- trading.trades
-- ============================================
CREATE TABLE IF NOT EXISTS trading.trades (
    id                   UUID            PRIMARY KEY,
    user_id              UUID            NOT NULL,
    symbol               VARCHAR(20)     NOT NULL,
    asset_class          SMALLINT        NOT NULL,
    direction            SMALLINT        NOT NULL,
    status               SMALLINT        NOT NULL,
    volume_amount        NUMERIC(24,8)   NOT NULL,
    volume_currency      CHAR(3)         NOT NULL,
    entry_price_amount   NUMERIC(24,8)   NOT NULL,
    entry_price_currency CHAR(3)         NOT NULL,
    exit_price_amount    NUMERIC(24,8),
    exit_price_currency  CHAR(3),
    pnl_amount           NUMERIC(24,8),
    pnl_currency         CHAR(3),
    strategy             VARCHAR(80),
    notes                VARCHAR(2000),
    opened_at            TIMESTAMPTZ     NOT NULL,
    closed_at            TIMESTAMPTZ,
    account_currency     CHAR(3)         NOT NULL,
    created_at           TIMESTAMPTZ     NOT NULL,
    updated_at           TIMESTAMPTZ,

    CONSTRAINT ck_trades_asset_class CHECK (asset_class BETWEEN 1 AND 5),
    CONSTRAINT ck_trades_direction   CHECK (direction IN (1, 2)),
    CONSTRAINT ck_trades_status      CHECK (status IN (1, 2, 3)),
    CONSTRAINT ck_trades_volume_positive      CHECK (volume_amount > 0),
    CONSTRAINT ck_trades_entry_price_positive CHECK (entry_price_amount > 0),

    CONSTRAINT fk_trades_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS ix_trades_user_opened_at
    ON trading.trades (user_id, opened_at DESC);
CREATE INDEX IF NOT EXISTS ix_trades_user_status
    ON trading.trades (user_id, status);
CREATE INDEX IF NOT EXISTS ix_trades_user_symbol
    ON trading.trades (user_id, symbol);
CREATE INDEX IF NOT EXISTS ix_trades_opened_at
    ON trading.trades (opened_at DESC);

-- Comentarios
COMMENT ON TABLE  trading.trades IS 'Operaciones de trading del usuario. Una fila por trade.';
COMMENT ON COLUMN trading.trades.asset_class IS '1=Forex, 2=Crypto, 3=Binary, 4=Commodity, 5=Other';
COMMENT ON COLUMN trading.trades.direction  IS '1=Long, 2=Short';
COMMENT ON COLUMN trading.trades.status     IS '1=Open, 2=Closed, 3=Cancelled';

COMMIT;
