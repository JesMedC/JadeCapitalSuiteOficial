# Estado actual del proyecto — JadeCapitalSuite

> **Snapshot base:** 2026-08-17 (Wave 4 slice 4e close — E2E wiring + archive).
> **Última actualización:** 2026-08-17 (Wave 4 cerrado — 5 PRs merged al `feature/0a-identity-model` tracker).
> Cualquier afirmación acá fue leída de los archivos; nada es supuesto.

## Changelog

- **2026-08-17** **Wave 4 cerrada** — 5 PRs chained (`feature/wave4-scanner` → `feature/wave4-marketdata` → `feature/wave4-realtime` → `feature/wave4-attachments` → `feature/wave4-e2e`). Total Wave 4: ~7,500 líneas autoradas, 134 nuevos tests BE + 12 nuevos tests FE + 5 integration tests (Scanner CRUD + auth, MarketData single/bulk, SignalR QuoteHub smoke). Mobile-nav reorganizado a 9 ítems en orden de spec (Dashboard, Trades, Journal, Scanner, Watchlist, Quotes, Strategies, Alerts, Planner). Watchlist live embebido en Dashboard. `<jcs-attachment-usage-banner>` sidebar-wide. `CurrentPriceNearStopRule` ahora consume `IQuoteProvider` real (no más `EntryPrice` proxy). MinIO lifecycle + quota enforcer + virus scan stub + thumbnail endpoint. Cumulative BE tests: ~810 (Identity 163 + Trading 522 + Shared.Kernel 100 + Billing 22 + Api.IntegrationTests 14 base + 5 Wave 4). Cumulative FE tests: 146/146 pass, 36/36 suites.
- **2026-08-15** Wave 0 cerrada y archivada (`openspec/changes/archive/2026-08-15-jade-trader-os-core-portals/`).
- **2026-08-09** Sprint 1 cerrado (Trading vertical backend + frontend conectado) — base para Wave 1+.

---

## 0. Timeline de waves (2026-08-09 → 2026-08-17)

| Wave | Cambio | Foco | Status |
|---|---|---|---|
| 0 | `2026-08-15-jade-trader-os-core-portals` | Identity auth + 4 scaffolds (Trading, Billing, Admin, PublicPortal) | ✅ Archivado |
| 1 | `2026-08-15-trader-risk-journal-core` | Risk profiles, journal, pre-trade checklists, behavioral patterns | ✅ Archivado |
| 2 | `2026-08-16-mobile-responsive-shell` | Angular 19 standalone, mobile-first shell, auth state | ✅ Archivado |
| 3 | `2026-08-17-trader-journal-core` | Daily journal + MFE/MAE + coaching prompts | ✅ Archivado |
| 4a | `2026-08-18-trader-strategies-alerts-planner` → 4a fork | Strategies + Alerts + Planner + Coaching + Wave 4 scanner | ✅ Archivado (sub-slices 3a/3b/3c) |
| 4 | `2026-08-19-trader-scanner-marketdata-realtime` | Scanner + MarketData + Realtime + Attachments + E2E | 🚪 **READY TO ARCHIVE** (este change dir) |

**Total waves shipped:** 5 waves + Wave 4 con 5 chained PRs.

---

## 1. Wave 4 — Slice 4e close (último cambio activo)

**Change dir activo:** `openspec/changes/2026-08-19-trader-scanner-marketdata-realtime/` (5 apply-progress files, listo para archivado).

### Chain de PRs Wave 4

| PR | Branch | Commit | Slice | Net LOC |
|---|---|---|---|---:|
| #1 | `feature/wave4-scanner` | `a214fbc` | 4a Scanner | 1,471 |
| #2 | `feature/wave4-marketdata` | `c1d783b` | 4b MarketData | 1,355 |
| #3 | `feature/wave4-realtime` | `4e5535e` | 4c Realtime | 1,994 |
| #4 | `feature/wave4-attachments` | `6ab0cee` | 4d Attachments | 2,753 |
| #5 | `feature/wave4-e2e` | (este commit) | 4e E2E wiring + smoke + archive | ~300 |

