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
- [ ] 1.1 `infrastructure/postgres/migrations/0015a_strategies.sql` (idempotent, additive):
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
- [ ] 2.3 RED tests `TradeTests` (3 new scenarios): set strategy valid, set strategy null untag, set strategy on cancelled trade rejected.
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
- [ ] 5.2 `dotnet test --filter "FullyQualifiedName~Trade.Strategy" --nologo --verbosity minimal` → not separated; covered by handler tests.
- [x] 5.3 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings.

### 3a.2 Frontend (~200 líneas)

**Phase 1: Service + state**
- [x] 1.1 `api/strategies.service.ts` con 6 métodos HTTP (list, create, update, archive, getAnalytics, setTradeStrategy).
- [x] 1.2 `state/strategies.state.ts` (Signals: list, selectedId, analyticsById, isLoading, isSaving, error).
- [ ] 1.3 2 jest specs (state) — covered by 4 page-level specs (state is exercised implicitly).

**Phase 2: Page + routing**
- [x] 2.1 `strategies-page.ts` standalone Signals OnPush SCSS con: list + create form (inline) + analytics panel (per-card expand) + archive.
- [x] 2.2 `strategies.routes.ts` (sub-routes: `/strategies`).
- [x] 2.3 Add `'strategies'` route a `trader.routes.ts` (loadChildren → STRATEGIES_ROUTES).
- [ ] 2.4 LinkCard from Dashboard: deferred to slice 3d (no dashboard integration in 3a).
- [x] 2.5 4 jest specs (renders empty, create success, analytics visible, archive flow).

### 3a E2E wiring (final patch)
- [ ] 3.1 Patch `Trade` UI en `trades-list.page.ts` para taggear inline con strategy dropdown. (Deferred to 3d — out of scope for 3a per user instructions.)
- [x] 3.2 Smoke E2E: create strategy → list → analytics (count=0). Tag-trade UI not built in 3a; backend PUT /api/trades/{id}/strategy works via API curl.

---

## Slice 3b — Alerts (≤ 720 líneas, split 3b.1 + 3b.2)

### 3b.1 Backend (~520 líneas)

**Phase 1: Shared abstraction**
- [ ] 1.1 `Shared.Kernel/Coaching/AlertSeverity.cs` (enum byte 1..3).
- [ ] 1.2 `Shared.Kernel/Alerts/IAlertRule.cs` (interface + `AlertContext` record).
- [ ] 1.3 `Shared.Kernel/Alerts/AlertWire.cs` (wire-shape record).
- [ ] 1.4 RED tests `AlertSeverityTests` (3 scenarios: ordinal, ranges).
- [ ] 1.5 RED tests `AlertWireTests` (2 scenarios: serialize/deserialize).

**Phase 2: Domain (TDD)**
- [ ] 2.1 RED tests `AlertTests` (6 scenarios del spec): create valid, title length cap, body length cap, acknowledge sets timestamp, ack idempotent, expires_at optional).
- [ ] 2.2 GREEN: `Alert` aggregate + `AlertErrors.cs`.

**Phase 3: Application (TDD)**
- [ ] 3.1 `AlertRegistry` (constructor + `EvaluateForUser(AlertContext)` + try/catch per rule).
- [ ] 3.2 RED tests `AlertRegistryTests` (3 scenarios: empty input, 1 rule fires, 1 rule throws others continue).
- [ ] 3.3 GREEN: `AlertRegistry` implementation.
- [ ] 3.4 RED tests `GetAlertsHandlerTests` (3) + `AcknowledgeAlertHandlerTests` (2) + `GetAlertByIdHandlerTests` (2).
- [ ] 3.5 GREEN: 3 handlers + DTOs (`AlertDto`).

**Phase 4: 5 rules**
- [ ] 4.1 `Trading.Application/Features/Alerts/Rules/NoTradesInDaysRule.cs` (8 unit tests).
- [ ] 4.2 `DrawdownExceededRule.cs` (8 unit tests).
- [ ] 4.3 `RRAverageBelowRule.cs` (6 unit tests).
- [ ] 4.4 `CurrentPriceNearStopRule.cs` (4 unit tests; uses `EntryPrice` proxy).
- [ ] 4.5 `OpenTradeOffPlanRule.cs` (6 unit tests).
- [ ] 4.6 RED tests `NoTradesInDaysRuleTests` (preconditions + thresholds + dedup boundary).
- [ ] 4.7 RED tests `DrawdownExceededRuleTests` (peak calc + threshold).
- [ ] 4.8 RED tests `RRAverageBelowRuleTests` (last 10 trades + threshold).
- [ ] 4.9 RED tests `CurrentPriceNearStopRuleTests` (proxy logic + 1% threshold).
- [ ] 4.10 RED tests `OpenTradeOffPlanRuleTests` (symbol match + plan match).

