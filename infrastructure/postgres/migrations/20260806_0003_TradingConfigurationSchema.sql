-- Migracion: TradingConfigurationSchema.
-- Sprint 1 Fase 1.5A. Tablas: trading.accounts, trading.instruments.
-- Update: trading.trades agrega account_id + instrument_id (FKs).
--
-- Idempotente: todas las CREATE usan IF NOT EXISTS, los ALTER usan
-- ADD COLUMN IF NOT EXISTS. Permite correr contra volumenes fresh
-- y contra volumenes donde ya exista la migracion previa.

BEGIN;

-- ============================================
-- trading.accounts
-- ============================================
CREATE TABLE IF NOT EXISTS trading.accounts (
    id                UUID            PRIMARY KEY,
    user_id           UUID            NOT NULL,
    name              VARCHAR(80)     NOT NULL,
    broker            VARCHAR(80)     NOT NULL,
    currency          CHAR(3)         NOT NULL,
    initial_balance   NUMERIC(24,8)   NOT NULL,
    leverage          NUMERIC(10,2)   NOT NULL,
    payout_percent    NUMERIC(5,4)    NOT NULL,
    is_active         BOOLEAN         NOT NULL DEFAULT TRUE,
    created_at        TIMESTAMPTZ     NOT NULL,
    updated_at        TIMESTAMPTZ,

    CONSTRAINT ck_accounts_initial_balance_non_negative CHECK (initial_balance >= 0),
    CONSTRAINT ck_accounts_leverage_positive           CHECK (leverage > 0),
    CONSTRAINT ck_accounts_payout_percent_range        CHECK (payout_percent BETWEEN 0 AND 1),

    CONSTRAINT fk_accounts_user
        FOREIGN KEY (user_id) REFERENCES identity.users(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_accounts_user
    ON trading.accounts (user_id);
CREATE INDEX IF NOT EXISTS ix_accounts_user_active
    ON trading.accounts (user_id, is_active);

COMMENT ON TABLE trading.accounts IS 'Cuentas de trading del usuario. Un User puede tener N cuentas en distintos brokers.';
-- COMMENT de payout_percent con guard: si la columna ya no existe (fue dropeada en 0004), no falla.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = 'trading' AND table_name = 'accounts' AND column_name = 'payout_percent') THEN
        COMMENT ON COLUMN trading.accounts.payout_percent IS 'Para opciones binarias: % payout (0.85 = 85%).';
    END IF;
END $$;

-- ============================================
-- trading.instruments
-- ============================================
-- Drop old CHECK constraint si existe (idempotencia para re-aplicar el seed
-- con los nuevos bitmasks de asset_class: 0..31 en vez de 1..5).
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_instruments_asset_class') THEN
        ALTER TABLE trading.instruments DROP CONSTRAINT ck_instruments_asset_class;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS trading.instruments (
    id                UUID            PRIMARY KEY,
    symbol            VARCHAR(20)     NOT NULL,
    asset_class       SMALLINT        NOT NULL,
    contract_size     NUMERIC(24,8)   NOT NULL,
    decimal_places    INTEGER         NOT NULL,
    pip_value         NUMERIC(24,8)   NOT NULL,
    payout_percent    NUMERIC(5,4)    NOT NULL,
    is_active         BOOLEAN         NOT NULL DEFAULT TRUE,
    created_at        TIMESTAMPTZ     NOT NULL,
    updated_at        TIMESTAMPTZ,

    CONSTRAINT ck_instruments_contract_size_positive      CHECK (contract_size > 0),
    CONSTRAINT ck_instruments_decimal_places_non_negative CHECK (decimal_places >= 0),
    CONSTRAINT ck_instruments_pip_value_non_negative      CHECK (pip_value >= 0),
    CONSTRAINT ck_instruments_payout_percent_range        CHECK (payout_percent BETWEEN 0 AND 1),
    -- Sprint 1.7 FASE A: asset_class es bitmask (0..31). 0005 reescribe este CHECK
    -- en volumenes existentes y migra los rows del seed a los nuevos bitmasks.
    CONSTRAINT ck_instruments_asset_class                 CHECK (asset_class >= 0 AND asset_class <= 31)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_instruments_symbol
    ON trading.instruments (symbol);
CREATE INDEX IF NOT EXISTS ix_instruments_asset_class
    ON trading.instruments (asset_class);

COMMENT ON TABLE  trading.instruments IS 'Instrumentos de trading configurables. Compartidos entre todos los usuarios.';
COMMENT ON COLUMN trading.instruments.asset_class IS 'Bitmask: 1=Forex, 2=Crypto, 4=Binary, 8=Commodity, 16=Other. Un instrumento puede pertenecer a multiples mercados.';
COMMENT ON COLUMN trading.instruments.contract_size IS 'Tamano del contrato (e.g. 100000 para forex estandar).';
COMMENT ON COLUMN trading.instruments.pip_value     IS 'Valor del pip en el quote currency.';

-- ============================================
-- trading.instruments: Seed inicial
-- ============================================
-- Requiere pgcrypto (ya habilitado en 01-extensions.sql). Si falla por
-- "function gen_random_uuid() does not exist", hay que correr
-- CREATE EXTENSION IF NOT EXISTS pgcrypto; manualmente.
--
-- asset_class es un bitmask (Sprint 1.7 FASE A):
--   1 = Forex, 2 = Crypto, 4 = Binary, 8 = Commodity, 16 = Other.
-- 0005 ajusta el CHECK a bitmask y trae los rows existentes al nuevo modelo.
INSERT INTO trading.instruments
    (id, symbol, asset_class, contract_size, decimal_places, pip_value, payout_percent, is_active, created_at)
VALUES
    -- Forex + Binary (mismo instrumento sirve para ambos mercados)
    (gen_random_uuid(), 'EUR/USD', 5, 100000, 5, 0.0001, 0.85, TRUE, NOW()),
    (gen_random_uuid(), 'GBP/USD', 5, 100000, 5, 0.0001, 0.85, TRUE, NOW()),
    (gen_random_uuid(), 'USD/JPY', 5, 100000, 3, 0.01,  0.85, TRUE, NOW()),
    (gen_random_uuid(), 'AUD/USD', 5, 100000, 5, 0.0001, 0.85, TRUE, NOW()),
    (gen_random_uuid(), 'USD/CHF', 5, 100000, 5, 0.0001, 0.85, TRUE, NOW()),
    -- Commodity + Binary
    (gen_random_uuid(), 'XAU/USD', 9, 100,    2, 0.01,  0.85, TRUE, NOW()),
    -- Crypto (sin Binary por ahora)
    (gen_random_uuid(), 'BTC/USD', 2, 1,      2, 0.01,  0.85, TRUE, NOW()),
    (gen_random_uuid(), 'ETH/USD', 2, 1,      2, 0.01,  0.85, TRUE, NOW())
ON CONFLICT (symbol) DO NOTHING;

-- ============================================
-- trading.trades: Add account_id + instrument_id
-- ============================================
ALTER TABLE trading.trades
    ADD COLUMN IF NOT EXISTS account_id    UUID,
    ADD COLUMN IF NOT EXISTS instrument_id UUID;

-- Pre-launch: no hay produccion de datos historicos, podemos truncar.
-- Si en el futuro hay trades antiguos sin account_id, va a fallar la FK
-- y el seed necesitara un backfill script.
TRUNCATE TABLE trading.trades CASCADE;

ALTER TABLE trading.trades
    ALTER COLUMN account_id    SET NOT NULL,
    ALTER COLUMN instrument_id SET NOT NULL;

-- FKs con idempotencia via DO block (Postgres no soporta ADD CONSTRAINT IF NOT EXISTS).
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_trades_account') THEN
        ALTER TABLE trading.trades
            ADD CONSTRAINT fk_trades_account
            FOREIGN KEY (account_id) REFERENCES trading.accounts(id) ON DELETE RESTRICT;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_trades_instrument') THEN
        ALTER TABLE trading.trades
            ADD CONSTRAINT fk_trades_instrument
            FOREIGN KEY (instrument_id) REFERENCES trading.instruments(id) ON DELETE RESTRICT;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_trades_account_opened_at
    ON trading.trades (account_id, opened_at DESC);
CREATE INDEX IF NOT EXISTS ix_trades_instrument_opened_at
    ON trading.trades (instrument_id, opened_at DESC);

COMMIT;
