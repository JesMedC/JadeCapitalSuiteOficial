-- Migracion: Instrument.AssetClasses ahora es un [Flags] enum (bitmask).
-- Sprint 1.7 FASE A.
--
-- Hasta 0004 la columna asset_class se validaba como 1..5 (un unico valor).
-- Desde 0005 admite cualquier bitmask de los flags definidos:
--   0  = None      (no se permite persistir: dominio lo rechaza)
--   1  = Forex
--   2  = Crypto
--   4  = Binary
--   8  = Commodity
--   16 = Other
--   31 = Forex|Crypto|Binary|Commodity|Other  (todos los bits)
-- En instrumentos: 0..31 (cualquier combinacion valida; 0 = None esta
-- bloqueado por la validacion del dominio, no por el CHECK).
-- En trades: 1..16 (un solo bit, derivado de Account.MarketType).
--
-- Tambien actualizamos los rows del seed inicial para que los instrumentos
-- existentes reflejen el nuevo modelo (Forex+Binary, Commodity+Binary, Crypto).
-- Idempotente: los UPDATEs son no-op si la columna ya tiene el valor.

BEGIN;

-- ============================================
-- 1) CHECK de instrumentos: aceptar bitmask (0..31).
-- ============================================
ALTER TABLE trading.instruments
    DROP CONSTRAINT IF EXISTS ck_instruments_asset_class;

ALTER TABLE trading.instruments
    ADD CONSTRAINT ck_instruments_asset_class
    CHECK (asset_class >= 0 AND asset_class <= 31);

-- ============================================
-- 2) CHECK de trades: un solo flag (1, 2, 4, 8 o 16).
-- ============================================
ALTER TABLE trading.trades
    DROP CONSTRAINT IF EXISTS ck_trades_asset_class;

ALTER TABLE trading.trades
    ADD CONSTRAINT ck_trades_asset_class
    CHECK (asset_class >= 1 AND asset_class <= 16);

-- ============================================
-- 3) Actualizar el seed inicial a los nuevos bitmasks.
-- Idempotente: si el row ya tiene el valor, el UPDATE es no-op.
-- ============================================
UPDATE trading.instruments SET asset_class = 5  WHERE symbol = 'EUR/USD' AND asset_class <> 5;  -- Forex|Binary
UPDATE trading.instruments SET asset_class = 5  WHERE symbol = 'GBP/USD' AND asset_class <> 5;  -- Forex|Binary
UPDATE trading.instruments SET asset_class = 5  WHERE symbol = 'USD/JPY' AND asset_class <> 5;  -- Forex|Binary
UPDATE trading.instruments SET asset_class = 5  WHERE symbol = 'AUD/USD' AND asset_class <> 5;  -- Forex|Binary
UPDATE trading.instruments SET asset_class = 5  WHERE symbol = 'USD/CHF' AND asset_class <> 5;  -- Forex|Binary
UPDATE trading.instruments SET asset_class = 9  WHERE symbol = 'XAU/USD' AND asset_class <> 9;  -- Commodity|Binary
UPDATE trading.instruments SET asset_class = 2  WHERE symbol = 'BTC/USD' AND asset_class <> 2;  -- Crypto
UPDATE trading.instruments SET asset_class = 2  WHERE symbol = 'ETH/USD' AND asset_class <> 2;  -- Crypto

-- ============================================
-- 4) Comentarios
-- ============================================
COMMENT ON COLUMN trading.instruments.asset_class IS 'Bitmask: 1=Forex, 2=Crypto, 4=Binary, 8=Commodity, 16=Other. Un instrumento puede pertenecer a multiples mercados.';
COMMENT ON COLUMN trading.trades.asset_class      IS 'Single flag: 1=Forex, 2=Crypto, 4=Binary, 8=Commodity, 16=Other. Deriva de Account.MarketType.';

COMMIT;
