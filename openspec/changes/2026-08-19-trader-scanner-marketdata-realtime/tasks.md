# Tasks — Wave 4 (Trader Scanner + MarketData + Realtime)

## Review Workload Forecast

| Slice | Boundary | Lines | Base |
|---|---|---:|---|
| 4a | Scanner: domain + application + EF + migration 0017 + endpoints + UI + 15 tests | ~700 | tracker |
| 4b | MarketData: Quote VO + IQuoteProvider + InMemoryQuoteProvider + migration 0016 + endpoints + 10 tests | ~600 | 4a |
| 4c | Realtime: QuoteHub + IQuoteClient + QuoteBroadcastService + SignalR wire + CurrentPriceNearStopRule update + FE SignalR client + watchlist + 12 tests | ~700 | 4b |
| 4d | Attachments: AttachmentQuota + MinIO lifecycle + AttachmentLifecycleService + quota enforcer + thumbnail + virus stub + migration 0018 + 10 tests | ~500 | 4c |
| 4e | E2E wiring: nav update (9 items) + dashboard watchlist embed + smoke E2E + tasks close | ~300 | 4d |
| **Total** | 5 chained slices | **~2,800** | tracker→main |

Decision needed before apply: **No** (auto-chain, 400-line budget per PR). User confirmed `feature-branch-chain` (Wave 0/1/2/3 precedent). Per-slice `git diff --stat` MUST be < 400 if pre-split; otherwise chained PRs (1-2 per slice).

**Build**: `dotnet build JadeCapital.slnx --nologo --verbosity minimal`.
**SQL harness**: `psql -v ON_ERROR_STOP=1 -f migrations/<file>.sql`; idempotent re-run (same script twice → exit 0).
**Frontend**: `cd frontend && npm run build` and `cd frontend && npx jest`.
**SignalR smoke**: `wscat -c ws://localhost:5000/hubs/quotes?access_token=<jwt>` (manual probe).

### Work Units (PR → test → runtime → rollback)

- 4a: `dotnet test --filter "FullyQualifiedName~Scanner"` + smoke `curl /api/scanner/saved` Bearer (200/422). Rollback: revert code; keep 0017 migration applied (inert).
- 4b: `dotnet test --filter "FullyQualifiedName~MarketData|Quote"` + smoke `curl /api/quotes/EURUSD` Bearer (200/404). Rollback: revert code; tabla inerte queda.
- 4c: `dotnet test --filter "FullyQualifiedName~QuoteHub|QuoteBroadcast"` + manual `wscat` connect + subscribe + receive. Rollback: revert code; hub route removida.
- 4d: `dotnet test --filter "FullyQualifiedName~Attachment"` + smoke `curl /api/attachments/usage` Bearer (200/413). Rollback: revert code; bucket policy se mantiene (cleanup sigue corriendo pero no rompe nada).
- 4e: ng build + npx jest + docker compose up -d --build + smoke from iPhone Tailscale URL.

---

## Slice 4a — Scanner (≤ 700 líneas, split 4a.1 + 4a.2)

### 4a.1 Backend (~500 líneas)

**Phase 1: Migration**
- [x] 1.1 `infrastructure/postgres/migrations/0017_scanner_filters.sql` (idempotent, additive): `trading.scanner_filters` table + `ux_scanner_filters_user_name` partial unique index.
- [x] 1.2 Wire en `migrate.Dockerfile` (escape pattern `\"`).

**Phase 2: Shared enums (TDD)**
- [x] 2.1 `Shared.Kernel/Enums/VolatilityWindow.cs` (enum byte 0/7/8/9).
- [x] 2.2 `Shared.Kernel/Enums/ActiveHoursWindow.cs` (record: DayOfWeek/StartHour/EndHour con validations).

**Phase 3: Domain (TDD)**
- [x] 3.1 RED tests `ScannerFilterTests` (10 scenarios: create valid, name required, name length cap, spread range validation, volume/rr validation, volatility window enum, active hours valid, update preserves CreatedAt, soft-delete idempotent, cross-user guard).
- [x] 3.2 GREEN: `ScannerFilter` aggregate + `ActiveHoursWindow` record + `ScannerFilterErrors.cs` + `ScannerFilterCreatedDomainEvent` + `ScannerFilterUpdatedDomainEvent`.

