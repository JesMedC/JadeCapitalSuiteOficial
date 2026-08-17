# Apply Progress — Wave 2 consolidated

> **Change**: `2026-08-17-trader-journal-core`
> **Status**: ✅ All 5 slices applied (2a, 2b, 2c, 2d, 2e). Wave 2 closed.
> **Branch**: `feature/0a-identity-model` (54 commits ahead of origin)

This file consolidates per-slice apply-progress records. Per-slice files are retained in this folder for audit.

---

## 1. Slice 2a — Journal Daily (~1800 net lines across backend + frontend)

### 2a.1 Backend (3 commits)
- **Commits**: `f041813` migration+domain, `e6473bb` application+repo+DTOs, `7f22084` infrastructure+api+smoke.
- **size:exception**: documented (~700 lines vs 400 cap).
- **Migration `0013_journal_entries.sql`**: idempotent, additive. UNIQUE INDEX `(user_id, local_date)`. CHECK constraints per spec (mood 1-5, plan ≤ 2000, reflection ≤ 5000, ≤ 10 tags, each tag ≤ 32).
- **Domain**: `JournalEntry` aggregate + `Mood` VO + `LocalDate` time primitive + 2 domain events.
- **Application**: 4 handlers (CreateOrUpdate / GetToday / GetRange / Delete) + `IUserTimezoneAccessor` (reads `X-User-Timezone` header, fallback UTC).
- **API**: 4 endpoints `RequireAuthorization` (GET today / GET range / POST today / DELETE id). Rate limit `api-general`.
- **Tests**: +14 domain + 15 application = 29 nuevos unit tests.

### 2a.2 Frontend (1 commit `51fb444`)
- 9 files, +971/-2.
- `journal.service.ts` (HTTP wrapper) + `journal.state.ts` (Signals) + `journal-page.ts` (507 líneas con inline SCSS mobile-first) + 4 jest specs.
- Bottom-nav entry `'Diario'` agregada al `trader-shell.ts` (5 items total).
- 4 jest specs: renders empty / save success / 422 validation / delete confirmation.

---

## 2. Slice 2b — Behavioral Analytics (~1986 net lines)

### 2b.1 Backend (2 commits + 1 hotfix)
- **Commits**: `94f62ee` domain analyzer + 5 rules + 10 tests, `2a348f1` application + endpoint + repo extensions.
- **Hotfix** `5ec1562`: `PreTradeChecklistConfiguration` mapeaba `CreatedAt/UpdatedAt` que NO existen en migration 0011. Fix: `.Ignore()` ambos.
- **Domain**: `BehavioralEvent` record + `Severity` enum + `EmotionalityBucket` + `BehavioralAggregations` + `BehavioralAnalyzer` pure function.
- **5 rules**: RevengeTrade, OvertradingDay, TiltSequence, OverconfidenceAfterWin, EmotionalityAggregation. Thresholds como constants configurables.
- **API**: `GET /api/trades/behavioral?period=7d|30d|90d|all` retorna events + aggregations + period + window dates.
- **Tests**: +10 nuevos (2 por rule).

### 2b.2 Frontend (1 commit `ce04449`)
- 8 files, +867.
- `patterns.service.ts` + `patterns.state.ts` + `patterns-page.ts` con period selector + events cards (severity-based color) + aggregation bars (3 buckets low/mid/high emotionality).
- Bottom-nav entry `'Patrones'` agregada (5 items total).
- 3 jest specs.

---

## 3. Slice 2c — MFE/MAE Charts (~1378 net lines)

