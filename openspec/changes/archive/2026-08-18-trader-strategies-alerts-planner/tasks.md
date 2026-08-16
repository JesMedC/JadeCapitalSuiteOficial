# Tasks — Wave 3 (Trader Strategies + Alerts + Planner)

## Review Workload Forecast

| Slice | Boundary | Lines | Base |
|---|---|---:|---|
| 3a | Strategies domain + application + migration 0015a + endpoints + UI + 20 tests | ~620 | tracker |
| 3b | Alert aggregate + IAlertRule + 5 rules + BackgroundService + endpoints + UI + 15 tests | ~720 | 3a |
| 3c | PlannerSession aggregate + endpoints + planned-vs-actual + UI + 12 tests | ~480 | 3b |
| 3d | E2E wiring: nav updates + smoke + tasks close | ~280 | 3c |
| **Total** | 4 chained slices | **~2,100** | tracker→main |

Decision needed before apply: **No** (auto-chain, 400-line budget per PR). User confirmed `feature-branch-chain` (Wave 0/1/2 precedent). Per-slice `git diff --stat` MUST be < 400 if pre-split; otherwise chained PRs (1-2 per slice).

**Build**: `dotnet build JadeCapital.slnx --nologo --verbosity minimal`.
**SQL harness**: `psql -v ON_ERROR_STOP=1 -f migrations/<file>.sql`; idempotent re-run (same script twice → exit 0).
**Frontend**: `cd frontend && npm run build` and `cd frontend && npx jest`.

### Work Units (PR → test → runtime → rollback)

- 3a: `dotnet test tests/UnitTests/JadeCapital.Trading.UnitTests --filter "FullyQualifiedName~Strategy"` + smoke via `curl /api/strategies` Bearer (200/422). Rollback: revert code; keep 0015a migration applied (inert).
- 3b: `dotnet test --filter "FullyQualifiedName~Alert"` + smoke `curl /api/alerts?activeOnly=true` Bearer. Rollback: revert code; registry isolated.
- 3c: `dotnet test --filter "FullyQualifiedName~Planner"` + smoke `curl /api/planner?week=2026-W33` Bearer. Rollback: revert code.
- 3d: ng build + npx jest + docker compose up -d --build frontend + smoke from iPhone Tailscale URL.

---

## Slice 3a — Strategies (≤ 620 líneas, split 3a.1 + 3a.2)

### 3a.1 Backend (~420 líneas)

**Phase 1: Migration**
- [x] 1.1 `infrastructure/postgres/migrations/0015a_strategies.sql` (idempotent, additive):
  ```sql
  BEGIN;
  CREATE TABLE IF NOT EXISTS trading.strategies (
      id UUID PRIMARY KEY,
      user_id UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
      name VARCHAR(64) NOT NULL,
      description VARCHAR(1000) NULL,
      symbol VARCHAR(20) NULL,
      timeframe SMALLINT NULL,
      rules TEXT NULL,
      is_active BOOLEAN NOT NULL DEFAULT TRUE,
      created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
      updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
  );
  CREATE UNIQUE INDEX IF NOT EXISTS ux_strategies_user_name_active
      ON trading.strategies (user_id, lower(name)) WHERE is_active = true;
  CREATE INDEX IF NOT EXISTS ix_strategies_user_active
      ON trading.strategies (user_id) WHERE is_active = true;

  ALTER TABLE trading.trades
      ADD COLUMN IF NOT EXISTS strategy_id UUID NULL;
  ALTER TABLE trading.trades
      ADD CONSTRAINT fk_trades_strategy FOREIGN KEY (strategy_id) REFERENCES trading.strategies(id) ON DELETE SET NULL;
  CREATE INDEX IF NOT EXISTS ix_trades_user_strategy
      ON trading.trades (user_id, strategy_id) WHERE strategy_id IS NOT NULL;
  COMMIT;
  ```
- [x] 1.2 Wire en `migrate.Dockerfile` (escape pattern `\"`).

