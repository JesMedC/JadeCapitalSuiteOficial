# Proposal: Trader Risk & Journal Core — Wave 1

## Intent and Problem

Wave 0 closed Identity, Billing and Admin seams. The Trader portal still ships a working Trade lifecycle but lacks every analytics, risk-control, and journal feature that turns raw trades into decisions the user can act on:

- Analytics mocks a synthetic `initialBalance = 10000` and `dailyYield = 0.0006` in the FE (`frontend/.../analytics.page.ts:900-901`) instead of computing expectancy, profit factor, drawdown and per-symbol stats server-side from the user's actual trades.
- There is no concept of a per-user **risk profile** (capital, max drawdown, risk-per-trade, risk-reward target). Position sizing is therefore guesswork and cannot be enforced or reviewed.
- `OpenTrade` accepts any volume with no pre-trade gate (no checklist, no RR check, no exposure sanity). The user can take oversized risk before they have a chance to slow down.
- `RiskPerTrade` is not first-class — calculators cannot compute `volume = risk_amount / distance_to_stop` because StopLoss is not in the schema.
- There is no **post-trade review**: no structured notes, no emotionality/setup/lessons fields, no screenshot or trade-log attachment. Journaling is ad-hoc in the `notes` blob.
- MinIO is provisioned in `docker-compose.yml` but the .NET Host never wires a `MinioClient`. There is no upload path and no object-key persistence.
- There are no Trading integration tests covering Open/Close/Delete/checklist/review/metrics flows end-to-end (Sprint 1F follow-up).

Wave 1 closes every one of those gaps without rewriting the Trade aggregate lifecycle, without touching Stripe, Public pricing, Auth or Admin, and without changing the .NET 10 modular-monolith / Clean Architecture dependency direction established in Wave 0.

## Goals

- **1f**: Trader analytics compute expectancy, profit factor, payoff, SQN, max drawdown, equity curve, drawdown overlay and per-symbol stats server-side. The dashboard summary stops shipping synthetic balance values. Frontend renders server metrics verbatim.
- **1a**: A single-active **risk profile** per user stores capital, max drawdown %, risk-per-trade %, and risk-reward target. CRUD via `GET/PUT /api/risk-profile`. No role/impersonation mechanics.
- **1c**: A **pre-trade checklist** gates `OpenTrade` when submitted. Failed validation produces a 422 with field-level reasons and never persists the trade.
- **1b**: A **position-size calculator** derives `volume = risk_amount / distance_to_stop` in quote currency using the user's active risk profile. The result is returned alongside the checklist (informational; not enforced — `RiskPerTrade` override remains per-trade).
- **1d**: A **post-trade review** captures emotionality, setup tags, lessons, and structured **attachments** stored in MinIO via presigned URLs (client uploads directly; backend persists only the object key + metadata). The review is required for closed trades but never blocks `CloseTrade`.
- **1e**: End-to-end **integration tests** (Testcontainers + Respawn) for the Trader surface close the Sprint 1F follow-up and prevent regressions in Open/Close/Checklist/Review/Metrics.

## Scope Boundaries

**Changed:** Identity.Domain adds `RiskProfile` (schema `identity`). Trading.Domain adds `PreTradeChecklist`, `TradeReview`, `TradeAttachment`. Trading.Application adds `GetTradingMetricsQuery`, `CalculatePositionSizeQuery`, `SubmitChecklistCommand`, `SubmitReviewCommand`, `RequestAttachmentUploadCommand`. Trading.Infrastructure adds new tables, EF configurations, and a query store for metrics. Trading.Api adds `GET /api/trades/metrics`, `POST /api/trades/{id}/checklist`, `POST /api/trades/{id}/review`, `POST /api/trades/{id}/review/attachments`, `POST /api/trades/{id}/review/attachments/{attId}/complete`. Identity.Api adds `GET/PUT /api/risk-profile`. Shared.Infrastructure adds `AddMinioInfrastructure(IServiceCollection)` and an `IMinioAttachmentStore` abstraction. Frontend adds a Risk Profile tab to settings, a Pre-Trade Checklist component on the Open-Trade dialog, a Review form on the Trade detail page, and replaces the client-side metrics mocks with `metricsApi.metrics(period)`.

**Unchanged:** Stripe, Public pricing UI (Wave 0 partial; that surfacing is out of Wave 1 scope), Auth/registration, Admin subscriptions, Wave 0 password-recovery semantics, Angular 19 strict TypeScript / Signals / OnPush / SCSS conventions, .NET 10 modular-monolith dependency direction, `Trade` lifecycle (Open/Close/Cancel/UpdateNotes/Delete), `trading.trades` row-level schema (additive migrations only; nullable columns).

## Capabilities

### New Capabilities

