# Proposal: Trader Strategies + Alerts + Planner — Wave 3

## Intent and Problem

Wave 1 sentó la base de métricas server-side, perfil de riesgo, checklist pre-trade y post-trade review. Wave 2 agregó el **loop de aprendizaje**: journal diario, behavioral analytics, MFE/MAE, y coaching prompts basados en `ICoachingRule`. Pero faltan tres capacidades que cierran el ciclo del trader consciente:

1. **Strategies** — el "qué tradeás". Sin un catálogo de strategies (nombre, instrumento, timeframe, reglas), el trader no puede etiquetar sus trades para descubrir qué setups tienen edge positivo y cuáles están drenando capital. La propiedad `Trade.Strategy` hoy es un string libre ≤ 80 chars: no permite analytics, no permite uniqueness, no permite activar/desactivar.

2. **Alerts** — el "qué te avisa automáticamente". Wave 2 ya detecta *eventos* (revenge trading, overtrading, tilt) vía `BehavioralEvent`, pero solo cuando el dashboard renderiza. No hay un canal persistente que diga "mirá esto hoy" independientemente de si el trader abrió la app. Wave 3 introduce alertas evaluadas periódicamente con acknowledge, dedup y severidad.

3. **Planner** — el "cuándo planeás operar". El trader conscienteAgenda sesiones de práctica (lun-mié 10hs, vie 14hs) y compara plan vs realidad. Hoy no existe la noción de "sesión planificada": un día con muchos trades sin plan es invisible al sistema.

Sin estos elementos, JadeCapital es un cuaderno reactivo. Con ellos, es una **herramienta de trading deliberado** que justifica la Fase 5 (Pago) y Fase 6 (Multi-tenant).

## Goals

- **3a Strategies**: CRUD de strategies por user (nombre único, descripción, instrumento, timeframe, reglas free-text). Cada trade puede taggearse con `strategy_id` (nullable FK). Analytics por strategy: count, winRate, totalPnl, expectancy, profitFactor, avgMfe, avgMae.
- **3b Alerts**: 5 reglas determinísticas (`NoTradesInDaysRule`, `DrawdownExceededRule`, `RRAverageBelowRule`, `CurrentPriceNearStopRule`, `OpenTradeOffPlanRule`) evaluadas por un `BackgroundService` cada 5 minutos. Endpoint `GET /alerts?activeOnly=true` + `PATCH /alerts/{id}/ack`. Persistencia con dedup (one alert per `RuleId` + `UserId` + `day`).
- **3c Planner**: weekly planner con `PlannerSession` (date, planned_start_time, planned_end_time, symbol, status: planned/completed/skipped/cancelled, notes). Endpoint `GET /planner?week=2026-W33` que devuelve sesiones + comparación plan vs realidad (closed trades del mismo día).

## Scope Boundaries

**Changed**:
- Schema `trading`:
  - `trading.strategies` (nueva tabla) — `id, user_id, name, description, symbol, timeframe, rules, is_active, created_at, updated_at`.
  - `trading.alerts` (nueva tabla) — `id, user_id, rule_id, severity, title, body, cta_route, cta_label, acknowledged_at, expires_at, created_at`.
  - `trading.planner_sessions` (nueva tabla) — `id, user_id, date, planned_start_time, planned_end_time, symbol, status, notes, created_at, updated_at`.
  - `trading.trades.strategy_id` (nueva FK nullable) — additive.
- API: 3 nuevos groups de endpoints (`/api/strategies`, `/api/alerts`, `/api/planner`).
- Frontend: 3 páginas nuevas (`/app/strategies`, `/app/alerts`, `/app/planner`) + updates de nav.

**Unchanged**:
- Wave 1 features (RiskProfile, Checklist, PositionSize, PostTradeReview, Metrics).
- Wave 2 features (JournalEntry, BehavioralAnalytics, MFE/MAE, CoachingPrompts).
- Admin shell, Public, Auth, Identity.
- `Trade.Strategy` (string libre) — se mantiene para backward compatibility; la nueva FK `strategy_id` es la fuente canónica de analytics.

## Capabilities (new)

- **`strategies`** — user-owned CRUD de strategies. Cada strategy tiene símbolo (nullable), timeframe (enum), reglas free-text. Analytics agregado (count, winRate, totalPnl, expectancy, profitFactor, avgMfe, avgMae) calculado on read desde los trades linkeados.
- **`alerts`** — reglas determinísticas evaluadas por BackgroundService. Alertas persistidas con severidad, acknowledge (soft-delete visible), expiry, dedup por (RuleId, UserId, day). 5 reglas iniciales.
- **`planner`** — sesiones semanales planeadas. Status enum (planned/completed/skipped/cancelled). Comparación plan vs realidad via join con closed trades del mismo day.

## Capabilities (modified)