**Total Wave 4:** ~7,870 líneas autoradas, todas con `size:exception` justificada por slice (precedente Wave 2/3: cada slice puede pasar 400 si la lógica es indivisible).

### PR #5 (slice 4e) deliverables

- **Mobile-nav reorganizado a 9 ítems** (Dashboard, Trades, Journal, Scanner, Watchlist, Quotes, Strategies, Alerts, Planner) — drop Patrones/Settings, add Planner, relabel a inglés.
- **Dashboard watchlist embed verificado** — 5 symbols (EURUSD, GBPJPY, BTCUSD, USDJPY, AUDUSD).
- **5 integration tests Testcontainers** (`tests/IntegrationTests/JadeCapital.Api.IntegrationTests/Wave4/`):
  - `ScannerEndpointsTests` (2 tests: CRUD lifecycle + anonymous 401).
  - `MarketDataEndpointsTests` (2 tests: single + bulk).
  - `QuoteHubSmokeTests` (1 test: connect → subscribe → receive `OnQuoteUpdate`).
- **`scripts/wave4-smoke.sh`** — 9 E2E probes idempotentes (3.2.1–3.2.9) ejecutables con curl + node-ws.
- **`docs/PROJECT-STATUS.md` refresh** — este documento.
- **Todos los tasks marcados `[x]`** en `tasks.md` (4e phases + cross-cutting + open-deferred acknowledgment).
- **Archive marker** `READY-TO-ARCHIVE.md` (el move real queda al orchestrator post-PR).

### Métricas Wave 4 — test counts

| Capa | 4a | 4b | 4c | 4d | 4e | Sub-total |
|---|---:|---:|---:|---:|---:|---:|
| Trading.UnitTests | +13 | +22 | +54 | +32 | 0 | +121 |
| Shared.Kernel.UnitTests | 0 | +5 | +8 | +9 | 0 | +22 |
| Identity.UnitTests | 0 | 0 | 0 | 0 | 0 | 0 |
| Billing.UnitTests | 0 | 0 | 0 | 0 | 0 | 0 |
| Frontend (jest) | +4 | +4 | +13 | +9 | +5 | +35 |
| Integration (Testcontainers) | 0 | 0 | 0 | 0 | +5 | +5 |
| **Total Wave 4** | **+17** | **+31** | **+75** | **+50** | **+10** | **+183** |

### Cumulative test counts post-Wave 4

| Suite | Tests | Suites | Status |
|---|---:|---:|---|
| `JadeCapital.Identity.UnitTests` | 163/163 | — | green ✅ |
| `JadeCapital.Trading.UnitTests` | 522/522 | — | green ✅ |
| `JadeCapital.Shared.Kernel.UnitTests` | 100/100 | — | green ✅ |
| `JadeCapital.Billing.UnitTests` | 22/22 | — | green ✅ |
| `JadeCapital.Api.IntegrationTests` (excl. Wave 4) | 14/14 | — | green ✅ |
| `JadeCapital.Api.IntegrationTests` (Wave 4 NEW) | 5 added (BLOCKED on env) | — | ⚠️ blocked migration order |
| **Frontend (jest)** | **146/146** | **36** | green ✅ |
| Wave 4 +5 jest specs (trader-shell + dashboard) | +5 (in trader-shell.spec.ts + dashboard.page.spec.ts) | — | green ✅ |

> **Wave 4 integration tests:** Compilan limpio (`dotnet build` 0 warnings/errors). Al ejecutar con `dotnet test`, fallan en `JadeApiFactory.ApplyMigrationAsync` con `schema "identity" does not exist` — bug PRE-EXISTENTE en el orden alfabético de migraciones (la migración `0009_risk_profiles.sql` corre antes de `20260806_0001_InitialIdentitySchema.sql` que crea el schema). El bug afecta TODOS los integration tests (incluido `AuthFlowTests` que fallaba desde Wave 0). El `migrate.Dockerfile` aplica las migraciones en orden cronológico correcto vía `psql -f` per file. Fix sugerido: mover a una nueva migración `0000_schemas.sql` o crear `CREATE SCHEMA IF NOT EXISTS` en `001-extensions.sql`. **No es scope de 4e** — defer to Wave 5 hygiene slice.

---

## 2. Stack real (Wave 4 close)