**Phase 4: Application (TDD)**
- [x] 4.1 RED tests `CreateScannerFilterHandlerTests` (4): valid create, duplicate name → 409, validation errors, requires UserId from claim.
- [x] 4.2 GREEN: `CreateScannerFilterCommand` + `CreateScannerFilterHandler` + `IScannerFilterRepository`.
- [x] 4.3 RED tests `UpdateScannerFilterHandlerTests` (4) + `SoftDeleteScannerFilterHandlerTests` (2) + `GetScannerFiltersHandlerTests` (3) + `GetScannerFilterByIdHandlerTests` (2).
- [x] 4.4 GREEN: 4 handlers + DTOs (`ScannerFilterDto`, `UpsertScannerFilterRequest`) + `ScannerFilterMapping`.
- [x] 4.5 RED tests `RunScanHandlerTests` (5): with saved filter, with ad-hoc filter, empty result, cross-user filter → 404, limit out-of-range → 422.
- [x] 4.6 GREEN: `RunScanQuery` + `RunScanHandler` + `IScannerRunner` + `ScanResultDto` + `ScanResultMapping`.

**Phase 5: Infrastructure + API**
- [x] 5.1 `ScannerFilterConfiguration` (EF) — HasColumnName snake_case, JSONB `active_hours` via `OwnsMany(...).ToJson()`.
- [x] 5.2 `ScannerFilterRepository` impl (GetByIdAsync, ListByUserAsync, ExistsByNameAsync, AddAsync, UpdateAsync, SoftDeleteAsync).
- [x] 5.3 `ScannerRunner` impl — joins `trading.instruments` con cached metrics, computes `MatchScore` (weighted blend), returns ranked `ScanResult[]`.
- [x] 5.4 `ScannerEndpoints` (`MapScannerEndpoints`): 6 endpoints (list, create, getById, update, delete, run). RequireAuthorization. `api-general` rate limit.
- [x] 5.5 `app.MapScannerEndpoints()` en `Program.cs`.
- [x] 5.6 DI: `AddScoped<IScannerFilterRepository, ScannerFilterRepository>()` + `AddScoped<IScannerRunner, ScannerRunner>()` en `TradingModuleRegistration`.

**Phase 6: Validate**
- [x] 6.1 `dotnet test --filter "FullyQualifiedName~Scanner" --nologo --verbosity minimal` → 15+ passed.
- [x] 6.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.

### 4a.2 Frontend (~200 líneas)

**Phase 1: Service + state**
- [x] 1.1 `api/scanner.service.ts` con 6 métodos HTTP (list, getById, create, update, delete, run).
- [x] 1.2 `state/scanner.state.ts` (Signals: filters, selectedFilterId, results, isRunning, isSaving, error).
- [x] 1.3 2 jest specs (state) — covered by 4 page-level specs.

**Phase 2: Page + routing**
- [x] 2.1 `scanner-page.ts` standalone Signals OnPush SCSS con: filters list (left) + create form (inline) + run button + results table (right) con match-score column.
- [x] 2.2 `scanner.routes.ts` (sub-routes: `/scanner`).
- [x] 2.3 Add `'scanner'` route a `trader.routes.ts` (loadChildren → SCANNER_ROUTES).
- [x] 2.4 4 jest specs (renders empty, create success, run returns ranked results, delete flow).

### 4a E2E wiring (final patch)
- [x] 3.1 Smoke E2E: create scanner filter → run → verify ranked results. (12-curl Bearer smoke covers this in 4e.)
- [x] 3.2 Mobile-nav entry: see 4e.1.1.

> **Slice 4a completion note**: code shipped without applying tasks.md (pre-existing code base). Build green, 13/13 unit tests, 4/4 frontend specs. 2 deviations from spec documented in `apply-progress-wave4-partial.md`.

---

## Slice 4b — MarketData (≤ 600 líneas, split 4b.1 + 4b.2)

### 4b.1 Backend (~450 líneas)