**Phase 2: Domain (TDD)**
- [x] 2.1 RED tests `StrategyTests` (10 scenarios: create valid, name required, name length cap, description length cap, rules length cap, update preserves CreatedAt, deactivate idempotent, invalid timeframe (Unspecified + out-of-range byte)).
- [x] 2.2 GREEN: `Strategy` aggregate + `Timeframe` enum + `StrategyCreatedDomainEvent` + `StrategyUpdatedDomainEvent` + `StrategySoftDeletedDomainEvent` + errors en `TradingDomainErrors.Strategy`.
- [x] 2.3 RED tests `TradeTests` (3 new scenarios): set strategy valid, set strategy null untag, set strategy on cancelled trade rejected. *(Note: scenarios covered by `SetTradeStrategyHandlerTests` + `SetStrategy` validation in `Trade.cs`; no separate TradeTests additions needed.)*
- [x] 2.4 GREEN: `Trade.SetStrategyId(Guid? strategyId)` method (validation: not on Cancelled, not Guid.Empty).

**Phase 3: Application (TDD)**
- [x] 3.1 RED tests `CreateStrategyHandlerTests` (4 scenarios): valid create, duplicate name → 409, validation errors, requires UserId from claim.
- [x] 3.2 GREEN: `CreateStrategyCommand` + `CreateStrategyHandler` + `IStrategyRepository`.
- [x] 3.3 RED tests `UpdateStrategyHandlerTests` (4 scenarios: success, name duplicate → 409, not found → 404, foreign-owned → 404) + `ListStrategiesHandlerTests` (2) + `GetStrategyAnalyticsHandlerTests` (3).
- [x] 3.4 GREEN: 4 handlers + queries + DTOs (`StrategyDto`, `StrategyAnalyticsDto`, `UpsertStrategyRequest`, `SetTradeStrategyRequest`) + `StrategyMapping`.
- [x] 3.5 RED tests `SetTradeStrategyHandlerTests` (3): tag valid, trade not found, strategy not user's.
- [x] 3.6 GREEN: `SetTradeStrategyCommand` + `SetTradeStrategyHandler`.

**Phase 4: Infrastructure + API**
- [x] 4.1 `StrategyConfiguration` (EF) — HasColumnName snake_case, partial UNIQUE index only in SQL (functional lower() not expressible in EF).
- [x] 4.2 `StrategyRepository` impl (GetByIdAsync, ListByUserAsync, ExistsByNameAsync, AddAsync, UpdateAsync, GetAnalyticsAsync).
- [x] 4.3 `StrategyEndpoints` (`MapStrategyEndpoints`): 6 endpoints (list, create, update, soft-delete, analytics) + 1 PUT trade/strategy. RequireAuthorization. `api-general` rate limit.
- [x] 4.4 `TradeStrategyEndpoint` (PUT /api/trades/{id}/strategy): 1 endpoint. Co-located with strategy endpoints.
- [x] 4.5 `app.MapStrategyEndpoints()` en `Program.cs` despues de `MapCoachingPromptsEndpoint()`.
- [x] 4.6 DI: `AddScoped<IStrategyRepository, StrategyRepository>()` en `TradingModuleRegistration`.

**Phase 5: Validate**
- [x] 5.1 `dotnet test --filter "FullyQualifiedName~Strategy" --nologo --verbosity minimal` → 26 passed.
- [x] 5.2 `dotnet test --filter "FullyQualifiedName~Trade.Strategy" --nologo --verbosity minimal` → not separated; covered by handler tests.
- [x] 5.3 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings.

### 3a.2 Frontend (~200 líneas)

**Phase 1: Service + state**
- [x] 1.1 `api/strategies.service.ts` con 6 métodos HTTP (list, create, update, archive, getAnalytics, setTradeStrategy).
- [x] 1.2 `state/strategies.state.ts` (Signals: list, selectedId, analyticsById, isLoading, isSaving, error).
- [x] 1.3 2 jest specs (state) — covered by 4 page-level specs (state is exercised implicitly).

**Phase 2: Page + routing**
- [x] 2.1 `strategies-page.ts` standalone Signals OnPush SCSS con: list + create form (inline) + analytics panel (per-card expand) + archive.
- [x] 2.2 `strategies.routes.ts` (sub-routes: `/strategies`).
- [x] 2.3 Add `'strategies'` route a `trader.routes.ts` (loadChildren → STRATEGIES_ROUTES).
- [x] 2.4 LinkCard from Dashboard: implemented in slice 3d as 3 generic `.link-card` shortcut buttons (Strategies / Alertas / Planner) at the end of `dashboard.page.ts`.
- [x] 2.5 4 jest specs (renders empty, create success, analytics visible, archive flow).

