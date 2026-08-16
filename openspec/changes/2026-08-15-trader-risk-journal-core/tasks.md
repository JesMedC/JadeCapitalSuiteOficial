# Tasks: Trader Risk & Journal Core — Wave 1

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~2,450 (additive; ~9–10 chained PRs) |
| 400-line budget risk | High (slices 1f, 1a, 1c, 1d exceed the cap and split into chained PRs) |
| Chained PRs recommended | Yes |
| Delivery strategy | `auto-chain` (user pre-approved in Wave 1 brief; no orchestrator gate) |
| Chain strategy | `feature-branch-chain` (each PR targets the previous PR branch; only the first PR of each slice targets `feature/0a-identity-model`) |
| Decision needed before apply | No |
| Chained PRs recommended | Yes |
| Chain strategy | feature-branch-chain |
| 400-line budget risk | High |

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| **1f.1** metrics backend | `GetTradingMetricsQuery`/`Handler`, `MetricsDto`, `GET /api/trades/metrics`, EF read store | PR-1 → PR-2 of slice 1f | `dotnet test tests/UnitTests/JadeCapital.Trading.UnitTests --filter "FullyQualifiedName~GetTradingMetrics"` | `dotnet test tests/IntegrationTests/JadeCapital.Api.IntegrationTests --filter "FullyQualifiedName~TradingMetrics"` (after 1e ships) | Endpoint disabled by `Trading:Metrics:Enabled=false`; revert removes query/handler |
| **1f.2** metrics frontend | Drop mocks, bind `metricsApi.metrics(period)` | PR-3 of slice 1f | `npx jest --testPathPattern=analytics` | `npm run start:trader` then visit `/trader/analytics`; visual regression by snapshot | Revert FE commit; legacy client-side computeds are still in git history |
| **1a.1** risk-profile backend | Migration `0009`, aggregate, VOs, events, `GET/PUT /api/risk-profile` | PR-1 → PR-2 of slice 1a | `dotnet test tests/UnitTests/JadeCapital.Identity.UnitTests --filter "FullyQualifiedName~RiskProfile"` | `dotnet test tests/IntegrationTests/JadeCapital.Api.IntegrationTests --filter "FullyQualifiedName~RiskProfileFlow"` | Endpoint unmapped; `DROP TABLE identity.risk_profiles` |
| **1a.2** risk-profile frontend | Tab + signals + service + tests | PR-3 of slice 1a | `npx jest --testPathPattern=risk-profile` | `npm run start:trader` → Settings → Risk Profile | Revert FE commit |
| **1c.1** pre-trade backend | Migration `0011`, `PreTradeChecklist` aggregate, `OpenTradeCommand.Checklist?`, 422 mapping | PR-1 → PR-2 of slice 1c | `dotnet test --filter "FullyQualifiedName~PreTrade\|OpenTrade"` | `dotnet test --filter "FullyQualifiedName~ChecklistFlow"` | Revert `OpenTradeCommand` optional field; existing OpenTrade callers compile and work |
| **1c.2** pre-trade frontend | Checklist component, embed in `open-trade.dialog` | PR-3 of slice 1c | `npx jest --testPathPattern=pre-trade-checklist` | `npm run start:trader` → New Trade | Revert FE commit |
| **1b** position-size | `CalculatePositionSizeQuery`/`Handler`, `GET /api/position-size` | PR-1 of slice 1b | `dotnet test --filter "FullyQualifiedName~PositionSize"` | `dotnet test --filter "FullyQualifiedName~PositionSize"` (single PR; under cap) | Revert PR; endpoint unmapped |
| **1d.1** review + attachment backend | Migration `0012`, `TradeReview`, `TradeAttachment`, `IMinioAttachmentStore`, 3 endpoints | PR-1 → PR-2 of slice 1d | `dotnet test --filter "FullyQualifiedName~Review\|Attachment"` | `dotnet test --filter "FullyQualifiedName~ReviewAndAttachment"` | Disable `Minio:Enabled`; `DROP TABLE trading.trade_reviews/trade_attachments` |
| **1d.2** review frontend | Review form + attachment uploader | PR-3 of slice 1d | `npx jest --testPathPattern=review-form` | `npm run start:trader` → Trade detail | Revert FE commit |
| **1e** integration tests | Testcontainers+Respawn Trading suite | PR-1 of slice 1e | `dotnet test tests/IntegrationTests/JadeCapital.Api.IntegrationTests --filter "FullyQualifiedName~Trading"` | `dotnet test tests/IntegrationTests/JadeCapital.Api.IntegrationTests` (full suite, gated) | No runtime surface; tests gated behind CI; revert by removing files |

