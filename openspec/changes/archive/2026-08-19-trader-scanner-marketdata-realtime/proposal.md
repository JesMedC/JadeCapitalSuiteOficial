# Proposal: Trader Scanner + MarketData + Realtime — Wave 4

## Intent and Problem

Wave 1-3 dieron CRUD + analytics + alertas + planner. Lo que falta es el **real-time loop**: el trader necesita ver precios en vivo, escanear oportunidades que matchean su perfil, y recibir push cuando su stop está cerca del precio actual.

Hoy la app es histórica — los datos viven en el journal post-mortem. Para que JadeCapital sea operativa, el trader tiene que poder:

1. **Ver precios live** mientras mira su watchlist. Wave 3b `CurrentPriceNearStopRule` usa `EntryPrice` como proxy porque no hay provider — eso es honesto en Wave 3, pero deja al trader a ciegas.
2. **Escanear oportunidades** sin abrir 10 pestañas de broker. Filtros por spread/volumen/R-R histórico/volatilidad, guardados por usuario.
3. **Recibir push** sin refrescar la página. SignalR para que el precio en el dashboard se mueva solo.
4. **Adjuntar archivos** (screenshot de trade, PDF de research) sin acumular basura indefinidamente. Wave 1d ya tiene `IAttachmentStorage` pero sin lifecycle.

Sin Wave 4, JadeCapital es un cuaderno reactivo. Con Wave 4, es una herramienta **operativa** que justifica Fase 5 (Pago) y Fase 6 (Multi-tenant).

## Goals

- **4a Scanner**: filtros user-owned (spread, volumen, R-R histórico, ventana de volatilidad, horarios activos) con CRUD + run ad-hoc. Devuelve `ScanResult[]` ranked por match-score.
- **4b MarketData abstraction**: `IQuoteProvider` interface en Shared.Kernel + `InMemoryQuoteProvider` stub determinístico (seed + clock). `Quote` record. Endpoint `/api/quotes/{symbol}` + bulk `/api/quotes?symbols=...`. Migración 0016 con `trading.quotes_cache`.
- **4c Realtime (SignalR)**: hub `/hubs/quotes` con subscribe/unsubscribe por symbol. `QuoteBroadcastService` (BackgroundService) corre cada 5s, lee del provider, push a `Clients.Group("symbol-{symbol}")`. Cliente FE con `@microsoft/signalr` + reconnect con backoff. `CurrentPriceNearStopRule` ahora consume `IQuoteProvider.GetQuoteAsync(symbol)` (no más EntryPrice proxy).
- **4d MinIO attachments integration**: lifecycle (cleanup 90 días via MinIO bucket policy), thumbnails para images (presigned GET con transform), quota enforcement (50MB/user total, 100 attachments/user, `413 Payload Too Large` si excede), virus scan stub (`IVirusScanner` no-op), storage usage endpoint.

## Scope Boundaries

**Changed**:
- Schema `trading`:
  - `trading.scanner_filters` (nueva) — `id, user_id, name, min_spread, max_spread, min_volume, min_risk_reward, volatility_window, active_hours JSONB, is_active, created_at, updated_at`.
  - `trading.quotes_cache` (nueva) — `symbol PK, bid, ask, spread, volume_24h, source, cached_at`.
  - `trading.trade_attachments` extiende con `thumbnail_object_key, bytes, expires_at`.
  - `trading.instruments` extiende con `last_quote_at, bid, ask, spread` (cached snapshot para join perf).
- API: 4 nuevos groups (`/api/scanner`, `/api/quotes`, `/hubs/quotes`, attachment quota/thumbnail extensions).
- Frontend: scanner page + SignalR quotes service + watchlist component live + mobile-nav entry "Scanner" (9 items con scroll horizontal).

**Unchanged**:
- Wave 1-3 features (Identity, RiskProfile, Checklist, PositionSize, PostTradeReview, Metrics, Strategies, Alerts, Planner, Journal, Patterns).
- Admin shell, Public landing, Auth.
- `Trade.Strategy` legacy string. `Trade.MfeAmount/MaeAmount` siguen calculándose como hoy.
- Existing Alert rules (sólo `CurrentPriceNearStopRule` cambia su data source — comportamiento externo idéntico).

## Capabilities (new)

