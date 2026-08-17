# MarketData Specification

## Purpose

A read-only market data abstraction over a pluggable quote provider. The system exposes `Quote` records (bid, ask, spread, volume, timestamp, source) via REST endpoints and (via Wave 4c) a SignalR hub. The default provider is an in-memory deterministic stub — seed-driven and clock-driven — so tests reproduce exactly the same quotes. Wave 6 will swap the provider for a real broker integration without touching consumers.

This spec covers the data model and the HTTP surface. Realtime push is documented in `openspec/changes/.../specs/realtime/spec.md`.

## Requirements

### Requirement: Quote record shape

A `Quote` MUST be a value record `{ Symbol, Bid, Ask, Spread, Volume24h, Timestamp, Source }` where `Bid`, `Ask`, `Spread` are `decimal`; `Volume24h` is `decimal`; `Timestamp` is `DateTimeOffset`; `Source` is a `QuoteSource` enum (`Stub, Mock, Live, Broker`).

#### Scenario: Quote serialization

- GIVEN a `Quote { Symbol: "EURUSD", Bid: 1.0850m, Ask: 1.0851m, Spread: 0.0001m, Volume24h: 150000m, Timestamp: 2026-08-19T14:32:00Z, Source: Stub }`
- WHEN the quote is serialized to JSON
- THEN the response MUST include all seven fields with the exact values
- AND `Spread` MUST equal `Ask - Bid` (within 0.0001 tolerance)

### Requirement: Quote provider interface

The system MUST define `IQuoteProvider` in `Shared.Kernel.MarketData` with two methods: `Task<Quote?> GetQuoteAsync(string symbol, CancellationToken ct)` and `Task<IReadOnlyList<Quote>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken ct)`. `GetQuoteAsync` returns `null` if the symbol is unknown. `GetQuotesAsync` returns only the known symbols (silently drops unknowns).

#### Scenario: GetQuoteAsync known symbol

- GIVEN the in-memory provider has been seeded with EURUSD and GBPJPY
- WHEN `GetQuoteAsync("EURUSD")` is called
- THEN the response MUST be a non-null `Quote` with `Symbol == "EURUSD"`

#### Scenario: GetQuoteAsync unknown symbol

- GIVEN the in-memory provider does not know ZZZZZ
- WHEN `GetQuoteAsync("ZZZZZ")` is called
- THEN the response MUST be `null`

#### Scenario: Bulk fetch drops unknowns

- GIVEN EURUSD and GBPJPY exist, ZZZZZ does not
- WHEN `GetQuotesAsync(["EURUSD", "ZZZZZ", "GBPJPY"])` is called
- THEN the response MUST contain exactly 2 quotes (EURUSD + GBPJPY)
- AND ZZZZZ MUST be silently dropped (no exception)

### Requirement: Deterministic in-memory provider

The default `InMemoryQuoteProvider` MUST be deterministic: the same `(symbol, IClock.UtcNow.Ticks)` MUST produce the same `Quote`. The seed MUST be derived from `symbol.GetHashCode()`. Tests MUST be able to reproduce any quote by passing an `IClock` mock with a fixed UtcNow.

#### Scenario: Same symbol + same time → same quote

- GIVEN `IClock` returns `2026-08-19T14:00:00Z`
- WHEN `GetQuoteAsync("EURUSD")` is called twice with the same clock
- THEN both calls MUST return equal `Quote` records (Bid, Ask, Spread identical)

#### Scenario: Different time → different quote

- GIVEN the clock advances from `14:00:00Z` to `14:00:05Z`
- WHEN `GetQuoteAsync("EURUSD")` is called at each time
- THEN the Bid/Ask MUST differ (deterministic walk)

#### Scenario: Symbol casing

- GIVEN EURUSD is seeded
- WHEN `GetQuoteAsync("eurusd")` is called (lowercase)
- THEN the response MUST be non-null (case-insensitive lookup)

### Requirement: HTTP GET single quote

`GET /api/quotes/{symbol}` MUST return 200 + `Quote` JSON or 404 if unknown. Case-insensitive.

#### Scenario: Get EURUSD

- GIVEN the provider has EURUSD
- WHEN `GET /api/quotes/EURUSD` is called with valid Bearer
- THEN the response MUST be 200 + `Quote` JSON

#### Scenario: Unknown symbol returns 404