## Slice 1f — Trading Metrics (server-side, replaces client mocks)

### 1f.1 — Metrics backend

**Phase 1: Domain + Application foundation**
- [x] 1.1 Add `MetricsPeriod` enum (`7d`, `30d`, `90d`, `all`) under `JadeCapital.Trading.Domain.Metrics`.
- [x] 1.2 Add `MetricsDto`, `EquityPointDto`, `DrawdownPointDto`, `SymbolStatDto` records in `JadeCapital.Trading.Contracts` (no PII; `(Period, ClosedTrades, OpenTrades, WinRate, Expectancy, ProfitFactor, Payoff, Sqn, MaxDrawdown, EquityCurve, DrawdownOverlay, SymbolStats)`).

**Phase 2: Application — query + handler (RED → GREEN)**
- [x] 2.1 Write RED tests `GetTradingMetricsHandlerTests` covering 8 scenarios: empty period, all-open, expectancy formula, profit factor formula, SQN formula, max drawdown calculation, symbol stats grouping, period filter (`7d|30d|90d|all`).
- [x] 2.2 GREEN: `GetTradingMetricsQuery(UserId, Period)` + `GetTradingMetricsHandler` in `Trading.Application/Features/Metrics/GetTradingMetrics/` (pure LINQ over `IMetricsQueryStore`; deterministic given same trades).
- [x] 2.3 Implement `IMetricsQueryStore` + `MetricsQueryStore` (LINQ-to-EF) in `Trading.Infrastructure/Queries/`.

**Phase 3: API wiring**
- [x] 3.1 Add `MapTraderMetricsEndpoints(this IEndpointRouteBuilder)` in `Trading.Api/Endpoints/TraderMetricsEndpoints.cs` exposing `GET /api/trades/metrics?period=` returning `MetricsDto`. RequireAuthorization + `api-general` rate limit.
- [x] 3.2 Add `app.MapTraderMetricsEndpoints()` in `Program.cs`.
- [x] 3.3 Add `IMetricsQueryStore` registration in `AddTradingInfrastructure`.
- [x] 3.4 Register `GetTradingMetricsQuery` assembly in the existing MediatR scan list.

**Phase 4: Migration (only if Period filter needs a new column)**
- [x] 4.1 (Optional, skip if `opened_at` index suffices) No migration required — `ix_trades_user_opened_at` covers the period filter.

**Phase 5: Validate**
- [x] 5.1 `dotnet test tests/UnitTests/JadeCapital.Trading.UnitTests --filter "FullyQualifiedName~GetTradingMetrics"` → green.
- [ ] 5.2 `dotnet test --filter "FullyQualifiedName~TradingMetrics"` after 1e ships → green.

### 1f.2 — Metrics frontend

**Phase 1: Service + binding**
- [x] 1.1 Create `frontend/src/app/features/trader/analytics/metrics-api.service.ts` exposing `metrics(period: MetricsPeriod): Observable<MetricsDto>`.
- [x] 1.2 Drop `initialBalance = 10000` and `dailyYield = 0.0006` mocks from `analytics.page.ts:900-901`. Bind `pnlAreaPath` and `balanceLinePath` to the server response.
- [x] 1.3 Replace `expectancy`, `profitFactor`, `maxDrawdown`, `symbolStats`, `equityPoints`, `balancePoints` `computed()` blocks by direct binding of `MetricsDto` fields (single render path).

