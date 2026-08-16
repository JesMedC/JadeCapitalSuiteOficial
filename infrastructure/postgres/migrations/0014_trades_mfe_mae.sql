-- Migracion 0014 — MFE/MAE Charts (slice 2c de 2026-08-17-trader-journal-core).
--
-- Anade al schema `trading`:
--   * trading.trades.mfe_amount     NUMERIC(24,8) NULL — Maximum Favorable
--     Excursion en account currency. 0 cuando el trade es perdedor (no
--     podemos saber el high sin tick data en Wave 2); NULL cuando el trade
--     esta Open. Computado sincrónicamente en Trade.Close() via
--     MfeMaeCalculator (mismo aggregate, mismo SaveChanges, misma transacción).
--   * trading.trades.mae_amount     NUMERIC(24,8) NULL — Maximum Adverse
--     Excursion (≤ 0). Espejo del anterior.
--   * trading.trades.mfe_currency   CHAR(3) NULL — codigo ISO 4217-like.
--   * trading.trades.mae_currency   CHAR(3) NULL.
--
-- Reglas de la migracion (idempotente, additive only):
--   - ALTER TABLE ... ADD COLUMN IF NOT EXISTS.
--   - NO DROP, NO ALTER destructivo (rollback = DROP COLUMN manual).
--   - Nullable: open trades tienen MFE/MAE = NULL (no aplica).
--   - Sin CHECK constraints de signo en la DB: la invariante de signo vive
--     en el dominio (MfeMustBeNonNegative / MaeMustBeNonPositive). El calculo
--     aproximado es 0 cuando el campo opuesto corresponde (MFE=0 en losers,
--     MAE=0 en winners) — la DB no necesita enforce numerico.
--   - Sin indices nuevos: las queries de aggregate usan
--     ix_trades_user_opened_at / ix_trades_user_status ya existentes.
--
-- Origen de la fecha: 2026-08-17 (sprint Wave 2 / slice 2c).

BEGIN;

ALTER TABLE trading.trades
    ADD COLUMN IF NOT EXISTS mfe_amount NUMERIC(24,8) NULL;

ALTER TABLE trading.trades
    ADD COLUMN IF NOT EXISTS mae_amount NUMERIC(24,8) NULL;

ALTER TABLE trading.trades
    ADD COLUMN IF NOT EXISTS mfe_currency CHAR(3) NULL;

ALTER TABLE trading.trades
    ADD COLUMN IF NOT EXISTS mae_currency CHAR(3) NULL;

-- Comentarios.
COMMENT ON COLUMN trading.trades.mfe_amount IS 'Maximum Favorable Excursion (Wave 2 approximation: P&L si winner, 0 si loser, NULL si open). Computed sync en Trade.Close() via MfeMaeCalculator.';
COMMENT ON COLUMN trading.trades.mae_amount IS 'Maximum Adverse Excursion (Wave 2 approximation: -|P&L| si loser, 0 si winner, NULL si open). Computed sync en Trade.Close() via MfeMaeCalculator.';
COMMENT ON COLUMN trading.trades.mfe_currency IS 'ISO 4217-like 3-letter currency code for mfe_amount. Matchea account currency por invariante del dominio.';
COMMENT ON COLUMN trading.trades.mae_currency IS 'ISO 4217-like 3-letter currency code for mae_amount.';

COMMIT;