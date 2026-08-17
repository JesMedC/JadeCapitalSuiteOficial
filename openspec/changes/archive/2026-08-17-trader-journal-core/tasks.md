# Tasks — Wave 2 (Trader Journal Core)

## Review Workload Forecast

| Slice | Boundary | Lines | Base |
|---|---|---:|---|
| 2a | Journal daily domain + application + migration 0013 + endpoints + UI + tests | ~700 | tracker |
| 2b | Behavioral analytics handler + endpoint + UI patterns page + tests | ~520 | 2a |
| 2c | MFE/MAE migration 0014 + calculator + endpoint + chart UI + tests | ~480 | 2b |
| 2d | ICoachingRule registry + 5 rules + endpoint + dashboard integration + tests | ~550 | 2c |
| 2e | E2E wiring: dashboard embeds coaching prompts + bottom-nav entry + smoke verification | ~300 | 2d |
| **Total** | 5 chained slices | **~2,550** | tracker→main |

Decision needed before apply: **No** (auto-chain, 400-line budget per PR). User confirmed `feature-branch-chain` (Wave 0/1 precedent). Per-slice `git diff --stat` < 400 if pre-split; otherwise chained PRs (1-2 per slice).

**Build**: `dotnet build JadeCapital.slnx --nologo --verbosity minimal`.
**SQL harness**: `psql -v ON_ERROR_STOP=1 -f migrations/<file>.sql`; idempotent re-run (same script twice → exit 0).
**Frontend**: `cd frontend && npm run build` and `cd frontend && npx jest`.

### Work Units (PR → test → runtime → rollback)

- 2a: `dotnet test tests/UnitTests/JadeCapital.Trading.UnitTests --filter "FullyQualifiedName~Journal"` + smoke via `curl /api/journal/today` Bearer (200/404/422). Rollback: revert code; keep 0013 migration applied (inert).
- 2b: `dotnet test --filter "FullyQualifiedName~Behavioral"` + smoke `curl /api/trades/behavioral?period=30d` Bearer. Rollback: revert code.
- 2c: `dotnet test --filter "FullyQualifiedName~MfeMae"` + smoke `curl /api/trades/{id}/mfe-mae` Bearer. Rollback: revert code; keep 0014 migration (nullable columns).
- 2d: `dotnet test --filter "FullyQualifiedName~Coaching"` + smoke `curl /api/coaching/prompts`. Rollback: revert code; rules isolated.
- 2e: ng build + npx jest + docker compose up -d --build frontend + smoke from iPhone Tailscale URL.

---

## Slice 2a — Journal Daily (≤700 líneas, split 2a.1 + 2a.2)

### 2a.1 Backend (~450 líneas)

**Phase 1: Migration**
- [x] 1.1 `infrastructure/postgres/migrations/0013_journal_entries.sql` (idempotent, additive):
  ```sql
  CREATE TABLE IF NOT EXISTS trading.journal_entries (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
    local_date DATE NOT NULL,
    timezone VARCHAR(64) NOT NULL DEFAULT 'UTC',
    mood_pre SMALLINT NULL CHECK (mood_pre BETWEEN 1 AND 5),
    mood_during SMALLINT NULL CHECK (mood_during BETWEEN 1 AND 5),
    mood_post SMALLINT NULL CHECK (mood_post BETWEEN 1 AND 5),
    premarket_plan TEXT NULL CHECK (LENGTH(premarket_plan) <= 2000),
    postmarket_reflection TEXT NULL CHECK (LENGTH(postmarket_reflection) <= 5000),
    tags TEXT[] NULL CHECK (array_length(tags, 1) <= 10),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
  );
  CREATE UNIQUE INDEX IF NOT EXISTS ux_journal_user_date ON trading.journal_entries(user_id, local_date);
  CREATE INDEX IF NOT EXISTS ix_journal_user_created ON trading.journal_entries(user_id, created_at DESC);
  ```
- [x] 1.2 Wire en `migrate.Dockerfile` (siguiendo el patrón Wave 1 fix: `\"` escapes).

**Phase 2: Domain (TDD)**
- [x] 2.1 RED tests `JournalEntryTests` (8 scenarios del spec.md): create valid, single-active per user+date, mood range validation, plan length cap, reflection length cap, tags cap, update idempotency, cross-user guard.
- [x] 2.2 GREEN: `JournalEntry` aggregate + `Mood` VO + `JournalEntryCreatedDomainEvent` + `JournalEntryUpdatedDomainEvent` + `JournalEntryErrors.cs`.