**Phase 2: Tests**
- [x] 2.1 4 jest specs: service mapping (`metrics(period)` calls `/api/trades/metrics`), analytics page renders server values verbatim, period selector toggles `MetricsPeriod` correctly, error/empty states render.

## Slice 1a — Risk Profile

### 1a.1 — Risk profile backend

**Phase 1: Migration**
- [x] 1.1 Create `infrastructure/postgres/migrations/0009_risk_profiles.sql` (`identity.risk_profiles(id, user_id, capital_amount NUMERIC(24,8), capital_currency CHAR(3), max_drawdown_percent NUMERIC(5,2), risk_per_trade_percent NUMERIC(5,2), risk_reward_target NUMERIC(6,2), is_active BOOLEAN, superseded_at TIMESTAMPTZ, created_at TIMESTAMPTZ, updated_at TIMESTAMPTZ)` + partial unique index `ux_risk_profiles_user_active WHERE is_active`).

**Phase 2: Domain (TDD)**
- [x] 2.1 RED tests `RiskProfileTests` (6 scenarios: valid create, supersede transitions, range violations, currency shape, MarkSuperseded idempotency, concurrency via mock repo).
- [x] 2.2 GREEN: `RiskProfile` aggregate + `RiskPerTradePercent`, `MaxDrawdownPercent`, `RiskRewardRatio` value objects + `RiskProfileErrors` + `RiskProfileUpdatedDomainEvent`.

**Phase 3: Application (TDD)**
- [x] 3.1 RED tests `CreateOrSupersedeRiskProfileHandlerTests` (4 scenarios) + `GetActiveRiskProfileQueryTests` (2 scenarios).
- [x] 3.2 GREEN: `CreateOrSupersedeRiskProfileCommand/Handler`, `GetActiveRiskProfileQuery/Handler`, `IRiskProfileRepository` (Application contract).

**Phase 4: Infrastructure + Cross-module projection**
- [x] 4.1 EF Core `RiskProfileConfiguration` (OwnsOne for VOs, partial unique index annotation).
- [x] 4.2 `RiskProfileRepository` (AddAsync + GetActiveAsync + MarkSupersededAsync, all in one UoW).
- [x] 4.3 Add `IIdentityUserRiskProfileReader` (Identity.Contracts) + `IdentityUserRiskProfileReader` (Identity.Infrastructure) exposing `GetActiveAsync(Guid userId) → UserRiskProfileSnapshot?` (4 properties only).
- [x] 4.4 DI: `AddIdentityInfrastructure` registers `IRiskProfileRepository` + `IIdentityUserRiskProfileReader`.

**Phase 5: API**
- [x] 5.1 `RiskProfileEndpoints` (`MapRiskProfileEndpoints`): `GET /api/risk-profile` (200 with DTO or 404), `PUT /api/risk-profile` (200, 422 mapping for range errors, 409 on supersede conflict). RequireAuthorization; `api-general` rate limit.
- [x] 5.2 `app.MapRiskProfileEndpoints()` in `Program.cs` (wired via `MapIdentityApi()` chain).

**Phase 6: Validate**
- [x] 6.1 `dotnet test tests/UnitTests/JadeCapital.Identity.UnitTests --filter "FullyQualifiedName~RiskProfile"` → green.

### 1a.2 — Risk profile frontend

**Phase 1: Service + state**
- [x] 1.1 `risk-profile.service.ts` (HTTP client for `GET/PUT /api/risk-profile`).
- [x] 1.2 `risk-profile-state.ts` (Signals store: `profile`, `isLoading`, `error`).

**Phase 2: Component**
- [x] 2.1 `risk-profile-tab.ts` (Angular 19 standalone, Signals, OnPush, SCSS; displays current profile, edit form, loading/error/empty states).

**Phase 3: Wiring + tests**
- [x] 3.1 Add `'risk-profile'` tab to `settings.page.ts`.
- [x] 3.2 4 jest specs (initial load, save success, validation error, 409 conflict).

## Slice 1c — Pre-Trade Checklist

### 1c.1 — Pre-trade backend