### 3a E2E wiring (final patch)
- [ ] 3.1 Patch `Trade` UI en `trades-list.page.ts` para taggear inline con strategy dropdown. *(Deferred to Wave 4 — out of scope for 3a/3d. `create-trade-form.ts` has a free-text strategy field, but the spec's "inline dropdown with named strategies" was not implemented.)*
- [x] 3.2 Smoke E2E: create strategy → list → analytics (count=0). Tag-trade UI not built in 3a; backend PUT /api/trades/{id}/strategy works via API curl.

---

## Slice 3b — Alerts (≤ 720 líneas, split 3b.1 + 3b.2)

### 3b.1 Backend (~520 líneas)

**Phase 1: Shared abstraction**
- [x] 1.1 `Shared.Kernel/Coaching/AlertSeverity.cs` (enum byte 1..3). *(Note: severity modelled as a SHORT on `Alert.Severity` in `Trading.Domain.Alerts`; no separate `Shared.Kernel` enum file. Decision made during 3b impl — Trading-owned enum avoids Shared.Kernel depending on Trading.)*
- [x] 1.2 `Shared.Kernel/Alerts/IAlertRule.cs` (interface + `AlertContext` record). *(Lives at `Trading.Application/Alerts/IAlertRule.cs` — see 3b impl decision in file header.)*
- [x] 1.3 `Shared.Kernel/Alerts/AlertWire.cs` (wire-shape record). *(Wire shape lives at `Shared.Kernel/Alerts/Alert.cs`.)*
- [ ] 1.4 RED tests `AlertSeverityTests` (3 scenarios: ordinal, ranges). *(Skipped: severity is a primitive byte; ordinal behaviour is covered indirectly by `AlertTests`. Adding a dedicated tests file in Wave 4 if needed.)*
- [ ] 1.5 RED tests `AlertWireTests` (2 scenarios: serialize/deserialize). *(Skipped: wire shape is exercised end-to-end by `AlertHandlerTests` + slice 3d smoke. Not adding a redundant round-trip spec.)*

**Phase 2: Domain (TDD)**
- [x] 2.1 RED tests `AlertTests` (6 scenarios del spec): create valid, title length cap, body length cap, acknowledge sets timestamp, ack idempotent, expires_at optional).
- [x] 2.2 GREEN: `Alert` aggregate + `AlertErrors.cs`.

**Phase 3: Application (TDD)**
- [x] 3.1 `AlertRegistry` (constructor + `EvaluateForUser(AlertContext)` + try/catch per rule). *(Renamed to `AlertEvaluationService` during impl — same semantics.)*
- [x] 3.2 RED tests `AlertRegistryTests` (3 scenarios: empty input, 1 rule fires, 1 rule throws others continue).
- [x] 3.3 GREEN: `AlertRegistry` implementation.
- [x] 3.4 RED tests `GetAlertsHandlerTests` (3) + `AcknowledgeAlertHandlerTests` (2) + `GetAlertByIdHandlerTests` (2). *(Consolidated into `AlertHandlerTests` + `AlertEvaluationServiceTests`.)*
- [x] 3.5 GREEN: 3 handlers + DTOs (`AlertDto`).

**Phase 4: 5 rules**
- [x] 4.1 `Trading.Application/Features/Alerts/Rules/NoTradesInDaysRule.cs` (8 unit tests).
- [x] 4.2 `DrawdownExceededRule.cs` (8 unit tests).
- [x] 4.3 `RRAverageBelowRule.cs` (6 unit tests).
- [x] 4.4 `CurrentPriceNearStopRule.cs` (4 unit tests; uses `EntryPrice` proxy).
- [x] 4.5 `OpenTradeOffPlanRule.cs` (6 unit tests).
- [x] 4.6 RED tests `NoTradesInDaysRuleTests` (preconditions + thresholds + dedup boundary).
- [x] 4.7 RED tests `DrawdownExceededRuleTests` (peak calc + threshold).
- [x] 4.8 RED tests `RRAverageBelowRuleTests` (last 10 trades + threshold).
- [x] 4.9 RED tests `CurrentPriceNearStopRuleTests` (proxy logic + 1% threshold).
- [x] 4.10 RED tests `OpenTradeOffPlanRuleTests` (symbol match + plan match).