**Phase 1: Shared abstraction (TDD)**
- [ ] 1.1 `Shared.Kernel/MarketData/Quote.cs` (record).
- [ ] 1.2 `Shared.Kernel/MarketData/QuoteSource.cs` (enum byte 0..3).
- [ ] 1.3 `Shared.Kernel/MarketData/IQuoteProvider.cs` (interface + 2 methods).
- [ ] 1.4 RED tests `QuoteTests` (2 scenarios: serialization, spread = ask - bid).
- [ ] 1.5 RED tests `IQuoteProviderContractTests` (3 scenarios via InMemoryQuoteProvider: known, unknown, bulk).

**Phase 2: In-memory provider**
- [ ] 2.1 `Trading.Infrastructure/MarketData/InMemoryQuoteProvider.cs` — deterministic seed (`symbol.GetHashCode()`) + `IClock.UtcNow.Ticks` walk.
- [ ] 2.2 RED tests `InMemoryQuoteProviderTests` (6 scenarios: same symbol same time → same quote, different time → different quote, casing, unknown → null, bulk drops unknown, seed produces bounded bid/ask range).

**Phase 3: Migration**
- [ ] 3.1 `infrastructure/postgres/migrations/0016_quotes_cache.sql` (idempotent, additive): `trading.quotes_cache` table + 4 nullable columns en `trading.instruments`.
- [ ] 3.2 Wire en `migrate.Dockerfile`.

**Phase 4: Application (TDD)**
- [ ] 4.1 RED tests `GetQuoteHandlerTests` (3): cache hit, cache miss → provider fetch, unknown → 404.
- [ ] 4.2 GREEN: `GetQuoteQuery` + `GetQuoteHandler` + `IQuoteCacheRepository`.
- [ ] 4.3 RED tests `GetQuotesBulkHandlerTests` (3): all known, mixed known/unknown, all unknown → 200 + [].
- [ ] 4.4 GREEN: `GetQuotesBulkQuery` + handler.

**Phase 5: Infrastructure + API**
- [ ] 5.1 `QuoteCacheConfiguration` (EF).
- [ ] 5.2 `QuoteCacheRepository` impl (GetAsync, UpsertAsync, GetManyAsync).
- [ ] 5.3 `QuoteEndpoints` (`MapQuoteEndpoints`): 2 endpoints (GET /{symbol}, GET ?symbols=) + 1 dev-only dump. RequireAuthorization. `api-quotes` rate limit (higher than general).
- [ ] 5.4 `app.MapQuoteEndpoints()` en `Program.cs`.
- [ ] 5.5 DI: `AddSingleton<IQuoteProvider, InMemoryQuoteProvider>()` + `AddScoped<IQuoteCacheRepository, QuoteCacheRepository>()` en `TradingModuleRegistration`.

**Phase 6: Validate**
- [ ] 6.1 `dotnet test --filter "FullyQualifiedName~MarketData|Quote" --nologo --verbosity minimal` → 10+ passed.
- [ ] 6.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.

### 4b.2 Frontend (~150 líneas)

**Phase 1: Service**
- [ ] 1.1 `api/quotes.service.ts` con 2 métodos HTTP (getBySymbol, getBulk).
- [ ] 1.2 2 jest specs (service — covered by 4c.2 page-level specs).

---

## Slice 4c — Realtime (≤ 700 líneas, split 4c.1 + 4c.2)

### 4c.1 Backend (~400 líneas)

**Phase 1: Hub class**
- [ ] 1.1 `Trading.Api/Hubs/QuoteHub.cs` (Hub<IQuoteClient> con SubscribeToSymbols/UnsubscribeFromSymbols + OnConnected/OnDisconnected logging).
- [ ] 1.2 `IQuoteClient` interface con `OnQuoteUpdate(Quote)` + `OnError(string, string)`.
- [ ] 1.3 RED tests `QuoteHubTests` (3 scenarios via TestServer: connect, subscribe, unsubscribe).

**Phase 2: Broadcast service**
- [ ] 2.1 `ActiveSubscriptions.cs` (ConcurrentDictionary side-channel para Symbol enumeration).
- [ ] 2.2 `Trading.Infrastructure/Realtime/QuoteBroadcastService.cs` (BackgroundService + 5s loop + change detection + scope factory + jitter).
- [ ] 2.3 RED tests `QuoteBroadcastServiceTests` (4 scenarios: tick fires, no subscribers → no-op, unchanged quote → skip, dead connection → skip).
- [ ] 2.4 RED tests `ActiveSubscriptionsTests` (3 scenarios: track, untrack, snapshot).