- `risk-profile`: User-owned single-active risk profile (capital, max drawdown %, risk-per-trade %, risk-reward target). Owned by Identity. CRUD via authenticated endpoints. No role/impersonation mechanics.
- `trading-metrics`: Server-side trading analytics: expectancy, profit factor, payoff ratio, SQN, max drawdown, equity curve points, drawdown overlay points, per-symbol stats. Period filter (`7d|30d|90d|all`).
- `pre-trade-checklist`: Pre-trade checklist (emotionality, setup quality, RR ≥ target, confluence count) attached to OpenTrade. Failed validation produces 422 with field-level reasons and never persists the trade.
- `position-size-calculator`: Position-size calculator deriving `volume = risk_amount / distance_to_stop` in quote currency using the active risk profile. `RiskPerTrade` override remains per-trade (not persisted in the risk profile itself).
- `post-trade-review`: Post-trade review capturing emotionality, setup, lessons, and attachments. Attachments uploaded to MinIO via presigned URL; backend persists object key, mime type, size and SHA-256.
- `trades-integration-tests`: Integration tests (Testcontainers + Respawn) for TradeFlow, RiskProfileFlow, TradingMetrics, ChecklistFlow, ReviewAndAttachment covering Open→Checklist→Close→Review→Metrics.

### Modified Capabilities

- None.

## Ownership and Approach

- **Identity** owns `RiskProfile` (schema `identity`). Risk profile is cross-module data, but the aggregate lives in the user's authoritative module.
- **Trading** owns `PreTradeChecklist`, `TradeReview`, `TradeAttachment` and the metrics calculator. Schema `trading`.
- **Shared.Infrastructure** owns the `MinioClient` registration (`AddMinioInfrastructure`), the `IMinioAttachmentStore` abstraction, and the bucket provisioning helper. `IMinioAttachmentStore` exposes `RequestUploadAsync(GeneratePresignedUploadUrlRequest)` and `EnsureBucketAsync`.
- Trading reads the active risk profile through a narrow projection (`IIdentityUserRiskProfileReader`) defined in `JadeCapital.Identity.Contracts.Projections` — same architectural pattern as `IUserOwnerProjection` from Wave 0. The interface exposes only what Trading needs: `CapitalAmount`, `CapitalCurrency`, `RiskPerTradePercent`, `RiskRewardTarget`.
- Host wiring impact:
  - `JadeCapital.Identity.Api.Endpoints.MapRiskProfileEndpoints()` (extension on `IEndpointRouteBuilder`).
  - `JadeCapital.Trading.Api.Endpoints.MapTraderMetricsEndpoints()` and `MapTraderReviewEndpoints()` (new endpoint files in `JadeCapital.Trading.Api/Endpoints/`).
  - `JadeCapital.Shared.Infrastructure.DependencyInjection.AddMinioInfrastructure(IServiceCollection, IConfiguration)` — called once in `Program.cs` after `AddSharedInfrastructure()`.
  - `OpenTradeCommand` gains an optional `PreTradeChecklistSubmission?` parameter; the handler reads the active risk profile via `IIdentityUserRiskProfileReader`, validates the checklist, and returns a 422-mapped `validation` error if the checklist fails.

## Non-Goals and Later Waves

- No role-based access control, no impersonation, no admin reset of risk profile (Admin surface stays subscription-only per Wave 0).
- No Stripe changes; no payment-method, no plan tier migration.
- No AI suggestions, no LLM-driven review summarization, no sentiment classification.
- No realtime push (SignalR, WebSockets) — metrics are computed on demand with HTTP cache headers.
- No multi-tenant partitioning — every table keeps `user_id` discriminator and indexes.
- Wave 2 (Strategies/Alerts/Planner), Wave 3 (Scanner/MarketData) and Wave 4 (Imports/AI) are separate changes.

## Chained Delivery, Validation, Rollout, and Rollback

Each slice is the first chained-PR of a feature-branch chain; later PRs of the same slice target the immediate previous PR branch (so each child diff stays focused). The first PR of each slice targets `feature/0a-identity-model`.

