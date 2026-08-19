-- Migration 0016 — Trader MarketData (slice 4b, Wave 4).
--
-- Anade al schema `trading`:
--   * trading.quotes_cache — cache del `IQuoteProvider` por symbol. Persistido
--     para cross-restart y para join scanner→cached quotes (Wave 4c broadcast).
--   * 4 columnas adicionales en `trading.instruments` (snapshot denormalizado
--     usado por el scanner):
--       last_quote_at TIMESTAMPTZ NULL
--       bid           NUMERIC(18,8) NULL
--       ask           NUMERIC(18,8) NULL
--       spread        NUMERIC(18,8) NULL
--
-- Reglas idempotente + additive only:
--   - CREATE TABLE IF NOT EXISTS, ADD COLUMN IF NOT EXISTS.
--   - PK symbol (1 fila por symbol). Sin ON DELETE CASCADE — los quotes son
--     globales (no per-user), y un cleanup eventual los borra por symbol.
--   - source SMALLINT matches `QuoteSource` enum (0=Stub, 1=Mock, 2=Live, 3=Broker).
--   - cached_at defaulted to now() — callers may overwrite.
--   - NO backfill: la cache queda vacia hasta que `QuoteBroadcastService`
--     (Wave 4c) o la primera llamada a `GET /api/quotes/*` escriban una fila.

BEGIN;

CREATE TABLE IF NOT EXISTS trading.quotes_cache (
    symbol      VARCHAR(20)   PRIMARY KEY,
    bid         NUMERIC(18,8) NOT NULL,
    ask         NUMERIC(18,8) NOT NULL,
    spread      NUMERIC(18,8) NOT NULL,
    volume_24h  NUMERIC(24,8) NOT NULL,
    source      SMALLINT      NOT NULL DEFAULT 0,
    cached_at   TIMESTAMPTZ   NOT NULL DEFAULT now()
);

ALTER TABLE trading.instruments
    ADD COLUMN IF NOT EXISTS last_quote_at TIMESTAMPTZ    NULL,
    ADD COLUMN IF NOT EXISTS bid           NUMERIC(18,8)  NULL,
    ADD COLUMN IF NOT EXISTS ask           NUMERIC(18,8)  NULL,
    ADD COLUMN IF NOT EXISTS spread        NUMERIC(18,8)  NULL;

COMMENT ON TABLE  trading.quotes_cache         IS 'Trader market data quote cache (slice 4b, Wave 4). Source of truth for the IQuoteProvider read-through path.';
COMMENT ON COLUMN trading.quotes_cache.source  IS 'QuoteSource enum: 0=Stub, 1=Mock, 2=Live, 3=Broker.';
COMMENT ON COLUMN trading.instruments.bid      IS 'Last known bid (snapshot denormalized from trading.quotes_cache; refreshed by QuoteBroadcastService in Wave 4c).';
COMMENT ON COLUMN trading.instruments.ask      IS 'Last known ask (snapshot denormalized from trading.quotes_cache).';
COMMENT ON COLUMN trading.instruments.spread   IS 'Last known spread (snapshot; ask - bid at last refresh).';
COMMENT ON COLUMN trading.instruments.last_quote_at IS 'Wall-clock of the last cached quote for this instrument (NULL until first refresh).';

COMMIT;