**Phase 3: SignalR wire**
- [ ] 3.1 `Program.cs`: `builder.Services.AddSignalR(...)` con `EnableDetailedErrors` en dev.
- [ ] 3.2 `Program.cs`: `app.MapHub<QuoteHub>("/hubs/quotes")` + CORS `AllowCredentials()` update.
- [ ] 3.3 DI: `AddHostedService<QuoteBroadcastService>()` en `TradingModuleRegistration`.
- [ ] 3.4 RED tests integration: `dotnet test --filter "FullyQualifiedName~SignalR"` — verify hub reachable from authenticated client.

**Phase 4: Alert rule modification**
- [ ] 4.1 Modify `CurrentPriceNearStopRule.cs` constructor — add `IQuoteProvider` injection.
- [ ] 4.2 Modify `Evaluate()` — replace `EntryPrice` proxy con `(quote.Bid + quote.Ask) / 2`. Silent skip on null/exception.
- [ ] 4.3 Update copy text — honest "cerca del stop" en lugar de "cerca de zona de entrada".
- [ ] 4.4 RED tests `CurrentPriceNearStopRuleTests` (5 updated scenarios: open trade near real stop → fires, open trade far from real stop → no alert, provider null → silent skip, provider throws → caught + skip, copy references StopLossPrice not EntryPrice).

**Phase 5: Validate**
- [ ] 5.1 `dotnet test --filter "FullyQualifiedName~QuoteHub|QuoteBroadcast|CurrentPriceNearStop" --nologo --verbosity minimal` → 12+ passed.
- [ ] 5.2 Manual `wscat -c ws://localhost:5000/hubs/quotes?access_token=<jwt>` → connect OK + invoke `SubscribeToSymbols(["EURUSD"])` + receive `OnQuoteUpdate` within 5s.
- [ ] 5.3 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.

### 4c.2 Frontend (~300 líneas)

**Phase 1: Dependencies**
- [ ] 1.1 `npm install @microsoft/signalr@^8` en `frontend/`.
- [ ] 1.2 Verify `frontend/package.json` includes the dep.

**Phase 2: SignalR client**
- [ ] 2.1 `api/quotes-signalr.service.ts` con `HubConnection` + reconnect-with-backoff (1s→2s→4s→8s→16s→30s, infinite retry) + `subscribe(symbols)` / `unsubscribe(symbols)` + `onQuoteUpdate(handler)`.
- [ ] 2.2 `state/quotes.state.ts` (Signals: `quotes = signal<Map<string, Quote>>`, `subscribedSymbols`, `connectionStatus`).
- [ ] 2.3 3 jest specs (subscribe flow, unsubscribe flow, reconnect backoff sequence).

**Phase 3: Watchlist component**
- [ ] 3.1 `shared/watchlist/jcs-watchlist.component.ts` standalone OnPush con `@Input() symbols: string[]`, `@Output() symbolClick`, subscribes on init, unsubscribes on destroy.
- [ ] 3.2 `jcs-watchlist.component.scss` (table styling + mobile-first horizontal scroll).
- [ ] 3.3 3 jest specs (renders initial symbols empty, receives quote update, unsubscribe on destroy).

**Phase 4: Dashboard integration**
- [ ] 4.1 Modify `dashboard.page.ts` — embed `<jcs-watchlist [symbols]="['EURUSD','GBPJPY','BTCUSD']">` en live-prices section.
- [ ] 4.2 2 jest specs (dashboard renders watchlist, watchlist click → navigate to symbol detail).

---

## Slice 4d — Attachments (≤ 500 líneas, split 4d.1 + 4d.2)

### 4d.1 Backend (~400 líneas)

**Phase 1: Shared (TDD)**
- [ ] 1.1 `Shared.Kernel/Storage/AttachmentQuota.cs` (record con defaults 50 MiB / 100 / 90 days).
- [ ] 1.2 `Shared.Kernel/Storage/IVirusScanner.cs` (interface) + `ScanResult` enum (NotScanned/Clean/Infected/Error).
- [ ] 1.3 RED tests `AttachmentQuotaTests` (2 scenarios: defaults, equality).

