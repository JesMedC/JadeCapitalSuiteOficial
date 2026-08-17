# Realtime Specification

## Purpose

Real-time quote streaming over SignalR. The server pushes quote updates to subscribed clients grouped by symbol. Clients subscribe/unsubscribe to symbols via the hub; the server's `QuoteBroadcastService` polls the `IQuoteProvider` every 5 seconds and broadcasts to the relevant groups. This spec covers the hub contract, the broadcast loop, and the client-side lifecycle (reconnect with backoff).

The realtime layer complements the synchronous HTTP endpoints in `marketdata` (Wave 4b). HTTP is the request/response fallback when the SignalR connection is down or unavailable.

## Requirements

### Requirement: QuoteHub contract

The server MUST expose `/hubs/quotes` (SignalR hub) with two server-callable methods: `SubscribeToSymbols(IEnumerable<string> symbols)` and `UnsubscribeFromSymbols(IEnumerable<string> symbols)`. The server MUST group connections by `symbol-{SYMBOL}` (uppercase). The hub MUST be reachable from authenticated users only.

#### Scenario: Connect to hub

- GIVEN an authenticated trader with a valid Bearer token
- WHEN the client opens a SignalR connection to `/hubs/quotes?access_token=...`
- THEN the connection MUST succeed (101 Switching Protocols)
- AND `OnConnectedAsync` MUST register the connection in the user's scope

#### Scenario: Anonymous connection rejected

- GIVEN an unauthenticated client
- WHEN a SignalR connection to `/hubs/quotes` is attempted
- THEN the connection MUST be rejected with 401

### Requirement: Client interface

The hub MUST invoke `OnQuoteUpdate(Quote quote)` on the client for each subscribed symbol. On protocol errors or invalid subscriptions, the hub MUST invoke `OnError(string code, string message)`.

#### Scenario: QuoteUpdate delivery

- GIVEN client C has subscribed to EURUSD
- WHEN the broadcast service pushes a new EURUSD quote
- THEN client C MUST receive an `OnQuoteUpdate` invocation with the new `Quote` JSON

#### Scenario: No delivery for unsubscribed symbol

- GIVEN client C has subscribed only to EURUSD
- WHEN the broadcast service pushes a GBPJPY quote
- THEN client C MUST NOT receive an `OnQuoteUpdate` for GBPJPY

### Requirement: Grouping by symbol

Each `(connectionId, symbol)` subscription MUST add the connection to the `symbol-{SYMBOL}` group. The server MUST use `Clients.Group("symbol-{SYMBOL}").SendAsync(...)` to multicast. A single broadcast call MUST reach all subscribers of a symbol.

#### Scenario: Multiple subscribers to same symbol

- GIVEN client A and client B both subscribe to EURUSD
- WHEN the broadcast service pushes a EURUSD quote
- THEN both A and B MUST receive an `OnQuoteUpdate` invocation

#### Scenario: One subscriber per symbol

- GIVEN client A subscribes to EURUSD; client B subscribes to GBPJPY
- WHEN the broadcast service pushes EURUSD
- THEN ONLY client A MUST receive the update
- AND client B MUST NOT receive anything

#### Scenario: Symbol grouping is case-insensitive

- GIVEN client subscribes to "eurusd"
- WHEN the broadcast service pushes a EURUSD quote (uppercase symbol)
- THEN the client MUST receive the update (lookup is normalized to uppercase)

### Requirement: Broadcast loop

A `QuoteBroadcastService` (BackgroundService) MUST run every 5 seconds (±500ms jitter), poll the `IQuoteProvider.GetQuotesAsync(symbols, ct)` for the set of actively-subscribed symbols, and push each new quote to the corresponding SignalR group. Idempotency: if the quote is unchanged from the previous tick for that symbol, the service MUST skip the broadcast (no-op).

#### Scenario: 5s tick

- GIVEN the service starts and finds 3 subscribed symbols (EURUSD, GBPJPY, BTCUSD)
- WHEN 5 seconds elapse
- THEN the service MUST call `IQuoteProvider.GetQuotesAsync` with those 3 symbols
- AND push each quote to the respective group

#### Scenario: No subscribers

- GIVEN there are 0 subscribed symbols
- WHEN the tick fires
- THEN the service MUST skip the provider call (no-op)
- AND MUST NOT log a warning

#### Scenario: Unchanged quote → skip

- GIVEN EURUSD's last pushed bid was 1.0850
- AND the provider now returns bid=1.0850 (unchanged)
- WHEN the tick fires
- THEN the service MUST NOT invoke `OnQuoteUpdate` for EURUSD (changed-detection prevents noise)