- GIVEN the provider does not know ZZZZZ
- WHEN `GET /api/quotes/ZZZZZ` is called
- THEN the response MUST be 404 with `error.code = "quote.not_found"`

### Requirement: HTTP GET bulk quotes

`GET /api/quotes?symbols=EURUSD,GBPJPY,BTCUSD` MUST return 200 + `Quote[]`. Symbols are comma-separated. Unknown symbols are silently dropped (no error in the array — just omitted).

#### Scenario: Bulk fetch

- GIVEN the provider has EURUSD, GBPJPY, BTCUSD, USDCAD
- WHEN `GET /api/quotes?symbols=EURUSD,GBPJPY,UNKNOWN` is called
- THEN the response MUST be 200 with 2 quotes (EURUSD, GBPJPY)
- AND UNKNOWN MUST be dropped

#### Scenario: Bulk fetch empty

- GIVEN the provider has no symbols matching the request
- WHEN `GET /api/quotes?symbols=UNKNOWN1,UNKNOWN2` is called
- THEN the response MUST be 200 + `[]`

### Requirement: Quote cache table

A `trading.quotes_cache` table MUST persist the most recent quote per symbol for cross-restart persistence and for the `trading.instruments` join used by the scanner. The table MUST be updated by `QuoteBroadcastService` (Wave 4c) and read by `GET /api/quotes/*` as a fast-path before the provider.

#### Scenario: Cache hit on second request

- GIVEN the cache has EURUSD with bid=1.0850 from 5 seconds ago
- WHEN `GET /api/quotes/EURUSD` is called
- THEN the response MUST be served from cache (no provider call)
- AND the response Timestamp MUST equal the cached timestamp

#### Scenario: Cache miss → provider fetch → cache write

- GIVEN the cache is empty for EURUSD
- WHEN `GET /api/quotes/EURUSD` is called
- THEN the provider MUST be invoked
- AND the resulting Quote MUST be written to `trading.quotes_cache`

#### Scenario: Stale cache refresh

- GIVEN the cache has EURUSD with `cached_at = now - 60s`
- WHEN `GET /api/quotes/EURUSD` is called
- THEN the provider MUST be invoked (stale > 30s)
- AND the cache MUST be updated with the fresh quote

## Data Model

```
trading.quotes_cache
  symbol        VARCHAR(20) PK
  bid           NUMERIC(18,8) NOT NULL
  ask           NUMERIC(18,8) NOT NULL
  spread        NUMERIC(18,8) NOT NULL
  volume_24h    NUMERIC(24,8) NOT NULL
  source        SMALLINT NOT NULL DEFAULT 0  -- matches QuoteSource enum
  cached_at     TIMESTAMPTZ NOT NULL DEFAULT now()

trading.instruments (additive columns)
  last_quote_at TIMESTAMPTZ NULL
  bid           NUMERIC(18,8) NULL
  ask           NUMERIC(18,8) NULL
  spread        NUMERIC(18,8) NULL
```

The `trading.instruments` extension is a cached snapshot (denormalized) used by the scanner's join. The source of truth is `trading.quotes_cache`; the snapshot is refreshed periodically by `QuoteBroadcastService`.

## Endpoints

- `GET /api/quotes/{symbol}` — single quote.
- `GET /api/quotes?symbols=A,B,C` — bulk quote.
- `GET /api/quotes/_internal/cache` — DEV-ONLY: dump the cache (not exposed in prod profile).

All require `RequireAuthorization` and `api-quotes` rate limit (a higher rate limit than `api-general` because watchlist pages call this frequently).

## Architecture

- **Shared.Kernel/MarketData/Quote.cs** — record (cross-module wire shape).
- **Shared.Kernel/MarketData/IQuoteProvider.cs** — interface.
- **Shared.Kernel/MarketData/QuoteSource.cs** — enum.
- **Trading.Infrastructure/MarketData/InMemoryQuoteProvider.cs** — default impl (deterministic seed + IClock).
- **Trading.Infrastructure/MarketData/QuoteCacheRepository.cs** — read/write `trading.quotes_cache`.
- **Trading.Application/MarketData/GetQuoteHandler.cs** + `GetQuotesBulkHandler.cs`.
- **Trading.Api/Endpoints/QuoteEndpoints.cs** — `MapQuoteEndpoints`.