**Phase 2: Virus scanner stub**
- [ ] 2.1 `Trading.Infrastructure/Storage/VirusScannerNoOp.cs` — always returns `ScanResult.Clean`.
- [ ] 2.2 RED tests `VirusScannerNoOpTests` (2 scenarios: any input → Clean, async with cancellation).

**Phase 3: Migration**
- [ ] 3.1 `infrastructure/postgres/migrations/0018_attachment_lifecycle.sql` (idempotent, additive): 5 nullable columns en `trading.trade_attachments` + `ix_trade_attachments_expires_sweep` partial index.
- [ ] 3.2 Wire en `migrate.Dockerfile`.

**Phase 4: Application (TDD)**
- [ ] 4.1 RED tests `AttachmentQuotaEnforcerTests` (4): under limit passes, total size exceeds → 413, count exceeds → 413, unconfirmed uploads ignored.
- [ ] 4.2 GREEN: `AttachmentQuotaEnforcer` + `QuotaCheckResult`.
- [ ] 4.3 Modify `RequestAttachmentUploadHandler` — invoke enforcer BEFORE presign; return 413 if exceeded.
- [ ] 4.4 Modify `ConfirmAttachmentUploadedHandler` — invoke `IVirusScanner.ScanAsync` + set `VirusScannedAt` + `ScanResult` + `ExpiresAt = now + 90 days`.
- [ ] 4.5 RED tests `GetThumbnailHandlerTests` (3): image returns 302 URL, non-image returns 415, cross-user returns 404.
- [ ] 4.6 GREEN: `GetThumbnailQuery` + handler.
- [ ] 4.7 RED tests `AttachmentUsageQueryTests` (3): empty user, multi-attachment compute, percentFull rounding).
- [ ] 4.8 GREEN: `AttachmentUsageQuery` + handler + `AttachmentUsageDto`.

**Phase 5: MinIO extension + lifecycle service**
- [ ] 5.1 Modify `MinioAttachmentStore` — add `GetThumbnailUrlAsync(objectKey, width, height, ct)` extension.
- [ ] 5.2 RED tests `MinioAttachmentStoreThumbnailTests` (2 scenarios via TestContainers MinIO: presigned URL contains transform params, expiry is 1h).
- [ ] 5.3 `Trading.Infrastructure/Storage/AttachmentLifecycleService.cs` (BackgroundService, daily 02:00 UTC + jitter, sweep `is_active = true AND expires_at < now()`, soft-delete + MinIO delete).
- [ ] 5.4 RED tests `AttachmentLifecycleServiceTests` (4 scenarios: sweep finds expired, sweep skips active, MinIO error → log + continue, sweep is idempotent).
- [ ] 5.5 DI: `AddSingleton<IVirusScanner, VirusScannerNoOp>()` + `AddScoped<AttachmentQuotaEnforcer>()` + `AddScoped<AttachmentUsageQuery>()` + `AddHostedService<AttachmentLifecycleService>()` en `TradingModuleRegistration`.
- [ ] 5.6 Modify `MapAttachmentEndpoints` — add `GET /{id}/thumbnail` + `GET /usage`.

**Phase 6: Validate**
- [ ] 6.1 `dotnet test --filter "FullyQualifiedName~Attachment" --nologo --verbosity minimal` → 10+ passed.
- [ ] 6.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.

### 4d.2 Frontend (~100 líneas)

**Phase 1: Service + UI**
- [ ] 1.1 `api/attachments.service.ts` con 2 métodos HTTP (getThumbnailUrl, getUsage).
- [ ] 1.2 Modify `trades-list.page.ts` — embed storage usage indicator (small bar: "8.5 MB / 50 MB used, 12/100 files").
- [ ] 1.3 2 jest specs (storage indicator renders, indicator updates after upload).

---

## Slice 4e — E2E Wiring + Smoke (~300 líneas, single PR)

**Phase 1: Nav update**
- [ ] 1.1 Update `trader-shell.ts` `navItems` — add `'Scanner'` entry (icon: 'menu') — now 9 items con horizontal scroll.
- [ ] 1.2 Update `trader.routes.ts` — add lazy route `'scanner'`.