- **`scanner`** — `ScannerFilter` aggregate user-owned. `ScanResult` read-only record. `ScanRunQuery` + `ScanRunHandler` ejecutan filtro contra `trading.instruments` + cached metrics (volumen, R-R histórico). CRUD de filtros + `/api/scanner/run`.
- **`marketdata`** — `IQuoteProvider` interface + `Quote` record + `InMemoryQuoteProvider` stub determinístico (seed + IClock). Migración 0016 (`trading.quotes_cache`). `/api/quotes/{symbol}` + bulk.
- **`realtime`** — SignalR hub `QuoteHub` con grupos por symbol. `QuoteBroadcastService` BackgroundService cada 5s. Frontend `@microsoft/signalr` client con reconnect+backoff. Watchlist component live.
- **`attachments-lifecycle`** — `AttachmentQuota` record (Shared.Kernel), MinIO bucket lifecycle policy (90d expiration), thumbnail endpoint, `IVirusScanner` stub, quota enforcement pre-upload (413 si excede).

## Capabilities (modified)

- **`alerts`** (Wave 3b) — `CurrentPriceNearStopRule` consume `IQuoteProvider.GetQuoteAsync(trade.Symbol)` en lugar de `EntryPrice` proxy. Comportamiento externo: la alert ahora se dispara cuando el precio REAL está cerca del stop, no cuando el entry está cerca del entry. Severity `low` se mantiene. El resto del rule (1% threshold, PII-safe copy) no cambia. Requiere delta spec en `specs/alerts/spec.md` (MODIFIED Requirement).

## Ownership and Approach

- **Shared.Kernel** owns:
  - `MarketData/Quote.cs` (record) + `MarketData/IQuoteProvider.cs` (interface) + `MarketData/QuoteSource.cs` (enum: Stub/Mock/Live/Broker).
  - `Storage/AttachmentQuota.cs` (record: MaxTotalBytes, MaxAttachmentCount, ExpirationDays).
  - `Storage/IVirusScanner.cs` (interface — default no-op impl).
- **Trading.Application** owns:
  - `Scanner/ScannerFilter.cs` aggregate + `ScanResult` record.
  - `Scanner/ScanRunHandler.cs` + 4 CRUD handlers.
  - `MarketData/QuoteCacheRepository.cs` + integration con `IQuoteProvider`.
  - `Realtime/QuoteHub.cs` integration + `EvaluateAlertsForUserHandler` update (inyectar `IQuoteProvider` en `CurrentPriceNearStopRule`).
- **Trading.Infrastructure** owns:
  - `MarketData/InMemoryQuoteProvider.cs` (determinístico seed + IClock).
  - `Realtime/QuoteHub.cs` class.
  - `Realtime/QuoteBroadcastService.cs` (BackgroundService, 5s loop).
  - `Storage/AttachmentLifecycleService.cs` (BackgroundService, daily cleanup sweep).
  - `Storage/AttachmentQuotaEnforcer.cs` (pre-upload check).
  - `Storage/VirusScannerNoOp.cs` (default impl).
  - `Storage/MinioAttachmentStore.cs` extension: `GetThumbnailAsync`.
- **Frontend** (Angular 19 standalone, Signals, OnPush, mobile-first):
  - `@microsoft/signalr` dep en `frontend/package.json`.
  - `trader/scanner/` — scanner-page + service + state + tests.
  - `trader/quotes/` — quotes.service (HTTP + SignalR wrapper) + quotes.state + quotes-signalr.service.
  - `shared/watchlist/` — `<jcs-watchlist>` reusable component (live prices via SignalR).
  - `dashboard.page.ts` modified: embed `<jcs-watchlist>` con EUR/USD, GBP/JPY, BTC/USD default.
  - `trader-shell.ts` modified: nav entry "Scanner" (icon: 'menu'). Mobile-nav scroll horizontal pasa de 8 a 9 items.
  - `trader.routes.ts` modified: `path: 'scanner'` lazy route.
- **Host wiring** (`Program.cs` + `TradingModuleRegistration`):
  - `AddSignalR()`.
  - `MapHub<QuoteHub>("/hubs/quotes")`.
  - `AddScoped<IQuoteProvider, InMemoryQuoteProvider>()`.
  - `AddSingleton<IVirusScanner, VirusScannerNoOp>()`.
  - `AddHostedService<QuoteBroadcastService>()` + `AddHostedService<AttachmentLifecycleService>()`.
  - `MapScannerEndpoints`, `MapQuoteEndpoints`.