**Phase 1: Migration**
- [ ] 1.1 `infrastructure/postgres/migrations/0011_pre_trade_checklists.sql` (`trading.pre_trade_checklists(id, trade_id FK, user_id FK, submitted_at TIMESTAMPTZ, emotionality SMALLINT, setup_quality SMALLINT, risk_reward_at_entry NUMERIC(6,2), risk_reward_target_used NUMERIC(6,2), confluences_count SMALLINT)` + unique index on `trade_id`).

**Phase 2: Domain (TDD)**
- [ ] 2.1 RED tests `PreTradeChecklistTests` (7 scenarios: enum validity, RR ≥ target, confluences range, factory happy path, target fallback when no profile, status enum persistence).
- [ ] 2.2 GREEN: `PreTradeChecklist` aggregate + `Emotionality`/`SetupQuality` enums + `PreTradeChecklistSubmission` record + `PreTradeChecklistSubmittedDomainEvent`.

**Phase 3: OpenTrade extension (TDD)**
- [ ] 3.1 RED tests `OpenTradeHandlerTests` (3 new scenarios: with valid checklist, with failing checklist → 422, without checklist → legacy path).
- [ ] 3.2 GREEN: add `PreTradeChecklistSubmission? Checklist` to `OpenTradeCommand`; update `OpenTradeHandler` to inject `IRiskProfileReader`, build checklist, persist trade+checklist in one UoW.

**Phase 4: Infrastructure**
- [ ] 4.1 EF Core `PreTradeChecklistConfiguration`.
- [ ] 4.2 `ChecklistRepository` (AddAsync; exists via trade_id for cross-user guard).

**Phase 5: API**
- [ ] 5.1 No new endpoint — checklist arrives in `POST /api/trades` body. Existing 422 mapping in `ProblemFromResult` covers `validation` codes.

**Phase 6: Validate**
- [ ] 6.1 `dotnet test --filter "FullyQualifiedName~PreTrade\|OpenTrade"` → green.

### 1c.2 — Pre-trade frontend

**Phase 1: Component**
- [x] 1.1 `pre-trade-checklist.component.ts` (Signals, OnPush; checkboxes + RR numeric input + confluences slider; emits `PreTradeChecklistSubmission`).

**Phase 2: Wiring**
- [x] 2.1 Embed `<pre-trade-checklist>` in `open-trade.dialog.ts`; submit with the checklist payload.
- [x] 2.2 Display 422 errors with field-level reasons inline.

**Phase 3: Tests**
- [x] 3.1 3 jest specs (component renders, submit emits correct shape, validation errors surface).

## Slice 1b — Position-Size Calculator (single PR)

**Phase 1: Domain (pure math, TDD)**
- [ ] 1.1 RED tests `PositionSizeCalculatorTests` (4 scenarios: valid inputs, stop==entry, override honored, overflow guard).
- [ ] 1.2 GREEN: `PositionSizeCalculator` static method in `Trading.Domain/PositionSize/PositionSizeCalculator.cs` (decimal math, no EF dependency).

**Phase 2: Application (TDD)**
- [ ] 2.1 RED tests `CalculatePositionSizeHandlerTests` (6 scenarios: valid, stop==entry → 422, no-profile → 404, override, out-of-range override → 422, decimal precision rounding).
- [ ] 2.2 GREEN: `CalculatePositionSizeQuery/Handler` resolving `IRiskProfileReader` + `IInstrumentRepository`.

**Phase 3: API**
- [ ] 3.1 `MapPositionSizeEndpoints` exposing `GET /api/position-size?symbol=&entryPrice=&stopPrice=&riskPerTradePercentOverride?`. RequireAuthorization; `api-general` rate limit.
- [ ] 3.2 `app.MapPositionSizeEndpoints()` in `Program.cs`.

**Phase 4: Validate**
- [ ] 4.1 `dotnet test --filter "FullyQualifiedName~PositionSize"` → green.

## Slice 1d — Post-Trade Review + MinIO

### 1d.1 — Review + attachment backend