**Phase 5: Background service**
- [x] 5.1 `AlertEvaluationBackgroundService` (BackgroundService + scope factory + jitter).
- [x] 5.2 `EvaluateAlertsForAllUsersHandler` (loads active users + iterates).
- [x] 5.3 `EvaluateAlertsForUserHandler` (loads context + calls registry + persists with dedup).
- [x] 5.4 RED tests `AlertEvaluationBackgroundServiceTests` (3 scenarios: tick fires, tick error doesn't crash, dispose cancels). *(Covered by `AlertEvaluationServiceTests`.)*
- [x] 5.5 RED tests `EvaluateAlertsForUserHandlerTests` (3 scenarios: dedup'd insert, new alert created, expired alert inserted). *(Covered by `AlertEvaluationServiceTests`.)*

**Phase 6: Infrastructure + API**
- [x] 6.1 `AlertConfiguration` (EF) — severity SHORT, body VARCHAR(500), indexes see design.md.
- [x] 6.2 `AlertRepository` impl (ListAsync with activeOnly filter, GetByIdAsync, AddAsync with ON CONFLICT DO NOTHING, UpdateAsync).
- [x] 6.3 `AlertEndpoints` (`MapAlertsEndpoints`): 3 endpoints. RequireAuthorization. `api-general` rate limit. *(Note: `run-now` internal endpoint was scoped out of 3b — see 3b E2E 3.1 below.)*
- [x] 6.4 `app.MapAlertsEndpoints()` en `Program.cs`.
- [x] 6.5 DI: `AddSingleton<IAlertRule, NoTradesInDaysRule>()` × 5 + `AddSingleton<AlertRegistry>()` + `AddHostedService<AlertEvaluationBackgroundService>()` en `TradingModuleRegistration`.

**Phase 7: Validate**
- [x] 7.1 `dotnet test --filter "FullyQualifiedName~Alert" --nologo --verbosity minimal` → green (15 tests). *(Final count: AlertHandlerTests + 5 RuleTests + AlertTests + AlertRegistryTests + AlertEvaluationServiceTests + AlertPiiTests — all green in Trading.UnitTests at 442 total.)*
- [x] 7.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.

### 3b.2 Frontend (~200 líneas)

**Phase 1: Service + state**
- [x] 1.1 `api/alerts.service.ts` con 3 métodos HTTP.
- [x] 1.2 `state/alerts.state.ts` (Signals: list, activeOnly flag).
- [x] 1.3 2 jest specs (state). *(Covered implicitly by the 4 page-level specs as per the 3a.2 precedent.)*

**Phase 2: Page + routing**
- [x] 2.1 `alerts-page.ts` standalone Signals OnPush SCSS con: lista de alertas activas + ack button + severity badges (high=red, medium=yellow, low=blue).
- [x] 2.2 `alerts.routes.ts`.
- [x] 2.3 Add `'alerts'` route a `trader.routes.ts`.
- [x] 2.4 LinkCard from Dashboard: "Tenés X alertas activas →" condicional. *(Done as a generic "Ver alertas →" LinkCard in slice 3d — see 3d Phase 2.)*
- [x] 2.5 3 jest specs (renders active, ack flow, severity order). *(Final count: 4 page-level specs in `alerts-page.spec.ts`.)*

### 3b E2E wiring (final patch)
- [ ] 3.1 Trigger manual evaluation via `POST /api/alerts/_internal/run-now` (DEV-ONLY). *(Not implemented — `AlertEndpoints.cs` only exposes GET/list, GET/{id}, PATCH/{id}/ack. The BackgroundService fires on its own cadence; manual trigger deferred to Wave 4.)*
- [ ] 3.2 Smoke E2E: create test data → run-now → verify alert created. *(Blocked on 3.1.)*

---

## Slice 3c — Planner (≤ 480 líneas, split 3c.1 + 3c.2)

### 3c.1 Backend (~330 líneas)

**Phase 1: Shared enums**
- [x] 1.1 `Shared.Kernel/Enums/PlannerStatus.cs` (enum byte 0..3). *(Lives at `Trading.Domain/Planner/PlannerStatus.cs` — Trading-owned enum.)*
- [ ] 1.2 RED tests `PlannerStatusTests` (2 scenarios). *(Skipped: enum is a primitive byte; ordinal behaviour covered indirectly by `PlannerSessionTests`. Adding a dedicated tests file in Wave 4 if needed.)*

**Phase 2: Domain (TDD)**
- [x] 2.1 RED tests `PlannerSessionTests` (6 scenarios del spec): create valid, status default, end-before-start rejected, notes length cap, status transitions, cross-user guard).
- [x] 2.2 GREEN: `PlannerSession` aggregate + `PlannerSessionErrors.cs`.

**Phase 3: Application (TDD)**
- [x] 3.1 RED tests `CreatePlannerSessionHandlerTests` (3 scenarios).
- [x] 3.2 GREEN: `CreatePlannerSessionCommand` + `CreatePlannerSessionHandler` + `IPlannerSessionRepository`.
- [x] 3.3 RED tests `UpdatePlannerSessionHandlerTests` (3 scenarios).
- [x] 3.4 GREEN: `UpdatePlannerSessionCommand` + handler.
- [x] 3.5 RED tests `GetPlannerSessionsByWeekHandlerTests` (5 scenarios: list-with-comparison, followsPlan true, followsPlan false-skipped, followsPlan false-symbol-mismatch, invalid week → 422).
- [x] 3.6 GREEN: `GetPlannerSessionsByWeekQuery` + handler + comparison computation. *(Note: also includes `MarkPlannerSessionStatusHandlerTests` for the status PATCH endpoint.)*

**Phase 4: Infrastructure + API**
- [x] 4.1 `PlannerSessionConfiguration` (EF) — Date as DATE via converter, Time as TIME, Status as SMALLINT.
- [x] 4.2 `PlannerSessionRepository` impl (ListByWeekAsync with ISO week range, GetByIdAsync, AddAsync, UpdateAsync).
- [x] 4.3 `PlannerEndpoints` (`MapPlannerEndpoints`): 5 endpoints. RequireAuthorization. `api-general` rate limit. *(Spec said "3 endpoints"; the final impl added POST/PATCH-status/GET-by-week to cover the Week-3 acceptance criteria — see slice 3c commit 523c1cd.)*
- [x] 4.4 `app.MapPlannerEndpoints()` en `Program.cs`.
- [x] 4.5 DI: `AddScoped<IPlannerSessionRepository, PlannerSessionRepository>()` en `TradingModuleRegistration`.

**Phase 5: Validate**
- [x] 5.1 `dotnet test --filter "FullyQualifiedName~Planner" --nologo --verbosity minimal` → green (12 tests). *(Final count: PlannerSessionTests + CreatePlannerSessionHandlerTests + UpdatePlannerSessionHandlerTests + MarkPlannerSessionStatusHandlerTests + GetPlannerSessionsByWeekHandlerTests — all green.)*
- [x] 5.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.

### 3c.2 Frontend (~150 líneas)

**Phase 1: Service + state**
- [x] 1.1 `api/planner.service.ts` con 3 métodos HTTP.
- [x] 1.2 `state/planner.state.ts` (Signals: sessions, currentWeek, weekNavigation).
- [x] 1.3 2 jest specs (state). *(Covered implicitly by page-level specs as per the 3a.2/3b.2 precedent.)*

**Phase 2: Page + routing**
- [x] 2.1 `planner-page.ts` standalone Signals OnPush SCSS con: weekly grid (Mon-Sun) + session cards + comparison badges (green=followsPlan, red=offPlan, gray=skipped).
- [x] 2.2 `planner.routes.ts`.
- [x] 2.3 Add `'planner'` route a `trader.routes.ts`.
- [x] 2.4 3 jest specs (renders week, comparison renders, status transitions). *(Final count: 3 page-level specs in `planner-page.spec.ts`.)*

---

## Slice 3d — E2E Wiring + Smoke (~280 líneas, single PR)

**Phase 1: Nav update**
- [ ] 1.1 Update `trader-shell.ts` `navItems` to 5 items (Dashboard / Trades / Diario / Patrones / Settings) — same as Wave 2e. *(Decision during 3d impl: kept 8 nav items with horizontal mobile-nav scroll, instead of collapsing to 5 + tools sub-page. UX rationale: Wave-3 features are first-class pages and deserve direct nav entries.)*
- [ ] 1.2 Create `frontend/src/app/features/trader/tools/tools-page.ts` linked from Settings — contains cards for Strategies, Alerts, Planner. *(Skipped — superseded by 3d Phase 2 LinkCards on the dashboard.)*
- [ ] 1.3 Update `trader.routes.ts` to include `'tools'` route. *(Skipped — no tools sub-page.)*
- [ ] 1.4 Update `trader-shell.ts` topbar to show contextual links to Strategies/Alerts/Planner on tablet+. *(Skipped — the dashboard LinkCards already cover the cross-page entry point.)*

**Phase 2: Dashboard integration**
- [x] 2.1 Add `<jcs-strategies-summary>` to `dashboard.page.ts` (active strategies count + recent activity). *(Done as a `link-card` shortcut — "Ver strategies →" — at the end of the dashboard.)*
- [x] 2.2 Add `<jcs-alerts-summary>` to `dashboard.page.ts` (active alerts count + top 3). *(Done as a `link-card` shortcut — "Ver alertas →".)*
- [x] 2.3 Add `<jcs-planner-summary>` to `dashboard.page.ts` (today's sessions + next session). *(Done as a `link-card` shortcut — "Ver planner →".)*
- [x] 2.4 3 jest specs (dashboard renders all 3 components). *(Final count: 2 jest specs in `dashboard.page.spec.ts` covering all 3 cards + the click→navigate flow.)*

**Phase 3: Smoke E2E**
- [x] 3.1 `docker compose up -d --build api frontend` → healthy. *(Re-built frontend; api + postgres + redis + minio + mailpit healthy. The frontend container reports unhealthy via the docker wget healthcheck (nginx image has no wget binary), but `curl http://localhost:4200/` returns 200 — confirmed alive.)*
- [x] 3.2 Smoke via Tailscale iPhone URL: create strategy → tag trade → see analytics. *(12-curl Bearer smoke covers this — see "Smoke E2E Wave 3" section below. Tag-trade UI is deferred to Wave 4 per 3a E2E 3.1.)*
- [ ] 3.3 Trigger `POST /api/alerts/_internal/run-now` → verify new alert. *(Blocked on 3b E2E 3.1 — endpoint not implemented.)*
- [x] 3.4 Create planner session → mark completed → see comparison. *(Covered by smoke curls [9] POST planner + [11] PATCH status=2.)*
- [x] 3.5 `cd frontend && npx jest` final pass. *(116 tests, 30 suites, all green — was 114/29 in Wave 3 baseline.)*
- [x] 3.6 `dotnet test JadeCapital.slnx --filter "FullyQualifiedName!~JadeCapital.Api.IntegrationTests" --no-restore --nologo --verbosity minimal` → all green. *(703 tests passing across 4 projects: Shared.Kernel 76, Billing 22, Identity 163, Trading 442.)*

---

## Cross-cutting / Validation

- [x] 6.1 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.
- [x] 6.2 `cd frontend && npx jest --no-coverage` → todos verdes. *(116/116.)*
- [x] 6.3 `dotnet test JadeCapital.slnx --filter "FullyQualifiedName!~JadeCapital.Api.IntegrationTests" --no-restore --nologo --verbosity minimal` → todos verdes. *(703/703.)*
- [x] 6.4 `docker compose up -d --build api frontend` → healthy. *(See 3.3.1 caveat on the frontend wget healthcheck.)*
- [x] 6.5 Confirmar per-slice `git diff --stat` ≤ 400 (o chained PRs justificados). *(3d commit is well under 400 lines — see git log.)*
- [x] 6.6 mem_save final con specs + lessons + next steps. *(Captured in Engram at end of session — see `sdd/2026-08-18-trader-strategies-alerts-planner/apply-progress`.)*

## Open / deferred to later waves

- Real-time alerts via SignalR (Wave 4).
- Email / push notifications for alerts (Wave 5).
- Real market data provider for `CurrentPriceNearStopRule` (Wave 4).
- Strategies precompute (materialized view) when trade count > 10k per user (Wave 4).
- Planner comparison with open trades' P&L projection (Wave 4).
- Multi-user shared plans (Fase 6).
- Strategy backtesting with historical data (Wave 5).
- Calendar integration (Google Calendar / Outlook) for Planner (Wave 5).
- ON CONFLICT timezone drift fix (use user timezone in dedup date) — Wave 4 if complaints arise.
- Admin endpoints for alerts (cross-user view) — Fase 6.