## Non-Goals and Later Waves

- NO integración real con broker (IBKR, MT5) — Wave 6.
- NO AI signal generation — Wave 5.
- NO multi-tenant data isolation — Fase 6.
- NO PWA offline — Fase 7.
- NO WebSocket fallback (sólo SignalR) — SignalR soporta long-polling y WebSocket nativamente.
- NO push notifications nativas (iOS/Android) — Fase 7.
- NO scanner multi-timeframe simultáneo — Wave 5 introduce composite filters.
- NO thumbnail transformation server-side (sólo presigned GET con transform params de MinIO).
- NO virus scan real (ClamAV) — `IVirusScanner` es stub no-op; Wave 6 lo enchufa.

## Chained Delivery, Validation, and Rollback

| Slice (≤ 400 líneas) | Deliverable | Validate | Rollout / rollback |
|---|---|---|---|
| 4a | Migration 0017 (`trading.scanner_filters`) + `ScannerFilter` aggregate + `ScanResult` + 5 handlers + 4 endpoints + scanner-service + UI `/app/scanner` + nav entry + 15 tests | unit + integration + smoke 200/422 | Revert code; tabla inerte queda |
| 4b | Migration 0016 (`trading.quotes_cache` + `trading.instruments` columns) + `Quote` + `IQuoteProvider` + `InMemoryQuoteProvider` + 3 endpoints + tests ~10 | unit + smoke 200/404 | Revert code; tabla inerte queda |
| 4c | `QuoteHub` + `IQuoteClient` + `QuoteBroadcastService` (5s BackgroundService) + SignalR wire en `Program.cs` + integration con `CurrentPriceNearStopRule` + `@microsoft/signalr` FE + `QuotesSignalRService` + `<jcs-watchlist>` + 12 tests | unit + integration + manual `wscat` test | Revert code; hub route removida |
| 4d | `AttachmentQuota` + MinIO lifecycle policy + `AttachmentLifecycleService` daily + quota enforcement middleware + thumbnail endpoint + virus scan stub + storage usage endpoint + 10 tests | unit + smoke + curl presign | Revert code; bucket policy se mantiene (cleanup sigue corriendo pero no rompe nada) |
| 4e | E2E wiring: mobile-nav update (9 items), dashboard watchlist embed, smoke E2E desde Tailscale, tasks close | docker compose + curl + npx jest + wscat SignalR | revert code |

Pre-PR forecast: ~2,800 líneas autoradas, 5 chained PRs feature-branch-chain. **size:exception justificado por slice** (Wave 2/3 precedente: cada slice puede pasar 400 si la lógica es indivisible, justificada en apply-progress).

## Dependencies and Risks

- **Migraciones 0016/0017/0018 aditivas**: nullable columns + new tables. Idempotent. Sin NOT NULL sin backfill. Quote cache pre-existente queda vacío hasta que `QuoteBroadcastService` empieza a popular.
- **SignalR connection lifecycle**: cliente FE debe reconnect con backoff exponencial (1s → 2s → 4s → 8s, max 30s). Documentado en `QuotesSignalRService`. Si el server-side restart, los clients auto-reconnectan.
- **MinIO bucket policy**: cleanup via lifecycle expiration rule (`Days: 90`). El `AttachmentLifecycleService` es belt-and-suspenders: corre diario y limpia attachments con `expires_at < now()` en la tabla, por si MinIO bucket policy no está configurado.
- **QuoteProvider stub determinístico**: si las quotes son random, los tests no son reproducibles. El stub usa `seed = symbol.GetHashCode()` + `IClock.UtcNow.Ticks`. Tests pueden pasar un `IClock` mock para reproducible time.
- **Domain events dispatcher no existe** (Wave 2 finding): Wave 4 NO usa `INotificationHandler<T>`. `QuoteBroadcastService` poll directo via `IServiceProvider.GetRequiredService<IQuoteProvider>()`. `AttachmentLifecycleService` igual. Sin in-memory bus.
- **QuoteBroadcast periodicity**: 5s es OK para retail tick rate. Si N subscribers × 5s × M symbols = mucho traffic — mitigación: client-side coalescing (render max cada 250ms), server-side throttle (skip si quote no cambió).
- **Thumbnail generation**: MinIO soporta `?width=200&height=200` query params en el GET presigned URL. NO requiere image processing server-side. Si el bucket no tiene esa config, fallback a URL original.
- **Mobile-nav 9 items**: ya hay 8 con scroll horizontal (Wave 3 decisión). Wave 4 agrega 1 (Scanner). Sigue dentro del precedent — scroll horizontal sin drawer.