### 2c.1 Backend (2 commits)
- **Commits**: `9b4f313` domain calculator + trade integration + 12 tests, `22b093b` infrastructure migration 0014 + EF + endpoint + histograms + 4 aggregator tests.
- **Migration `0014_trades_mfe_mae.sql`**: idempotent. Adds `mfe_amount/mae_amount NUMERIC(24,8)` + `mfe_currency/mae_currency CHAR(3)` nullable columns.
- **Decision (option B)**: `Trade.Close()` invoca `MfeMaeCalculator.Compute(this)` y `ApplyMfeMae(...)` ANTES de retornar — atómico por construcción. **NO usar `INotificationHandler<TradeClosedDomainEvent>`** porque el codebase no tiene dispatcher de domain events (auditado en attempt previo). `AggregateRoot._domainEvents` se acumulan pero NUNCA se publican.
- **Domain**: `MfeMaeCalculator.Compute(Trade)` pure function. `Trade.ApplyMfeMae(mfe, mae, currency)` con validation (mfe ≥ 0, mae ≤ 0).
- **Aggregate bug surfaced by E2E smoke**: formula de bucket aggregator atribuia winner a `mfeLongLosers`. Fixed con triangulation tests (4 tests).
- **Tests**: 12 calculator + 2 trade + 4 aggregator.

### 2c.2 Frontend (1 commit `394df65`)
- 5 files, +315.
- `mfe-mae-mini-chart.ts` component reusable (2 inline bars con colors).
- `trades-list.page.ts` extendido con 2 columns inline (MFE/MAE).
- 2 jest specs.

---

## 4. Slice 2d — Coaching Prompts (~2064 net lines)

### 2d.1 Backend (1 commit `0392f24`)
- 19 files, +1515.
- **Shared.Kernel**: `ICoachingRule`, `Severity`, `CoachingPrompt`, `Cta`, `CoachingContext` records.
- **Trading.Application**: `CoachingRuleRegistry` + 5 reglas (3 son adapters 1:1 sobre `BehavioralAnalyzer` events de 2b; 2 son standalone: `LongBreakRule`, `PreMarketPlanMissRule`).
- **PII rule**: ningún prompt copy incluye amounts absolutos del trade. Cualitativo only.
- **API**: `GET /api/coaching/prompts?period=...` retorna prompts ordenados severity desc + occurredAt desc.
- **Tests**: 13 nuevos.

### 2d.2 Frontend (1 commit `91efd5d`)
- 6 files, +550.
- `coaching-prompts.component.ts` reusable con severity color + CTAs.
- `dashboard.page.ts` embed `<jcs-coaching-prompts [prompts]>` arriba del primer KPI card.
- 3 jest specs.

---

## 5. Slice 2e — E2E Wiring + Smoke (2 commits)

- **`1904c81` `chore(wave-2): e2e wiring verification + smoke`**: 1 deletion (removed `'Calendario'` nav item para cumplir design.md 5 items max). Calendar route queda accesible via deep-link.
- **`188ab0b` `docs(sdd): mark Wave 2 tasks complete`**: 52 tasks marcadas `[x]`, 52 unchecked deferrals documentadas.

### Smoke E2E completo (12/12 curls)

| # | Endpoint | HTTP | Result |
|---|---|---|---|
| 1 | POST /api/auth/register | 201 | userId captured |
| 2 | POST /api/auth/login | 200 | accessToken (492 chars) |
| 3 | POST /api/journal/today | 200 | moodPre:4, premarketPlan ok |
| 4 | GET /api/journal/today | 200 | same DTO |
| 5 | GET /api/journal?from=&to= | 200 | array with 1 entry |
| 6 | POST /api/trades (Long EUR/USD) | 201 | tradeId 5f436364 |
| 7 | PUT /api/trades/{id}/close | 200 | pnl=5.0 |
| 8 | GET /api/trades/{id}/mfe-mae | 200 | mfeAmount:5.0, maeAmount:0 |
| 9 | GET /api/trades/behavioral?period=30d | 200 | events:[] (1 trade, no triggers) |
| 10 | GET /api/coaching/prompts?period=30d | 200 | prompts:[] (no triggers) |
| 11 | GET /api/journal?from=&to= (>7d range) | 200 | 1 entry |
| 12 | DELETE /api/journal/{id} | 204 | no content |

### URLs iPhone verificadas (HTTP 200 en cada una)