**Phase 5: Background service**
- [ ] 5.1 `AlertEvaluationBackgroundService` (BackgroundService + scope factory + jitter).
- [ ] 5.2 `EvaluateAlertsForAllUsersHandler` (loads active users + iterates).
- [ ] 5.3 `EvaluateAlertsForUserHandler` (loads context + calls registry + persists with dedup).
- [ ] 5.4 RED tests `AlertEvaluationBackgroundServiceTests` (3 scenarios: tick fires, tick error doesn't crash, dispose cancels).
- [ ] 5.5 RED tests `EvaluateAlertsForUserHandlerTests` (3 scenarios: dedup'd insert, new alert created, expired alert inserted).

**Phase 6: Infrastructure + API**
- [ ] 6.1 `AlertConfiguration` (EF) — severity SHORT, body VARCHAR(500), indexes see design.md.
- [ ] 6.2 `AlertRepository` impl (ListAsync with activeOnly filter, GetByIdAsync, AddAsync with ON CONFLICT DO NOTHING, UpdateAsync).
- [ ] 6.3 `AlertEndpoints` (`MapAlertsEndpoints`): 3 endpoints. RequireAuthorization. `api-general` rate limit.
- [ ] 6.4 `app.MapAlertsEndpoints()` en `Program.cs`.
- [ ] 6.5 DI: `AddSingleton<IAlertRule, NoTradesInDaysRule>()` × 5 + `AddSingleton<AlertRegistry>()` + `AddHostedService<AlertEvaluationBackgroundService>()` en `TradingModuleRegistration`.

**Phase 7: Validate**
- [ ] 7.1 `dotnet test --filter "FullyQualifiedName~Alert" --nologo --verbosity minimal` → green (15 tests).
- [ ] 7.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.

### 3b.2 Frontend (~200 líneas)

**Phase 1: Service + state**
- [ ] 1.1 `api/alerts.service.ts` con 3 métodos HTTP.
- [ ] 1.2 `state/alerts.state.ts` (Signals: list, activeOnly flag).
- [ ] 1.3 2 jest specs (state).

**Phase 2: Page + routing**
- [ ] 2.1 `alerts-page.ts` standalone Signals OnPush SCSS con: lista de alertas activas + ack button + severity badges (high=red, medium=yellow, low=blue).
- [ ] 2.2 `alerts.routes.ts`.
- [ ] 2.3 Add `'alerts'` route a `trader.routes.ts`.
- [ ] 2.4 LinkCard from Dashboard: "Tenés X alertas activas →" condicional.
- [ ] 2.5 3 jest specs (renders active, ack flow, severity order).

### 3b E2E wiring (final patch)
- [ ] 3.1 Trigger manual evaluation via `POST /api/alerts/_internal/run-now` (DEV-ONLY).
- [ ] 3.2 Smoke E2E: create test data → run-now → verify alert created.

---

## Slice 3c — Planner (≤ 480 líneas, split 3c.1 + 3c.2)

### 3c.1 Backend (~330 líneas)

**Phase 1: Shared enums**
- [ ] 1.1 `Shared.Kernel/Enums/PlannerStatus.cs` (enum byte 0..3).
- [ ] 1.2 RED tests `PlannerStatusTests` (2 scenarios).

**Phase 2: Domain (TDD)**
- [ ] 2.1 RED tests `PlannerSessionTests` (6 scenarios del spec): create valid, status default, end-before-start rejected, notes length cap, status transitions, cross-user guard).
- [ ] 2.2 GREEN: `PlannerSession` aggregate + `PlannerSessionErrors.cs`.

**Phase 3: Application (TDD)**
- [ ] 3.1 RED tests `CreatePlannerSessionHandlerTests` (3 scenarios).
- [ ] 3.2 GREEN: `CreatePlannerSessionCommand` + `CreatePlannerSessionHandler` + `IPlannerSessionRepository`.
- [ ] 3.3 RED tests `UpdatePlannerSessionHandlerTests` (3 scenarios).
- [ ] 3.4 GREEN: `UpdatePlannerSessionCommand` + handler.
- [ ] 3.5 RED tests `GetPlannerSessionsByWeekHandlerTests` (5 scenarios: list-with-comparison, followsPlan true, followsPlan false-skipped, followsPlan false-symbol-mismatch, invalid week → 422).
- [ ] 3.6 GREEN: `GetPlannerSessionsByWeekQuery` + handler + comparison computation.

**Phase 4: Infrastructure + API**
- [ ] 4.1 `PlannerSessionConfiguration` (EF) — Date as DATE via converter, Time as TIME, Status as SMALLINT.
- [ ] 4.2 `PlannerSessionRepository` impl (ListByWeekAsync with ISO week range, GetByIdAsync, AddAsync, UpdateAsync).
- [ ] 4.3 `PlannerEndpoints` (`MapPlannerEndpoints`): 3 endpoints. RequireAuthorization. `api-general` rate limit.
- [ ] 4.4 `app.MapPlannerEndpoints()` en `Program.cs`.
- [ ] 4.5 DI: `AddScoped<IPlannerSessionRepository, PlannerSessionRepository>()` en `TradingModuleRegistration`.

**Phase 5: Validate**
- [ ] 5.1 `dotnet test --filter "FullyQualifiedName~Planner" --nologo --verbosity minimal` → green (12 tests).
- [ ] 5.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.

### 3c.2 Frontend (~150 líneas)

**Phase 1: Service + state**
- [ ] 1.1 `api/planner.service.ts` con 3 métodos HTTP.
- [ ] 1.2 `state/planner.state.ts` (Signals: sessions, currentWeek, weekNavigation).
- [ ] 1.3 2 jest specs (state).

**Phase 2: Page + routing**
- [ ] 2.1 `planner-page.ts` standalone Signals OnPush SCSS con: weekly grid (Mon-Sun) + session cards + comparison badges (green=followsPlan, red=offPlan, gray=skipped).
- [ ] 2.2 `planner.routes.ts`.
- [ ] 2.3 Add `'planner'` route a `trader.routes.ts`.
- [ ] 2.4 3 jest specs (renders week, comparison renders, status transitions).

---

## Slice 3d — E2E Wiring + Smoke (~280 líneas, single PR)

**Phase 1: Nav update**
- [ ] 1.1 Update `trader-shell.ts` `navItems` to 5 items (Dashboard / Trades / Diario / Patrones / Settings) — same as Wave 2e.
- [ ] 1.2 Create `frontend/src/app/features/trader/tools/tools-page.ts` linked from Settings — contains cards for Strategies, Alerts, Planner.
- [ ] 1.3 Update `trader.routes.ts` to include `'tools'` route.
- [ ] 1.4 Update `trader-shell.ts` topbar to show contextual links to Strategies/Alerts/Planner on tablet+.

**Phase 2: Dashboard integration**
- [ ] 2.1 Add `<jcs-strategies-summary>` to `dashboard.page.ts` (active strategies count + recent activity).
- [ ] 2.2 Add `<jcs-alerts-summary>` to `dashboard.page.ts` (active alerts count + top 3).
- [ ] 2.3 Add `<jcs-planner-summary>` to `dashboard.page.ts` (today's sessions + next session).
- [ ] 2.4 3 jest specs (dashboard renders all 3 components).

**Phase 3: Smoke E2E**
- [ ] 3.1 `docker compose up -d --build api frontend` → healthy.
- [ ] 3.2 Smoke via Tailscale iPhone URL: create strategy → tag trade → see analytics.
- [ ] 3.3 Trigger `POST /api/alerts/_internal/run-now` → verify new alert.
- [ ] 3.4 Create planner session → mark completed → see comparison.
- [ ] 3.5 `cd frontend && npx jest` final pass.
- [ ] 3.6 `dotnet test JadeCapital.slnx --filter "FullyQualifiedName!~JadeCapital.Api.IntegrationTests" --no-restore --nologo --verbosity minimal` → all green.

---

## Cross-cutting / Validation

- [ ] 6.1 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.
- [ ] 6.2 `cd frontend && npx jest --no-coverage` → todos verdes.
- [ ] 6.3 `dotnet test JadeCapital.slnx --filter "FullyQualifiedName!~JadeCapital.Api.IntegrationTests" --no-restore --nologo --verbosity minimal` → todos verdes.
- [ ] 6.4 `docker compose up -d --build api frontend` → healthy.
- [ ] 6.5 Confirmar per-slice `git diff --stat` ≤ 400 (o chained PRs justificados).
- [ ] 6.6 mem_save final con specs + lessons + next steps.

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