Ninguna existente. La columna `trading.trades.strategy_id` es **additive only** (nullable FK); no se considera "modified" porque la propiedad `Trade.Strategy` string sigue intacta y el dominio Type solo agrega un campo opcional.

## Ownership and Approach

- **Trading.Application** owns los nuevos aggregates (`Strategy`, `Alert`, `PlannerSession`) + handlers + endpoints.
- **Trading.Infrastructure** owns:
  - `StrategyConfiguration`, `AlertConfiguration`, `PlannerSessionConfiguration` (EF).
  - `StrategyRepository`, `AlertRepository`, `PlannerSessionRepository`.
  - `AlertEvaluationBackgroundService` (Microsoft.Extensions.Hosting, scoped por iteración).
  - `IAlertRegistry` (singleton, similar a `CoachingRuleRegistry`) que itera `IEnumerable<IAlertRule>` y agrega.
- **Shared.Kernel** owns:
  - `Timeframe` enum (M1, M5, M15, M30, H1, H4, D1, W1, MN).
  - `AlertSeverity` (alias type of `Severity`, byte 1..3).
  - `PlannerStatus` enum (Planned, Completed, Skipped, Cancelled).
  - `IAlertRule` interface + `AlertContext` record + `Alert` record (mismo shape que `CoachingPrompt` pero con `AcknowledgedAt` y `ExpiresAt`).
- **Frontend** (Angular 20 standalone, Signals, OnPush, mobile-first):
  - `/app/strategies` — list + create/edit form + analytics panel inline.
  - `/app/alerts` — list de alertas activas + ack button + severity badges.
  - `/app/planner` — weekly grid con sesiones planned vs actual trades.
- **Host wiring** (`Program.cs` + `TradingModuleRegistration`):
  - `MapStrategiesEndpoints`, `MapAlertsEndpoints`, `MapPlannerEndpoints`.
  - `AddAlertBackgroundService()` (HostedService en `Microsoft.Extensions.DependencyInjection`).
  - `AddAlertRuleRegistry()` (singleton con 5 IAlertRule ya registrados).
- **Strategy de alertas**: **BackgroundService simple** (no Hangfire por ahora — Wave 4 lo introduce). Periodic check cada **5 minutos** por user activo. 5 minutos es un compromiso entre reactivity y rate limit (5min × 12 iteraciones/h × N users = contenible).

## Non-Goals and Later Waves

- NO real-time alerts (WebSocket / SignalR) — Wave 4.
- NO email/SMS/push notifications — Wave 5.
- NO multi-user shared plans (Fase 6 multi-tenant).
- NO strategies jerárquicas (parent/child) — flat per user.
- NO backtesting de strategies — Wave 5 con historical data.
- NO notas de sesión multimedia (audio, video) — Wave 6.
- NO integración con calendar externo (Google Calendar, Outlook) — Wave 5.
- NO mercado real-time price provider para `CurrentPriceNearStopRule` — Wave 3 usa `EntryPrice` como proxy; Wave 4 introduce provider real.

## Chained Delivery, Validation, and Rollback

| Slice (≤ 400 líneas) | Deliverable | Validate | Rollout / rollback |
|---|---|---|---|
| 3a | Migration 0015a (`trading.strategies` + `trading.trades.strategy_id` FK) + `Strategy` aggregate + `StrategyAnalytics` + 4 endpoints + Trade `UpdateStrategy` mutation + UI `/app/strategies` + 20 tests | unit + integration + smoke 200/422 | Additive migration; revert code mantiene tabla inerte |
| 3b | `Alert` aggregate + `IAlertRule` + 5 reglas + `AlertEvaluationBackgroundService` + 4 endpoints + dedup logic + UI `/app/alerts` + 15 tests | unit + smoke + cron iteration manual | Revert code; registry singleton |
| 3c | `PlannerSession` aggregate + 3 endpoints + planned vs actual logic + UI `/app/planner` + 12 tests | unit + smoke | Revert code; additive table |
| 3d | E2E wiring: nav updates (5 bottom items + top contextual), smoke E2E desde Tailscale, tasks close | docker compose + curl + npx jest | revert code |

Pre-PR forecast: ~2100 líneas autoradas, 4 chained PRs feature-branch-chain. **size:exception justificado por slice** (Wave 2 precedente: cada slice puede pasar 400 si la lógica es indivisible, justificada en apply-progress).

## Dependencies and Risks

