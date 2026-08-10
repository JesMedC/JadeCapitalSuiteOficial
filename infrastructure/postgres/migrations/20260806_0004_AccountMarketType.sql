BEGIN;

-- Agregar market_type a trading.accounts
ALTER TABLE trading.accounts
    ADD COLUMN IF NOT EXISTS market_type SMALLINT;

-- Truncar para evitar problemas con datos existentes (pre-launch)
UPDATE trading.accounts SET market_type = 1 WHERE market_type IS NULL;

-- Hacer market_type NOT NULL con default Forex (1) para futuros inserts
ALTER TABLE trading.accounts
    ALTER COLUMN market_type SET NOT NULL,
    ALTER COLUMN market_type SET DEFAULT 1;

-- CHECK constraint para market_type (idempotente via DO block).
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_accounts_market_type') THEN
        ALTER TABLE trading.accounts
            ADD CONSTRAINT ck_accounts_market_type CHECK (market_type IN (1, 2));
    END IF;
END $$;

-- Drop payout_percent del Account (ahora vive solo en Instrument).
-- Truncar para evitar conflicto con FKs.
ALTER TABLE trading.accounts DROP COLUMN IF EXISTS payout_percent;

-- Drop el CHECK constraint que ya no aplica
ALTER TABLE trading.accounts DROP CONSTRAINT IF EXISTS ck_accounts_payout_percent_range;

-- Leverage ahora nullable para Binary (default 1.0 en Application layer)
ALTER TABLE trading.accounts ALTER COLUMN leverage DROP NOT NULL;

-- Drop el CHECK de leverage positivo: Binary permite null, Forex valida en app.
ALTER TABLE trading.accounts DROP CONSTRAINT IF EXISTS ck_accounts_leverage_positive;

COMMENT ON COLUMN trading.accounts.market_type IS '1=Forex (con apalancamiento), 2=Binary (opciones binarias con payout)';
COMMENT ON COLUMN trading.accounts.leverage IS 'Solo aplica para Forex. Nullable para Binary (default 1.0).';

COMMIT;