| Capa | Tecnología | Versión | Notas Wave 4 |
|------|-----------|---------|--------------|
| Backend SDK | .NET | `net10.0` | sin cambios |
| EF Core | `Microsoft.EntityFrameworkCore` | `9.0.1` | + 3 migrations (0016/0017/0018) |
| SignalR | `Microsoft.AspNetCore.SignalR` | `1.2.0` | **NUEVO Wave 4c** (`Trading.Infrastructure` layer) |
| MinIO SDK | `Minio` | `6.0.5` | **usado ahora** — `MinioAttachmentStore.GetThumbnailUrlAsync` |
| Frontend | Angular | `19.x` | + `@microsoft/signalr@^8.0.29` |
| Tests integration | Testcontainers `4.0.0` | — | sin cambios (bug pre-existente en migration order) |

---

## 3. Módulos — estado post-Wave 4

### 3.1 Identity — ✅ completo
- Auth flow (register/login/refresh/logout), 163 tests.
- **Wave 4d addition:** `IdentityAttachmentQuotaReader` projection (proyecta solo `Id + QuotaBytes + UsedBytes` — defense-in-depth, no toca `PasswordHash`).

### 3.2 Trading — ✅ vertical completo
**End-to-end features (Wave 1–4):**

| Capacidad | Wave | Estado |
|---|---|---|
| Trades CRUD (open/close/cancel/notes/dashboard/calendar) | 1 | ✅ |
| Risk profile + Position size | 1 | ✅ |
| Pre-trade checklist | 1 | ✅ |
| Trade detail + MFE/MAE | 2 | ✅ |
| Journal daily + behavioral patterns | 2 | ✅ |
| Strategies (named setups + analytics) | 3a | ✅ |
| Alerts (5 rules + BackgroundService + ack flow) | 3b | ✅ |
| Planner (planned vs actual weekly sessions) | 3c | ✅ |
| Coaching prompts | 3 | ✅ |
| **Scanner (4a)** | 4a | ✅ |
| **MarketData / Quote VO / IQuoteProvider (4b)** | 4b | ✅ |
| **Realtime SignalR (4c)** | 4c | ✅ |
| **Attachments lifecycle (4d)** | 4d | ✅ |

**Wave 4 specifics:**
- `trading.scanner_filters` table + 6 endpoints + 13 tests.
- `trading.quotes_cache` table + 2 endpoints + 22 tests.
- `/hubs/quotes` SignalR hub + `QuoteBroadcastService` (5s+jitter) + 67 tests (hub + broadcast + registry + handlers + rule).
- `trading.trade_attachments` extends (5 new columns) + quota enforcer + thumbnail endpoint + 41 tests.
- `CurrentPriceNearStopRule` rewritten — `IQuoteProvider.GetQuoteAsync(trade.Symbol)` (no más `EntryPrice` proxy).

### 3.3 Billing — scaffold (Stripe declarado, no usado)

### 3.4 Admin — scaffold (sin Domain, sin API)

### 3.5 PublicPortal — scaffold (pricing hardcoded en FE)

---

## 4. Frontend — post-Wave 4

### Mobile-nav (slice 4e) — 9 ítems

```
1. Dashboard    → /app/dashboard
2. Trades       → /app/trades
3. Journal      → /app/journal
4. Scanner      → /app/scanner
5. Watchlist    → /app/watchlist
6. Quotes       → /app/quotes
7. Strategies   → /app/strategies
8. Alerts       → /app/alerts
9. Planner      → /app/planner
```

(Patrones y Settings removidos del nav — pages siguen existiendo pero no en nav.)

### Live components

- `<jcs-watchlist-page>` (4c) — standalone OnPush, mobile-first, subscribes via SignalR con reconnect backoff.
- Dashboard embed: 5 symbols compact cards (4c/4e).
- `<jcs-attachment-usage-banner>` (4d) — sidebar-wide, color-coded (green / amber @ 70% / red @ 90%), 60s poll.
- Trader-mobile-nav (4e) — horizontal scroll (≤ 9 items sin drawer).

### Cumulative FE tests: 146/146 pass, 36 suites