**Phase 3: Application (TDD)**
- [x] 3.1 RED tests `CreateOrUpdateJournalEntryHandlerTests` (4 scenarios) + `GetTodayJournalEntryQueryTests` (2) + `GetJournalEntriesByRangeQueryTests` (2) + `DeleteJournalEntryHandlerTests` (1).
- [x] 3.2 GREEN: commands + queries + handlers + `IJournalEntryRepository` contract.

**Phase 4: Infrastructure + API**
- [x] 4.1 `JournalEntryConfiguration` (EF) con `OwnsOne` para `Mood` VO y array mapping para `tags` (Npgsql native TEXT[]).
- [x] 4.2 `JournalEntryRepository` impl.
- [x] 4.3 `IUserTimezoneAccessor` interface + `HttpHeaderTimezoneAccessor` impl (lee `X-User-Timezone` con fallback UTC).
- [x] 4.4 `JournalEndpoints` (`MapJournalEndpoints`): 4 endpoints. RequireAuthorization. `api-general` rate limit.
- [x] 4.5 `app.MapJournalEndpoints()` en `Program.cs`.

**Phase 5: Validate**
- [x] 5.1 `dotnet test --filter "FullyQualifiedName~Journal" --nologo --verbosity minimal` → green.
- [x] 5.2 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.

### 2a.2 Frontend (~250 líneas)

**Phase 1: Service + state**
- [x] 1.1 `journal.service.ts` con 4 métodos HTTP.
- [x] 1.2 `journal.state.ts` (Signals).
- [x] 1.3 2 jest specs (state). — *consolidated into the 4 page specs per the slice's reduced scope.*

**Phase 2: Page + routing**
- [x] 2.1 `journal-page.ts` standalone Signals OnPush SCSS con form: mood_pre/during/post (5 buttons cada uno), premarket_plan textarea (max 2000), postmarket_reflection textarea (max 5000), tags input (max 10).
- [x] 2.2 Add `'journal'` route a `trader.routes.ts` + `Journal` entry al `navItems` del `trader-shell.ts`.
- [x] 2.3 4 jest specs (renders empty, save success, validation errors, cross-user 404).

---

## Slice 2b — Behavioral Analytics (≤520 líneas, split 2b.1 + 2b.2)

### 2b.1 Backend (~360 líneas)

**Phase 1: Domain**
- [x] 1.1 RED tests `BehavioralEventTests` (5 scenarios: revenge, overtrading, tilt, overconfidence, aggregation).
- [x] 1.2 GREEN: `BehavioralEvent` record + `Severity` enum + `EmotionalityBucket` record + `BehavioralAnalyzer` (pure function, no persistence).

**Phase 2: Application + API**
- [x] 2.1 `GetBehavioralAnalyticsHandler` — loads trades via `ITradingRepository`, calls `BehavioralAnalyzer.Analyze`, projects to DTO.
- [x] 2.2 `BehavioralEndpoints` (`MapBehavioralEndpoints`): `GET /api/trades/behavioral?period=...`.
- [x] 2.3 Wire en Host.
- [x] 2.4 10 unit tests del analyzer (cada rule + edge cases).

### 2b.2 Frontend (~160 líneas)

- [x] 1.1 `patterns-page.ts` standalone con lista de eventos + tabla de aggregations.
- [x] 1.2 `patterns.routes.ts` + nav entry.
- [x] 1.3 3 jest specs.

---

## Slice 2c — MFE/MAE Charts (≤480 líneas, split 2c.1 + 2c.2)

### 2c.1 Backend (~340 líneas)

**Phase 1: Migration**
- [x] 1.1 `infrastructure/postgres/migrations/0014_trades_mfe_mae.sql`:
  ```sql
  ALTER TABLE trading.trades
    ADD COLUMN IF NOT EXISTS mfe_amount NUMERIC(24,8) NULL,
    ADD COLUMN IF NOT EXISTS mae_amount NUMERIC(24,8) NULL,
    ADD COLUMN IF NOT EXISTS mfe_currency CHAR(3) NULL,
    ADD COLUMN IF NOT EXISTS mae_currency CHAR(3) NULL;
  ```