- `/`, `/auth/login`, `/app/dashboard`, `/app/trades`, `/app/journal`, `/app/patterns`
- `/health/ready`, `/api/billing/plans` (proxy via frontend)

---

## Cross-cutting Wave 2 outcomes

- **Build**: 0 errors, 0 warnings nuevos.
- **Backend unit tests**: 616 passing (76 Shared.Kernel + 22 Billing + 163 Identity + 355 Trading).
- **Frontend jest**: 103 passing (37 Wave 0 baseline + 49 Wave 1 + 17 Wave 2).
- **Migrations applied live**: 0013 `journal_entries`, 0014 `trades.mfe_amount/mae_amount`. Idempotency verified.
- **Stack docker**: API + frontend healthy. LAN + Tailscale accessible.
- **Mobile responsive**: 5 nav items en bottom-nav (< 768px). Cada entry abre su página con layout mobile-first.
- **Cross-module pattern**: 2b BehavioralAnalyzer reutilizado por 2d CoachingRuleRegistry (3 reglas son adapters 1:1).
- **Honest deferrals**: `Program.cs:122` AddAssemblyValidators solo Identity (Wave 1 caveat). Behavioral events persistence for trending deferred to Wave 3. LLM coaching deferred to Wave 5. Real market data provider for accurate MFE/MAE deferred to Wave 4.

## Specs Wave 2

- `journal-daily`: 5 requirements, 7 scenarios
- `behavioral-analytics`: 5 requirements, 8 scenarios
- `mfe-mae-charts`: 4 requirements, 7 scenarios
- `coaching-prompts`: 7 requirements, 6 scenarios

**Total: 21 requirements, 28 scenarios**

## Lessons

1. **El codebase NO tiene dispatcher de domain events** (`AggregateRoot._domainEvents` write-only). Cualquier spec que asuma `INotificationHandler<T>` debe auditar primero si hay dispatcher. Para Wave 2c, solución: computar MFE/MAE dentro de `Trade.Close()` factory (atómico por construcción, sin race condition).
2. **Hotfix de auditoría 2b descubrió bug pre-existente**: `PreTradeChecklistConfiguration` mapeaba `CreatedAt/UpdatedAt` que NO existían en migration 0011. Durmió porque 1c.2 solo escribía (nunca leía). Lección: cualquier EF Configuration que agregue `HasColumnName("X")` debe verificar que la migration creó la columna. Hay que auditar otros entities Wave 1.
3. **Smoke E2E surfaced aggregator bug**: tests unit solos no hubieran agarrado el bug de bucket assignment (winner asignado a `mfeLongLosers`). Sin el smoke real con Bearer, el bug se hubiera quedado. Triangulación tests (4 invariants) lo cerraron.
4. **JSON enum serialization**: `Program.cs` NO tiene `JsonStringEnumConverter`. Smoke curls originales con `"direction":"Long"` retornaron 500. Fix: usar numéricos (`direction: 1`).
5. **5 nav items max en mobile**: Wave 1.5 design asumía 5 pero trader-shell tenía 6 (incluyendo Calendario). Wave 2e dropeó Calendario del nav para cumplir design.md. Calendar route queda accesible via deep-link.
6. **PII rule explícito en coaching**: ningún prompt copy incluye amounts absolutos. Lenguaje cualitativo only. Cumplido via review manual del copy en código + test que verifica regex `[\d$%]` en body.

## Next steps para próximas sesiones

1. Fix `JadeApiFactory.ApplyMigrationAsync` migration sort order (Wave 1 follow-up).
2. Add `MinioContainer` to `JadeApiFactory` (Wave 1 follow-up).
3. Extend `Program.cs:122` AddAssemblyValidators (Wave 1 follow-up).
4. Wave 3: Strategies + Alerts + Planner (per roadmap original).
5. Behavioral events persistence for trending (deferred from Wave 2).
6. LLM-based coaching (Wave 5, Ollama).
7. Real market data provider for accurate MFE/MAE (Wave 4).