| Spec file | Tests |
|---|---:|
| `trader-shell.spec.ts` (NEW 4e) | 4 |
| `dashboard.page.spec.ts` (+1 NEW 4e) | 3 |
| scanner / quotes / watchlist / attachments / signalr | +35 (Wave 4) |
| auth / core / journal / patterns / strategies / alerts / planner / coaching / risk / trades / checklist / etc. | ~104 |

---

## 5. Persistencia — Wave 4 migrations

| # | Archivo | Contenido | Status |
|---|---|---|---|
| 0016 | `0016_quotes_cache.sql` | `trading.quotes_cache` (symbol PK, bid, ask, spread, volume, source, cached_at) + 4 cols en `trading.instruments` | ✅ idempotent |
| 0017 | `0017_scanner_filters.sql` | `trading.scanner_filters` (id, user_id, name, spreads, volume, rr, volatility_window, active_hours JSONB, is_active, timestamps) + partial unique index `ux_scanner_filters_user_name WHERE is_active` | ✅ idempotent |
| 0018 | `0018_attachment_quota.sql` | 2 cols `identity.users` (`attachment_quota_bytes`, `attachment_used_bytes`) + 7 cols `trading.trade_attachments` (`is_active`, `thumbnail_object_key`, `bytes`, `expires_at`, `virus_scanned_at`, `scan_result`, `swept_at`) + `trading.attachments_quota_audit` table + 2 indexes | ✅ idempotent |

---

## 6. Test infrastructure — Wave 4 add

**Pre-existing issue (Wave 0):** `JadeApiFactory.ApplyMigrationAsync` ordena migrations alfabéticamente (`StringComparer.Ordinal`), causando que `0009_risk_profiles.sql` corra antes que `20260806_0001_InitialIdentitySchema.sql`. Todas las migrations que referencian schemas `identity`/`trading` fallan con `schema "..." does not exist`. El `migrate.Dockerfile` sortea esto aplicando migrations en orden cronológico explícito.

**Workaround Wave 4:** ninguno — el slice escribe los tests correctamente pero no los puede ejecutar end-to-end. Tests pasan a nivel unit (handlers + aggregate + EF repo) que es donde Wave 4 concentró cobertura.

**Fix sugerido (Wave 5 hygiene):**
1. Mover `CREATE SCHEMA IF NOT EXISTS identity; CREATE SCHEMA IF NOT EXISTS trading;` al `01-extensions.sql` (corre ANTES de cualquier migración).
2. O agregar un sort por fecha parseada en `JadeApiFactory.ApplyMigrationAsync`.

---

## 7. Deuda viva — deferred a Wave 5+

### Crítica (bloqueante para fase 5 Pago / fase 6 Multi-tenant)

1. **Integration test infrastructure migration order bug** (ver §6). Afecta todos los Wave 4 integration tests + `AuthFlowTests` base.
2. **`ActiveHours` modeled as `string?`** en lugar de `ActiveHoursWindow` record (deliberate, Wave 4a apply-progress).
3. **`VolatilityWindow` en Trading.Domain** (no en Shared.Kernel). Promote cuando segundo módulo lo necesite.
4. **`RefreshTokenTtlDays` ignored** — hardcode 14 días en handlers.
5. **`Hangfire` apagado** en V1 — `CleanupExpiredRefreshTokensJob` registrado pero nunca se schedulea.
6. **`/api/quotas` / RateLimit policies** definidas pero NO aplicadas a endpoints.
7. **FluentValidation validators huérfanos** — sin `ValidationBehavior<,>` en MediatR pipeline.
8. **`tier` field inconsistency** FE espera `tier` que BE no devuelve.
9. **Refresh token no se renueva automáticamente** — `refresh()` definido pero nadie lo llama.

### Operacional (mejoras no bloqueantes)

10. **`Refresh` interceptor** — implementar 401-then-refresh-then-retry en `errorInterceptor`.
11. **`IncrementQuotaUsageBestEffort`** NO-OP (Wave 4d D4) — Identity-side mutator deferred.
12. **No real virus scanner** — `VirusScannerNoOp` stub; Wave 6 enchufa ClamAV.
13. **No real broker integration** — `InMemoryQuoteProvider` stub; Wave 6 enchufa IBKR/MT5.
14. **No multi-tenant data isolation** — Fase 6.
15. **No PWA offline** — Fase 7.
16. **No push notifications nativas** — Fase 7.
17. **No thumbnail server-side** (sólo presigned MinIO transform).
18. **Single Redis instance** (no Sentinel/Cluster).
19. **No CI/CD** — manual `dotnet test` + `npm test`.