### Requirement: Disconnect cleanup

When a client disconnects (graceful or due to timeout), the server MUST remove the connection from all groups it had joined. The broadcast service MUST NOT attempt to send to dead connections (SignalR handles this, but the service MUST tolerate throws).

#### Scenario: Graceful disconnect

- GIVEN client C subscribed to EURUSD, GBPJPY
- WHEN the client closes the connection
- THEN the connection MUST be removed from both groups
- AND subsequent broadcasts MUST NOT include client C

#### Scenario: Connection timeout

- GIVEN client C subscribed to EURUSD with no activity for 60s
- WHEN SignalR times out the connection
- THEN the server MUST clean up C's group memberships
- AND broadcasts MUST NOT throw (must skip dead connections)

### Requirement: Client reconnect with backoff

The client-side `QuotesSignalRService` MUST implement automatic reconnect with exponential backoff: start at 1s, double up to 30s max, retry indefinitely. On successful reconnect, the service MUST re-subscribe to all previously subscribed symbols. On disconnect, the service MUST mark quotes as `stale` so the UI shows "disconnected" state.

#### Scenario: Transient disconnect → auto-reconnect

- GIVEN the client is subscribed to EURUSD
- WHEN the network drops for 3 seconds and recovers
- THEN the service MUST reconnect within the backoff window
- AND MUST re-subscribe to EURUSD
- AND `OnQuoteUpdate` events MUST resume

#### Scenario: Persistent disconnect → exponential backoff

- GIVEN the client is subscribed to EURUSD
- WHEN the server is unreachable for 60s
- THEN the service MUST retry with intervals: 1s, 2s, 4s, 8s, 16s, 30s, 30s, 30s, ...
- AND MUST NOT crash the host app

#### Scenario: Manual unsubscribe

- GIVEN client is subscribed to EURUSD and GBPJPY
- WHEN the user clicks "unsubscribe EURUSD"
- THEN `QuotesSignalRService.unsubscribe("EURUSD")` MUST call `UnsubscribeFromSymbols(["EURUSD"])`
- AND subsequent EURUSD ticks MUST NOT reach the client
- AND GBPJPY ticks MUST still arrive

## Data Model

No new tables. The hub holds group membership in memory; the broadcast loop reads subscribed symbols from `IHubContext<QuoteHub>.Groups`. Connection state is ephemeral; server restart drops all subscriptions (clients reconnect and re-subscribe).

## Client Contract

```typescript
interface IQuoteClient {
  onQuoteUpdate(quote: Quote): void;
  onError(code: string, message: string): void;
}

interface Quote {
  symbol: string;
  bid: number;
  ask: number;
  spread: number;
  volume24h: number;
  timestamp: string;  // ISO-8601
  source: 'Stub' | 'Mock' | 'Live' | 'Broker';
}
```

## Architecture

- **Trading.Api/Hubs/QuoteHub.cs** — `Hub<IQuoteClient>` with `SubscribeToSymbols` + `UnsubscribeFromSymbols`.
- **Trading.Infrastructure/Realtime/QuoteBroadcastService.cs** — `BackgroundService` with 5s loop + scope factory + change detection.
- **Shared.Infrastructure/Realtime/HubConnectionFactory.cs** (optional) — DI helper.
- **Frontend**:
  - `trader/quotes/api/quotes-signalr.service.ts` — `@microsoft/signalr` `HubConnection` wrapper with reconnect-with-backoff.
  - `trader/quotes/state/quotes.state.ts` — Signals: `quotes$ = signal<Map<string, Quote>>`, `subscribedSymbols`, `connectionStatus`.
  - `shared/watchlist/jcs-watchlist.component.ts` — standalone OnPush component that subscribes to a configurable list of symbols and renders live prices.
  - `dashboard.page.ts` — embed `<jcs-watchlist [symbols]="['EURUSD','GBPJPY','BTCUSD']">`.
- **Host wiring** (`Program.cs`):
  - `builder.Services.AddSignalR();`
  - `app.MapHub<QuoteHub>("/hubs/quotes");`
  - `builder.Services.AddHostedService<QuoteBroadcastService>();`
- **CORS**: SignalR endpoints MUST be added to `Cors__Origins__N` config (the existing CORS policy covers SignalR if origins are listed).

## Endpoints

- `WS /hubs/quotes` — SignalR hub endpoint (WebSocket transport; long-polling fallback).
- HTTP fallback: clients can also poll `GET /api/quotes?symbols=...` if the SignalR connection is unavailable.

All hub interactions require an authenticated user. The access token is passed via query string (`?access_token=...`) because SignalR cannot send custom headers on WebSocket upgrade.