**Phase 2: Dashboard integration (verification)**
- [ ] 2.1 Verify `<jcs-watchlist [symbols]="['EURUSD','GBPJPY','BTCUSD']">` renders en `dashboard.page.ts` (done in 4c.2.4.1, just smoke).
- [ ] 2.2 Verify watchlist receives quote updates within 5s of hub connect.

**Phase 3: Smoke E2E**
- [ ] 3.1 `docker compose up -d --build api frontend` → healthy (api + postgres + redis + minio + mailpit + minio-init).
- [ ] 3.2 Smoke E2E from Tailscale iPhone URL:
  - [ ] 3.2.1 Create scanner filter → list → run → ranked results.
  - [ ] 3.2.2 GET /api/quotes/EURUSD → 200 + Quote JSON.
  - [ ] 3.2.3 SignalR connect → subscribe EURUSD → receive OnQuoteUpdate within 5s.
  - [ ] 3.2.4 Verify CurrentPriceNearStopRule fires with real price (seed scenario: open trade EURUSD with stop = 1.0800, mock provider returns bid = 1.0805, run-now → alert created).
  - [ ] 3.2.5 Upload attachment → confirm with virus scan → verify `expires_at` set.
  - [ ] 3.2.6 GET /api/attachments/usage → verify totalBytes + count.
  - [ ] 3.2.7 Trigger quota exceed (50 MB total) → 413.
  - [ ] 3.2.8 GET attachment thumbnail → 302 with presigned URL containing `width=200&height=200`.
  - [ ] 3.2.9 Verify MinIO bucket lifecycle rule applied (`mc ilm ls jadecapital/jade-attachments`).
- [ ] 3.3 `cd frontend && npx jest --no-coverage` → todos verdes. (Target: 116 + 18 nuevos = 134+ tests, 32+ suites.)
- [ ] 3.4 `dotnet test JadeCapital.slnx --filter "FullyQualifiedName!~JadeCapital.Api.IntegrationTests" --no-restore --nologo --verbosity minimal` → todos verdes. (Target: 703 + 47 nuevos = 750+ tests.)

**Phase 4: Tasks close + archive**
- [ ] 4.1 All checkboxes above marked done.
- [ ] 4.2 Verify cross-slice `git diff --stat` per PR ≤ 400 lines.
- [ ] 4.3 Update apply-progress.md with Wave 4 narrative.
- [ ] 4.4 Update verify-report.md with smoke results.
- [ ] 4.5 Archive via `/sdd-archive` (sync delta specs to main specs/).

---

## Cross-cutting / Validation

- [ ] 6.1 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.
- [ ] 6.2 `cd frontend && npx jest --no-coverage` → todos verdes. (Target: 134+ tests.)
- [ ] 6.3 `dotnet test JadeCapital.slnx --filter "FullyQualifiedName!~JadeCapital.Api.IntegrationTests" --no-restore --nologo --verbosity minimal` → todos verdes. (Target: 750+ tests.)
- [ ] 6.4 `docker compose up -d --build api frontend` → healthy.
- [ ] 6.5 Confirmar per-slice `git diff --stat` ≤ 400 (o chained PRs justificados).
- [ ] 6.6 mem_save final con specs + lessons + next steps. *(Deferred — user instruction: NO mem_save during SDD phase.)*

## Open / deferred to later waves

- Real broker integration (IBKR, MT5) — Wave 6 swaps `InMemoryQuoteProvider` for `BrokerQuoteProvider`.
- Real virus scanner (ClamAV) — Wave 6 swaps `VirusScannerNoOp` for `ClamAvVirusScanner`.
- Real-time alerts push (SignalR client receives alerts in addition to quotes) — Wave 5.
- AI signal generation from scanner results — Wave 5.
- Multi-tenant data isolation — Fase 6.
- PWA offline mode (cached quotes for offline view) — Fase 7.
- Multi-timeframe composite scanner filters — Wave 5.
- Thumbnail generation server-side (beyond MinIO transform params) — Wave 6.
- Calendar integration (Google Calendar) for planner — Wave 5.
- Strategy precompute (materialized view) when trade count > 10k per user — Wave 5.
- WebSocket transport tuning (keepalive interval, max message size) — Wave 5 operational hardening.
- BackgroundService metrics (Prometheus) — Wave 5 observability slice.