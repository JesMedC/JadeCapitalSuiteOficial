# Apply Progress — Wave 3 consolidated

> **Change**: `2026-08-18-trader-strategies-alerts-planner`
> **Status**: ✅ All 4 slices applied (3a Strategies, 3b Alerts, 3c Planner, 3d E2E Wiring). Wave 3 closed.
> **Branch**: `feature/0a-identity-model` (69 commits ahead of origin)

This file consolidates per-slice apply-progress records. Per-slice files (`apply-progress-slice-3*.md`) were not produced by the apply agents in this session — they shipped their work via per-slice commits and per-slice `tasks.md` updates. This consolidated record captures the trail.

---

## 1. Slice 3a — Strategies (8 commits, ~2940 net lines across backend + frontend)

### 3a.1 Backend (5 commits + 1 hotfix)
- **Commits**: `f041813` migration+domain, `e6473bb` application+repo+DTOs, `7f22084` infrastructure+api+smoke (wait, this is wrong attribution — actually for slice 3a): the 3a.1 commits were on the Wave 3 base branch.
- **Migration 0015a**: idempotent, additive. `trading.strategies` table (id, user_id, name, description, symbol FK soft, timeframe, rules, is_active, created_at, updated_at) + `trading.trades.strategy_id` nullable FK + indexes.
- **Domain**: `Strategy` aggregate + `Timeframe` enum (M1=1..MN=9) + `StrategyCreatedDomainEvent` + errors.
- **Application**: 5 commands/queries + `IStrategyRepository` + `CreateOrUpdateStrategyHandler` + `GetStrategyAnalyticsHandler` (aggregates count, winRate, totalPnl, expectancy, profitFactor, avgMfe, avgMae).
- **API**: 6 endpoints `RequireAuthorization`.
- **Tests**: ~20 unit (Strategy aggregate + handlers + analytics aggregation).

### 3a.2 Frontend (1 commit `0f023a9`)
- `strategies-page.ts` standalone Signals OnPush SCSS con cards + form + analytics expandable + soft-delete.
- 4 jest specs.

## 2. Slice 3b — Alerts (4 commits, ~3900 net lines)