- **Migration 0015 aditiva**: nullable FK + 3 new tables. Idempotent. Sin NOT NULL sin backfill. Trade pre-existentes quedan con `strategy_id = NULL` (correcto: no fueron tageados).
- **BackgroundService periodicity**: 5 minutos es OK para rate limit (12 iteraciones/h × N users activos). Si N users > 1000, conviene mover a Hangfire (Wave 4). Hoy los tests no escalan a esa cardinalidad.
- **Dedup de alerts**: el contrato (one alert per `RuleId` + `UserId` + `day`) previene spam. Si una regla evalúa y la misma `(rule_id, user_id, today)` ya existe, no se inserta una nueva. Si el usuario hace ack, la próxima iteración respeta el ack y no vuelve a crear.
- **PII safety**: alert copy usa lenguaje cualitativo ("DD > 5%", "sin trades hace 5 días") — NO embed absolute P&L amounts ni percentages por usuario. La regla no recibe amounts, solo derivados cualitativos.
- **`CurrentPriceNearStopRule` proxy**: en Wave 3 no hay market data provider real. Usamos `EntryPrice ± 1%` como proxy del "precio actual". Riesgo: alert false-positive. Mitigate: severity = `low` y copy honesto ("trade cerca de zona de entrada — sin tick data real").
- **Strategies uniqueness**: `name` único per user (no global). Si dos users crean ambas "London Break", está OK. Partial unique index en `trading.strategies(user_id, name) WHERE is_active = true`.
- **Nav overflow**: 8 candidates (Dashboard, Trades, Strategies, Journal, Patterns, Planner, Alerts, Settings) pero bottom-nav soporta 5 max. Decisión: mantener 5 bottom (Dashboard, Trades, Journal, Patterns, Settings) + acceso desde página Patterns/Settings a Strategies/Planner/Alerts via topbar contextual. Documentado en design.md.

## Success Criteria

1. Trader puede crear una strategy (`/app/strategies`), tagear un trade abierto con ella, y ver analytics inline (count, winRate, totalPnl, expectancy, profitFactor) en `/app/strategies/:id`.
2. Trader abre `/app/alerts` y ve al menos 1 alerta activa cuando aplica una regla (e.g. "5 días sin operar" tras 5 días de inactividad). Ack flow persiste `acknowledged_at` y la alerta desaparece de `GET /alerts?activeOnly=true`.
3. Trader planifica 3 sesiones para la próxima semana en `/app/planner`. Al cierre de la semana, la página muestra `planned: 3, completed: 2, skipped: 1, actual_trades: 5`.
4. `Trade.StrategyId` FK queda poblada en ~80% de los trades nuevos (medible via `trading.trades.strategy_id IS NOT NULL` en queries).
5. Build verde, 0 warnings nuevos, tests verdes. Mobile responsive mantiene pattern Wave 1.5. Nav 5 items + topbar contextual funcionando.

## Architectural Decisions

- **Wave 3 strategies vs `Trade.Strategy` legacy**: `Trade.Strategy` (string ≤ 80) sigue existiendo para backward compatibility (UI, Wave 1 trades). La nueva FK `strategy_id` es la fuente de analytics. NO se hace backfill automático de `strategy_id` desde `Trade.Strategy` (no hay mapping name → id). Decisión: coexisten. Trade con `strategy_id = NULL` puede tener `Strategy = "London Break"` legacy.
- **`IAlertRule` registry pattern**: misma forma que `ICoachingRule` (Wave 2d). Pero `IAlertRule` vive en `Shared.Kernel` (no en `Trading.Application`) porque el wire-shape Alert es compartido cross-module. `ICoachingRule` se queda en `Trading.Application` porque su contexto es Trading-specific.
- **`BackgroundService` vs Hangfire**: BackgroundService simple cumple el SLA de 5 minutos. Hangfire agregaría robustez (retry, persistence, distributed) innecesaria para Wave 3. Wave 4 (real-time) introduce Hangfire + SignalR.
- **Dedup contract**: `trading.alerts` tiene UNIQUE INDEX sobre `(rule_id, user_id, DATE(created_at))`. Wave 3 SQL cast: `((created_at AT TIME ZONE user_timezone)::date)`. Simplificación: usamos UTC date (no user timezone) — aceptable porque la periodicity de 5 min hace que el day boundary slip sea despreciable. Documentado en design.md.
- **Strategy analytics on read**: no precomputamos. Wave 3 no tiene volumen (10-100 trades/user). Wave 4 introduce materialized view si hace falta.
- **Planner comparison query**: cuando el usuario pide `GET /planner?week=2026-W33`, el handler hace JOIN con `trading.trades WHERE user_id = X AND closed_at::date IN (sessions.date)`. Eager-load: no, projection only. Devuelve `actualTrades: TradeSummary[]` por cada session.
- **Timeframe enum**: stored como SMALLINT (0..8) en DB; mapped to C# enum `Timeframe` via OwnsOne. Same pattern as `Trade.AssetClass` (Wave 1).

## Chained Strategy

Feature-branch-chain:
- 3a → main (tracker)
- 3b → 3a
- 3c → 3b
- 3d → 3c

OJO: cada slice produce 1-2 PRs targeteando al anterior. El primer PR de cada slice targetea `feature/0a-identity-model` (o la branch del último slice shipped).