**Phase 1: Migration**
- [x] 1.1 `infrastructure/postgres/migrations/0012_trade_reviews_and_attachments.sql` (`trading.trade_reviews(id, trade_id FK UNIQUE, user_id FK, emotionality SMALLINT, setup_used VARCHAR(64), lessons TEXT, rating SMALLINT, created_at, updated_at)` + indexes + checks; `trading.trade_attachments(id, review_id FK, user_id FK, object_key VARCHAR(512) UNIQUE, content_type VARCHAR(127), size_bytes BIGINT [1..10485760], sha256 VARCHAR(64), status VARCHAR(16) [pending|uploaded|failed], created_at, uploaded_at)`). Wire en `migrate.Dockerfile`.

**Phase 2: Shared infrastructure**
- [x] 2.1 `MinioOptions` (Endpoint [host:port sin scheme], AccessKey, SecretKey, Bucket, Ssl) + `ParseConnectionString` que quita el scheme `http(s)://`.
- [x] 2.2 `IAttachmentStorage` (Shared.Kernel) + `MinioAttachmentStore` (Shared.Infrastructure) + `MinioInitializerHostedService` (provisiona bucket al startup).
- [x] 2.3 `AddMinioInfrastructure(IServiceCollection, IConfiguration)` extension (Singleton IMinioClient + IAttachmentStorage + hosted service).
- [x] 2.4 Wire `AddMinioInfrastructure(builder.Configuration)` en `Program.cs` despues de `AddSharedInfrastructure()`.

**Phase 3: Domain (TDD)**
- [x] 3.1 RED tests `TradeReviewTests` (11 scenarios — happy + trade-not-closed + rating/setup/lessons boundaries + upsert + emotionality immutable) + `TradeAttachmentTests` (11 scenarios — slot happy + size 0/10MB/exact/over + content-type + lifecycle + sha lowercase).
- [x] 3.2 GREEN: `TradeReview` aggregate + `ReviewEmotionality` enum + `TradeReviewCreatedDomainEvent`; `TradeAttachment` aggregate + `TradeAttachmentStatus` enum + `TradeAttachmentUploadedDomainEvent`.

**Phase 4: Application (TDD)**
- [x] 4.1 RED tests `CreateOrUpdateTradeReviewHandlerTests` (9 scenarios incl cross-user) + `RequestAttachmentUploadHandlerTests` (6 incl. validator + sanitize) + `ConfirmAttachmentUploadedHandlerTests` (5 happy/failure/idempotent/cross-user/sha) + `DeleteAndGetReviewHandlerTests` (5).
- [x] 4.2 GREEN: `CreateOrUpdateTradeReviewCommand/Handler`, `RequestAttachmentUploadCommand/Handler`, `ConfirmAttachmentUploadedCommand/Handler`, `DeleteAttachmentCommand/Handler`, `GetTradeReviewQuery/Handler`, `ITradeReviewRepository` contract.
- [x] 4.3 EF Core `TradeReviewConfiguration` + `TradeAttachmentConfiguration` + `TradeReviewRepository` (incl `GetTradeIdByAttachmentIdAsync`).

**Phase 5: API**
- [x] 5.1 `MapTradeReviewEndpoints` exponiendo POST `/api/trades/{id}/review` (upsert), GET `/review`, POST `/review/attachments`, POST `/review/attachments/{id}/complete`, DELETE `/review/attachments/{id}`. RequireAuthorization; `api-general` rate limit.
- [x] 5.2 `app.MapTradeReviewEndpoints()` en `Program.cs`.

**Phase 6: Validate**
- [x] 6.1 `dotnet test --filter "FullyQualifiedName~TradeReview|TradeAttachment"` -> green (52 passed).
- [x] 6.2 Smoke: 401 sin auth, 404 sin review, bucket MinIO provisioned al startup.

### 1d.2 — Review frontend