- [x] 1.2 Wire en `migrate.Dockerfile`.

**Phase 2: Calculator + persistence**
- [x] 2.1 `MfeMaeCalculator` (Trading.Domain) — pure function implementing the approximation algorithm del spec.md. 8 unit tests.
- [x] 2.2 Extend `TradeConfiguration` EF: nuevos 4 columns nullable.
- [x] 2.3 Hook on `TradeClosedDomainEvent` → recompute MFE/MAE in same UoW. Implementation: `AppendMfeMaeOnTradeCloseHandler` en Trading.Application (MediatR notification handler for `INotificationHandler<TradeClosedDomainEvent>`).

**Phase 3: API**
- [x] 3.1 `TradeMfeMaeEndpoints`: `GET /api/trades/{id}/mfe-mae` con aggregate histograms.
- [x] 3.2 Wire en Host.

### 2c.2 Frontend (~140 líneas)

- [x] 1.1 Inline mini-chart en `trades-list.page.ts` (cada row muestra `mfe_amount` / `mae_amount` en 2 columnas).
- [x] 1.2 Chart component compartido en `features/trader/trades/mfe-mae-mini-chart.ts` (sparkline inline).
- [x] 1.3 2 jest specs.

---

## Slice 2d — Coaching Prompts Registry (≤550 líneas, split 2d.1 + 2d.2)

### 2d.1 Backend (~380 líneas)

**Phase 1: Shared abstraction**
- [x] 1.1 `ICoachingRule` interface + `CoachingPrompt` record + `CoachingContext` record + `Severity` enum en `Shared.Kernel/Coaching/`.
- [x] 1.2 `CoachingRuleRegistry` (en Shared.Infrastructure) que itera `IEnumerable<ICoachingRule>` y agrega.

**Phase 2: 5 rules**
- [x] 2.1 `RevengeTradeRule`, `OvertradingDayRule`, `TiltSequenceRule`, `LongBreakRule`, `PreMarketPlanMissRule` en `Trading.Application/Coaching/Rules/`.
- [x] 2.2 8 unit tests por rule (1.5 promedio × 5 rules ≈ 8).

**Phase 3: API**
- [x] 3.1 `CoachingEndpoints`: `GET /api/coaching/prompts?period=...`.
- [x] 3.2 Wire en Host.

### 2d.2 Frontend (~170 líneas)

- [x] 1.1 `coaching-prompts.component.ts` standalone (re-usable: dashboard + journal).
- [x] 1.2 Severity-based styling (high = red, medium = yellow, low = blue).
- [x] 1.3 3 jest specs (renders prompts, severity order, CTA click navigates).

---

## Slice 2e — E2E Wiring + Smoke (~300 líneas, single PR)

- [x] 1.1 Add `<jcs-coaching-prompts>` al top de `dashboard.page.ts`.
- [x] 1.2 Update `trader-shell.ts` `navItems` to 5 items (Dashboard / Trades / Journal / Patterns / Settings).
- [x] 1.3 Verify `jcs-mobile-nav` shows 5 items on mobile.
- [x] 1.4 `docker compose up -d --build api frontend` y smoke desde Tailscale.
- [x] 1.5 `cd frontend && npx jest` + `dotnet test` finales.

---

## Cross-cutting / Validation

- [x] 6.1 `dotnet build JadeCapital.slnx --nologo --verbosity minimal` → 0 errors, 0 warnings nuevos.
- [x] 6.2 `cd frontend && npx jest --no-coverage` → todos verdes.
- [x] 6.3 `dotnet test JadeCapital.slnx --filter "FullyQualifiedName!~JadeCapital.Api.IntegrationTests" --no-restore --nologo --verbosity minimal` → todos verdes.
- [x] 6.4 `docker compose up -d --build api frontend` → healthy.
- [x] 6.5 Confirmar per-slice `git diff --stat` ≤ 400 (o chained PRs justificados).
- [x] 6.6 mem_save final con specs + lessons + next steps.

## Open / deferred to later waves

- Behavioral events persistence for trending (Wave 3).
- Email / push notification triggers for coaching prompts (Wave 3).
- Real market data provider for accurate MFE/MAE (Wave 4).
- LLM-based coaching personalization (Wave 5).
- Soft-delete on journal entries (Fase 6).
- Multi-day trends and monthly summaries in patterns page (Wave 3).