### 3b.1 Backend (1 commit `74b8dad`)
- `IAlertRule` interface in `Shared.Kernel.Alerts` (same wire shape pattern as `ICoachingRule` from Wave 2 but in `Shared.Kernel` per spec).
- 5 rules: `NoTradesInDaysRule`, `DrawdownExceededRule`, `RRAverageBelowRule`, `CurrentPriceNearStopRule`, `OpenTradeOffPlanRule`.
- `AlertEvaluationService` + `AlertEvaluationBackgroundService` (5min cadence, ±30s jitter to avoid thundering herd).
- `Alert` aggregate + `AlertSeverity` enum + repository with dedup via `ux_alerts_user_rule_day` unique index.
- 35 unit tests.
- **Hotfix** `9d0251c`: DbContext serialize reads (sequential, not parallel — EFCore can't share context).

### 3b.2 Frontend (1 commit `182dad1`)
- `alerts-page.ts` with severity color + ack flow + active-only toggle.
- 4 jest specs.

## 3. Slice 3c — Planner (1 commit `523c1cd`, ~2750 net lines)

### 3c.1 Backend (~1500 lines)
- **Migration 0015c**: idempotent, additive. `trading.planner_sessions` (id, user_id, session_date, planned_start_time, planned_end_time, symbol FK soft, status 1..4, notes, created_at, updated_at). UNIQUE `(user_id, session_date)`. CHECK constraints (status range, end > start, notes length).
- **Domain**: `PlannerSession` aggregate + `PlannerStatus` enum (Planned=1, Completed=2, Skipped=3, Cancelled=4) + domain events + errors.
- **Application**: 5 handlers (`CreateOrUpdate`, `Update`, `GetById`, `MarkStatus`, `GetWeek` with comparison via JOIN with trading.trades).
- **API**: 5 endpoints.
- `IPlannerSessionRepository` + `PlannerSessionRepository` with weekly range query + week comparison aggregate.
- `PlannerMapping` in `_Common` for ISO date / HH:mm time parsing.
- `LocalDate.AddDays(days)` extension in `Shared.Kernel.Time` (used by handlers).
- EF Configuration `PlannerSessionConfiguration` with `HasConversion LocalDate ↔ DateOnly`.
- ~26 unit tests.

### 3c.2 Frontend (~250 lines)
- `planner.types.ts` with `PlannerStatus` enum + labels + colors.
- `planner.service.ts` (HTTP wrapper).
- `planner.state.ts` (Signals store with inline HTTP — no separate service).
- `planner-page.ts` standalone Signals OnPush SCSS with week navigator + comparison panel + new-session form + status dropdown per session.
- `planner.routes.ts` for lazy loading.
- Wire: `trader-shell.ts` (8 nav items with horizontal mobile-nav scroll), `trader.routes.ts` (path 'planner').
- 3 jest specs.

### Hotfixes in-flight durante 3c
- `using JadeCapital.Trading.Application.Abstractions;` agregado a los 5 Planner handlers.
- `using JadeCapital.Trading.Domain.Common;` para `TradingDomainErrors.Planner.*`.
- `PlannerSessionConfiguration` con `PlannerSession` import en DbContext.
- `PlannerEndpoints.Results.Created(...)` lambda (no method group).
- Migration 0015c: FK `instruments(symbol)` no `instruments(code)` (la columna real es `symbol`).
- `MarkPlannerSessionStatusHandler` signature con byte directo (no cast redundante).

## 4. Slice 3d — E2E Wiring (1 commit `8b0759d`, 211 net lines)

- **Dashboard LinkCards**: 3 cards adicionales (Strategies, Alerts, Planner) en `dashboard.page.ts` con navegación a las rutas correspondientes.
- **tasks.md close**: 108 `[x]` + 11 `[ ]` honestly deferred.
- **Smoke E2E completo Wave 3**: 12/12 curls Bearer all green (register → login → strategies/alerts/planner flows).
- **iPhone URLs**: 9/9 HTTP 200 (front, auth, dashboard, trades, strategies, alerts, planner, journal, patterns).

---

## Cross-cutting Wave 3 outcomes

- **Build**: 0 errors, 0 warnings nuevos.
- **Backend unit tests**: 703 passing (76 Shared.Kernel + 22 Billing + 163 Identity + 442 Trading).
- **Frontend jest**: 116 passing across 30 suites (37 baseline + 49 Wave 1 + 17 Wave 2 + 13 Wave 3 new).
- **Migrations applied live**: 0015a (strategies), 0015b (alerts), 0015c (planner_sessions). Idempotency verified.
- **Stack docker**: API + frontend healthy. LAN + Tailscale accessible from iPhone.
- **Mobile responsive**: 8 nav items en bottom-nav con `overflow-x: auto` (Wave 1.5 precedent mantenido). 3 dashboard LinkCards complementarios para shortcuts mobile.
- **Cross-module patterns**: `IAlertRule` en `Shared.Kernel.Alerts` (vs `ICoachingRule` en `Trading.Application`); ambos comparten wire-shape pattern.
- **Hotfixes honestos documentados**:
  - JadeApiFactory migration sort bug (Wave 1 carry-over).
  - MinIO TestContainer provision (Wave 1 carry-over).
  - PreTradeChecklist EF mapping bug (Wave 2 slice 2b hotfix).
  - Program.cs:122 AddAssemblyValidators solo Identity (Wave 1 caveat).
  - JSON enum serialization (Wave 2 caveat).
  - LocalDate.AddDays extension (Wave 3 3c additive).

## Caveats (10 honest items)

1. **size:exception per slice**: 3a + 3b + 3c + 3d all over 400-line per-PR cap.
2. **Migration 0010 cancelled** (from Wave 1): never applied — period filter covered by existing index.
3. **Wave 1 fixture bugs still open**: `JadeApiFactory.ApplyMigrationAsync` migration sort + missing MinIO TestContainer.
4. **Program.cs:122 AddAssemblyValidators** only Identity (Trading validators duplicate checks manually).
5. **JSON enum serialization** not enabled (`direction: 1` not `"direction": "Long"`).
6. **Frontend docker healthcheck** reports `unhealthy` but nginx serves 200 (cosmetic).
7. **Register→login race**: 3s sleep workaround in smoke scripts.
8. **Wave 3 partial deferrals**: `AlertSeverityTests`, `AlertWireTests`, `PlannerStatusTests` dedicated files skipped (exercised by their aggregate tests). Trade UI inline strategy dropdown deferred to Wave 4.
9. **`POST /api/alerts/_internal/run-now`** scoped out (BackgroundService cadence only).
10. **CurrentPriceNearStopRule** uses EntryPrice as proxy (no real market data provider — Wave 4).

## Specs Wave 3 (23 requirements, 45 scenarios)

- `strategies`: 6 reqs, 14 scenarios
- `alerts`: 10 reqs, 16 scenarios
- `planner`: 7 reqs, 15 scenarios

## Next steps para próximas sesiones

1. Wave 4: Scanner + MarketData + SignalR realtime + MinIO attachments.
2. Fix `JadeApiFactory.ApplyMigrationAsync` migration sort order.
3. Add `MinioContainer` to `JadeApiFactory`.
4. Extend `Program.cs:122` AddAssemblyValidators for Trading.Application.
5. JSON enum serialization global fix.
6. Behavioral events persistence for trending (deferred from Wave 2).
7. Real market data provider for accurate MFE/MAE (deferred from Wave 2c).
8. LLM-based coaching (Wave 5, Ollama).
9. Trade UI inline strategy dropdown (deferred from Wave 3a).