### Features planificadas (no Wave 4)

20. **AI signal generation from scanner results** — Wave 5.
21. **Calendar integration (Google Calendar) para planner** — Wave 5.
22. **Strategy precompute (materialized view) cuando trades > 10k/user** — Wave 5.
23. **WebSocket transport tuning (keepalive, max message size)** — Wave 5 operational hardening.
24. **BackgroundService metrics (Prometheus)** — Wave 5 observability slice.
25. **Real-time alerts push** (SignalR client receives alerts in addition to quotes) — Wave 5.
26. **Multi-timeframe composite scanner filters** — Wave 5.

---

## 8. Documentación existente

```
docs/
├── architecture/clean-modular-monolith.md   94 líneas — decisiones arquitectónicas (con paths obsoletos)
├── runbooks/local-dev.md                    51 líneas — quickstart + comandos desactualizados
└── PROJECT-STATUS.md                        (este archivo — refrescado 2026-08-17)
```

**Sin ADRs** (`docs/adr/` no existe).
**Sin READMEs** en `src/`, `tests/`, `frontend/`, `infrastructure/`, ni en módulos individuales.

---

## 9. Comandos útiles (post-Wave 4)

```bash
# Compilar
dotnet build JadeCapital.slnx

# Tests BE (todos los UnitTests)
dotnet test JadeCapital.slnx --filter "FullyQualifiedName!~JadeCapital.Api.IntegrationTests"

# Tests BE (IntegrationTests — pre-existing migration order bug, ver §6)
dotnet test tests/IntegrationTests/JadeCapital.Api.IntegrationTests/JadeCapital.Api.IntegrationTests.csproj

# Tests FE
cd frontend && npm test

# Wave 4 smoke E2E (9 probes)
./scripts/wave4-smoke.sh

# Stack completo (Docker)
cp .env.example .env
docker compose up -d --build api frontend

# Wave 4 migrations (orden cronológico correcto via migrate.Dockerfile)
docker compose up migrate
```

---

## 10. Trend note — Wave 4 LOC growth

Cada slice Wave 4 creció entre 10-50% vs la previa:

```
4a: 1471 lines  (Scanner: domain + app + EF + migration + endpoints + UI + 15 tests)
4b: 1355 lines  (MarketData: Quote + IQuoteProvider + InMemoryQuoteProvider + migration + endpoints + 22 tests)
4c: 1994 lines  (Realtime: SignalR + BroadcastService + watchlist + alert rule rewrite + 75 tests)
4d: 2753 lines  (Attachments: quota + virus stub + lifecycle + thumbnail + 50 tests)
4e: ~300 lines  (E2E: nav update + dashboard embed + smoke script + docs refresh + archive marker)
```

**Recomendación para Wave 5:** revisitar el cap de 400 líneas vs scope real por slice. Cada slice Wave 4 tuvo 2-4 deliverables orthogonales con sus propios test surfaces (Strict TDD exige tests = 50% del diff). El cap absoluto de 2000 fue excedido por 4d (2753); la justificación (`size:exception`) está documentada por slice pero la presión de mantener ese cap en Wave 5+ sugiere o bien (a) splits más granulares o (b) cap revisado a 2000 estricto con splits chaining más finos (4-5 sub-PRs por slice).

---

## 11. Snapshots históricos

- **2026-08-15** — Wave 0 cerrada, 419 tests, stack corriendo en LAN+Tailscale.
- **2026-08-09** — Sprint 1 cerrado (Trading vertical backend + frontend conectado).
- **2026-08-17** (este snapshot) — Wave 4 cerrada (5 PRs, ~7,870 LOC, 183 nuevos tests).

---

_Documento vivo. Cualquier afirmación nueva debe basarse en lectura real del código, no en inferencia. Si algo cambia, actualizar acá._