## Success Criteria

1. Trader corre `/api/scanner/run` con un saved filter y recibe `ScanResult[]` rankeados por match-score en < 500ms.
2. Trader crea un scanner filter (`POST /api/scanner/saved`), lo lista (`GET /api/scanner/saved`), lo corre (`POST /api/scanner/run`), lo borra (`DELETE /api/scanner/saved/{id}`). Cross-user isolation verificado.
3. Quote actual de `EURUSD` está disponible via `GET /api/quotes/EURUSD` y via SignalR `/hubs/quotes` con grupo `symbol-EURUSD`. Cliente FE suscribe desde `<jcs-watchlist>` y recibe `QuoteUpdate` events cada ≤ 5s.
4. Wave 3b `CurrentPriceNearStopRule` ahora dispara con precio real (verificable: mock provider devuelve precio que difiere de EntryPrice, alert se emite con copy "cerca del stop" no "cerca de entry").
5. MinIO attachments tienen lifecycle (auto-cleanup 90d via bucket policy + sweep diario). Quota: usuario no puede subir si total > 50MB o count > 100 — recibe 413. Thumbnails para images vía presigned GET con transform.
6. Build verde, 0 warnings nuevos, mobile responsive mantiene pattern Wave 3. Nav 9 items scroll horizontal. 0 regressions en los 703 tests existentes.

## Architectural Decisions

- **`IQuoteProvider` en Shared.Kernel, no en Trading**: el wire shape `Quote` es cross-module estable (lo consumen FE, BackgroundServices, Alert rules). Shared.Kernel evita acoplar Trading a una abstracción que otros módulos podrían necesitar.
- **Stub `InMemoryQuoteProvider` determinístico vs random**: random hace fallar tests de manera flaky. Determinístico con seed = `symbol.GetHashCode()` + clock ticks permite reproducir exactamente las mismas quotes en tests. Documentado en design.
- **`QuoteBroadcastService` poll vs WebSocket del provider**: el stub no expone WebSocket. El BackgroundService hace polling cada 5s del `IQuoteProvider` (que en prod sería un WebSocket client o REST polling). Esto desacopla el rate de push del rate de feed.
- **SignalR groups vs direct invocation**: grupos (`symbol-{symbol}`) permiten multicast eficiente cuando varios clients están suscritos al mismo symbol. Direct invocation por client sería O(N clients) por tick.
- **`AttachmentQuota` en Shared.Kernel, no config**: la quota es cross-module (la usa Storage, la enforcéa el handler, la reporta el endpoint). Config sería stringly-typed — un record tipado es mejor.
- **`IVirusScanner` stub no-op**: la abstracción existe desde Wave 4d, pero el impl real (ClamAV) es Wave 6. Si un attachment sube sin scan, no pasa nada por ahora — flag `VirusScannedAt = null` en la tabla para que Wave 6 pueda backfill.
- **Migration sequencing**: 3 archivos separados (0016 quotes_cache + 0017 scanner_filters + 0018 attachment_lifecycle) porque cada uno pertenece a un slice distinto y puede deployarse independiente. Aditiva + idempotent.
- **`CurrentPriceNearStopRule` cambio**: SÍ requiere delta spec en `alerts` (MODIFIED Requirement) — el comportamiento externo cambia (antes alert con copy "cerca de zona de entrada"; ahora alert con copy "cerca del stop"). El threshold (1%) y severity (low) se mantienen, pero la copy y la fuente de datos son distintas.
- **Watchlist component reusable**: `<jcs-watchlist>` vive en `shared/watchlist/` porque Wave 5 (Strategies analytics) y Wave 6 (broker integration) lo van a reusar. No hardcodear en dashboard.

## Chained Strategy

Feature-branch-chain:
- 4a → main (tracker)
- 4b → 4a
- 4c → 4b
- 4d → 4c
- 4e → 4d

OJO: cada slice produce 1-2 PRs targeteando al anterior. El primer PR de cada slice targetea `feature/0a-identity-model` (o la branch del último slice shipped).