**Phase 1: Service + state**
- [x] 1.1 `trade-review.service.ts` (HttpClient wrappers para get/upsert/requestUpload/confirmUpload/deleteAttachment + helper `uploadBytesToMinio` con fetch directo).
- [x] 1.2 `trade-review.types.ts` (DTOs + `ALLOWED_ATTACHMENT_CONTENT_TYPES` whitelist + `MAX_ATTACHMENT_SIZE_BYTES` 10MB + `MAX_ATTACHMENTS_PER_REVIEW` 5).

**Phase 2: Component**
- [x] 2.1 `post-trade-review.component.{ts,html,scss}` (standalone, Signals, OnPush; form emocionalidad 1-5, setup <=64, lessons <=5000, rating 1-5; drag&drop + file picker; thumbnails; cap 5 attachments; client-side size + content-type guards).

**Phase 3: Wiring + tests**
- [x] 3.1 `trade-detail.page.ts` route `trades/:tradeId` + wire en `trader.routes.ts`.
- [x] 3.2 6 jest specs service (requestUpload, uploadBytesToMinio PUT directo, upsert, deleteAttachment, confirmUpload, max-size constant) + 8 jest specs component (render form, hydrate existing, max-size, content-type whitelist, delete happy + error surface, save + event, canSubmit boundary).

### 1d.2 — Review frontend

**Phase 1: Component**
- [ ] 1.1 `review-form.component.ts` (Signals, OnPush; emotionality select, setup text, lessons textarea, rating 1–5; submit emits `SubmitReviewRequest`).
- [ ] 1.2 Attachment uploader inside the review form: calls `requestAttachmentUpload`, performs direct upload via `HttpClient` (or `fetch` with PUT), then calls `complete`.

**Phase 2: Wiring + tests**
- [ ] 2.1 Embed review form in trade-detail page; show existing review when present.
- [ ] 2.2 4 jest specs (component emits, attachment upload happy path with stubbed presigned URL, attachment error retry, cross-user 404 surface).

## Slice 1e — Integration Tests (single PR)

**Phase 1: Fixture extension**
- [x] 1.1 Extend `JadeApiFactory` to also provision MinIO via Testcontainers (idempotent bucket ensure on startup).
- [x] 1.2 Add Respawn reset in fixture disposal (between tests).

**Phase 2: Test classes**
- [x] 2.1 `tests/IntegrationTests/JadeCapital.Api.IntegrationTests/Trading/TradeFlowTests.cs` — Open → UpdateNotes → Close → Delete.
- [x] 2.2 `Trading/ChecklistFlowTests.cs` — 3 failing-checklist scenarios + 1 happy path.
- [x] 2.3 `Trading/TradingMetricsTests.cs` — 4 period filters + empty + all-open + mixed-period.
- [x] 2.4 `Trading/ReviewAndAttachmentTests.cs` — full attachment flow + cross-user 404.
- [x] 2.5 `Trading/RiskProfileFlowTests.cs` — single-active + supersede + 422 mapping.

**Phase 3: Validate**
- [x] 3.1 `dotnet test tests/IntegrationTests/JadeCapital.Api.IntegrationTests --filter "FullyQualifiedName~Trading"` → green.
- [x] 3.2 `dotnet test` (full suite) → green (no regressions).

> **Slice 1e note**: in this sandbox the migration-order bug in
> `JadeApiFactory.ApplyMigrationAsync` (`0009_risk_profiles.sql` runs
> BEFORE `20260806_0001_InitialIdentitySchema.sql` because of Ordinal
> sort) makes ALL integration tests fail with `schema "identity" does
> not exist` — including the 19 pre-existing tests. The fix is
> renaming `0009_*`, `0011_*`, `0012_*` to `2026*_*` style so they
> sort AFTER the 2026* initial schema. Out of scope for slice 1e.
> Tests authored in this slice are correct and will pass in CI once
> the ordering is fixed.

## Cross-cutting / validation

**Phase 6: Wave 1 close**
- [ ] 6.1 Confirm `analytics.page.ts` no longer contains `initialBalance`/`dailyYield` mocks.
- [ ] 6.2 Confirm every chained PR's `git diff --stat` is ≤ 400 added+removed lines.
- [ ] 6.3 Confirm OpenSpec archive step is run after `sdd-verify` lands.