| Slice (≤400 lines / sub-slice) | Deliverable | Validate | Rollout / rollback |
|---|---|---|---|
| **1f** metrics server-side | `GetTradingMetricsQuery`/`Handler`, `MetricsDto`, `GET /api/trades/metrics?period=`, frontend `MetricsApiService` replacing client-side mocks | `dotnet test` unit (Trading.UnitTests 8 specs) + `npx jest` frontend (4 specs) + analytics page renders server values verbatim | Endpoint disabled by feature flag (`Trading:Metrics:Enabled`); revert to client mocks if regression; additive migration rollback by dropping column |
| **1a** risk-profile | `RiskProfile` aggregate, `RiskPerTradePercent`/`MaxDrawdownPercent`/`RiskRewardRatio` VOs, migration `0009_risk_profiles.sql`, `GET/PUT /api/risk-profile`, FE tab | `dotnet test` (Identity.UnitTests 6 specs) + `npx jest` (FE 4 specs) + OpenAPI round-trip | Endpoint disabled via reverse proxy; revert migration by `DROP TABLE identity.risk_profiles`; null `RiskProfile` reads fall back to "no profile" state |
| **1c** pre-trade checklist | `PreTradeChecklist` aggregate, factory `Trade.OpenWithChecklist(...)`, 422 mapping, migration `0011_pre_trade_checklists.sql`, FE checklist component | `dotnet test` (Trading.UnitTests 7 specs) + `npx jest` (FE 3 specs) + OpenTrade-without-checklist still works (back-compat) | Checklist becomes optional again by removing the optional param; existing OpenTrade calls unaffected |
| **1b** position-size calculator | `CalculatePositionSizeQuery`/`Handler`, `PositionSizeDto`, `GET /api/position-size?symbol=&riskAmount=&stop=` | `dotnet test` (Trading.UnitTests 6 specs) + integration with active risk profile | Endpoint disabled; no DB impact; revert is a file deletion |
| **1d** post-trade review + MinIO | `TradeReview` + `TradeAttachment` aggregates, `IMinioAttachmentStore` (presigned URL), migration `0012_trade_reviews_and_attachments.sql`, `POST /api/trades/{id}/review`, `POST .../attachments`, `POST .../complete`, FE Review form | `dotnet test` (Trading.UnitTests 8 specs) + integration tests against a MinIO container | Endpoint disabled; attachments table `DROP TABLE trading.trade_attachments`; MinIO bucket cleanup |
| **1e** integration tests | `tests/IntegrationTests/JadeCapital.Api.IntegrationTests/Trading/` sub-folder with `TradeFlowTests`, `RiskProfileFlowTests`, `TradingMetricsTests`, `ChecklistFlowTests`, `ReviewAndAttachmentTests` using Testcontainers + Respawn | `dotnet test` (Trading integration suite) | No runtime surface; tests are gated behind CI; revert by removing test files |

Each slice above is itself split into 1–3 chained PRs when it exceeds the 400-line budget; e.g. `1f` ships as `1f.1` metrics backend (~350 LOC) → `1f.2` metrics frontend (~140 LOC). The first PR of every slice is a single, focused, reviewable diff.

## Dependencies and Risks

- **Dependencies**: SMTP (already wired Wave 0), PostgreSQL + Redis Testcontainers (already wired), MinIO container (added to `docker-compose.yml` in slice 1d), predecessor slice outputs (1f → 1a → 1c → 1b → 1d → 1e), `JadeCapital.Identity.Contracts` projection pattern.
- **Risks**:
  - **MinIO wiring** in .NET (`MinioClient` lifecycle, IAM policies, presigned URL TTL, bucket CORS): mitigated by isolating behind `IMinioAttachmentStore` so the host never references `MinioClient` directly and tests can stub the store.
  - **Cross-module RiskProfile read**: Trading must NOT take a hard dependency on `JadeCapital.Identity.Domain`. Mitigated by `IIdentityUserRiskProfileReader` projection in `Identity.Contracts` — same shape as `IUserOwnerProjection`.
  - **OpenTrade extension refactor**: adding `PreTradeChecklistSubmission?` to `OpenTradeCommand` is an additive change; existing callers compile. Tests assert both branches.
  - **Metrics performance**: full-table scan for SQN/drawdown on the user's trade history. Mitigated by `ix_trades_user_opened_at` (already exists) + bounded `period` (max `all` only for power users; documented in OpenAPI).
  - **Migration drift**: every migration is additive, nullable or `IF NOT EXISTS`, so a partial rollback never loses data.

## Success Criteria

- [ ] `GET /api/trades/metrics?period=7d|30d|90d|all` returns expectancy, profit factor, payoff, SQN, max drawdown, equity curve, drawdown overlay, per-symbol stats for the authenticated user, computed from `trading.trades` only (no client-side mocks).
- [ ] `GET/PUT /api/risk-profile` round-trips the active risk profile (single-active constraint enforced by partial unique index in `0009_risk_profiles.sql`).
- [ ] `POST /api/trades` with a failing checklist returns 422 with field-level reasons and persists nothing; without a checklist it behaves exactly as before.
- [ ] `GET /api/position-size` returns the correct `volume` for `(capital, riskPerTradePercent, entryPrice, stopPrice)` using `volume = (capital × riskPct/100) / |entry - stop|` with NUMERIC(24,8) safety.
- [ ] A closed trade can carry a review with attachments; attachments live in MinIO under `trading/attachments/{userId}/{tradeId}/{attId}` and the backend never proxies upload bytes.
- [ ] The Trading integration suite (Testcontainers + Respawn) covers Open → Checklist → Close → Review → Metrics end-to-end and is green in CI.
- [ ] **Wave 1 close criterion**: the analytics dashboard renders **all** metrics server-side — `initialBalance` and `dailyYield` mocks are deleted from `analytics.page.ts` and replaced with `api.metrics(period)`.
- [ ] Every chained slice stays within ≤400 authored changed lines per PR and is independently reversible.