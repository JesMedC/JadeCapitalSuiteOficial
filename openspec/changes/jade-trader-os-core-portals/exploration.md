# Exploration: Jade Trader OS — Trader + Admin portals + Password Recovery (corrected)

> Scope source: user-confirmed narrowing (Engram `sdd/jade-trader-os/scope`, observation #386).
> Master Prompt source: `/home/jesus/ProyectoOficialJadeCapitalSuite/MASTER PROMPT — JADE TRADER OS.md` (2,710 lines, 49 sections).
> Project key: `proyectooficialjadecapitalsuite`.
> Change name: `jade-trader-os-core-portals`.
> Delivery strategy: `force-chained`, review budget 400 lines per slice.
> Document mode: exploration separates full target roadmap from the first implementable change.

---

## 1. Stack reality vs Master Prompt

The Master Prompt specifies React 19 + Vite + Tailwind + React Query + REST `/api/v1/*` on top of ASP.NET Core. The actual implemented stack is **Angular 19** (standalone, Signals, OnPush, strict TS, SCSS, path aliases `@core/* @shared/* @features/* @env/*`, prefix `jcs-*`) on **ASP.NET Core .NET 10** with EF Core 9.0.1, MediatR 12, FluentValidation 11, JWT HS256 + rotating refresh tokens + PBKDF2, no ASP.NET Identity.

User-confirmed scope says: "Maintain the established TypeScript frontend stack and conventions. Do not rewrite the app or change frameworks." Therefore every Master Prompt section that contradicts the existing stack is **superseded by the codebase**, not the other way around. The corrections in §3 below preserve Angular 19 + .NET 10 + Clean/Vertical Slices as the authoritative substrate.

---

## 2. Current State of Code (ground truth, read-only)

### 2.1 Modules on disk

```
src/2.Modules/
├── Identity/         → FULL vertical slice (Domain/Application/Infrastructure/Api/Contracts)
├── Trading/          → FULL vertical slice (Domain/Application/Infrastructure/Api/Contracts)
├── Billing/          → SCAFFOLD (csproj only, no .cs)
├── Admin/            → SCAFFOLD (Application + Infrastructure csproj, no .cs)
└── PublicPortal/     → SCAFFOLD (Application + Infrastructure csproj, no .cs)
```

`docs/PROJECT-STATUS.md` (dated 2026-08-09) describes Trading as empty. That doc is stale: Engram observations and on-disk `.cs` show Trading is fully implemented through Sprint 1.5C/1.6 (Account/Instrument/Trade aggregates, Settings UI, slide-out CreateTradeForm). Treat **the code on disk** as the source of truth.

### 2.2 Identity — what exists today

Verified present in `src/2.Modules/Identity/`:

- **Aggregates**: `User : AggregateRoot<Guid>` (`MaxFailedLoginAttempts=5`, `LockoutMinutes=15`, methods `Register / ChangePassword / ChangeDisplayName / ChangeTimezone / ChangeRole / Suspend / Reactivate / Cancel / RecordSuccessfulLogin / RecordFailedLogin / IsLockedOut / CanAuthenticate`; states `Active | Suspended | Cancelled | LockedOut`). `RefreshToken : Entity<Guid>` with SHA-256 hash, chained rotation, reuse detection.
- **Domain events**: `UserRegistered`, `UserPasswordChanged`, `UserRoleChanged`, `UserSuspended`, `UserReactivated`, `UserCancelled`, `UserLockedOut`.
- **Commands/Queries** (MediatR, xUnit covered): `Register`, `Login`, `RefreshToken`, `Logout`.
- **Endpoints** (`MapGroup("/api/auth")`): `POST /register`, `POST /login`, `POST /refresh`, `POST /logout`. All four carry `.RequireRateLimiting("auth-strict")`.
- **Security**: `Pbkdf2PasswordHasher` (HMAC-SHA256, 100k iters, 16-byte salt, 32-byte output, format `{iter}.{saltB64}.{hashB64}`); `JwtTokenService` (HS256, 15-min access, opaque 48-byte URL-safe refresh, SHA-256 hashed at rest).
- **Persistence**: schema `identity`, tables `users` + `refresh_tokens`. SQL-managed migrations (no EF migrations in production).
- **Validation pipeline**: `ValidationBehavior<,>` registered via `Shared.Infrastructure.AddSharedInfrastructure()` and wired into MediatR — FluentValidation validators DO run.
- **Background jobs**: `RefreshTokenCleanupService : BackgroundService` already running with batch delete + retention config.

### 2.3 Identity — gaps for password recovery

Verified absent:

- **Email infrastructure**: `grep -rln -i "email\|smtp\|mail" src/2.Modules/Identity` returns only the auth endpoints file. No `IEmailSender` abstraction, no SMTP, no `EmailMessage`. ADR-0001 explicitly notes "no hay SMTP, SendGrid, ni servicio de envío".
- **Temporary password storage**: `User` has no `TempPasswordHash`, no `TempPasswordExpiresAt`, no `MustChangePassword`.
- **Password history**: No `Identity.PasswordHistory` table, no `PasswordHistoryEntry` entity, no `IPasswordHistoryRepository`.
- **Forgot-password endpoint**: `AuthEndpoints.cs` has no `POST /api/auth/forgot-password`.
- **Change-password endpoint**: No `POST /api/auth/change-password`. `User.ChangePassword(newHash)` is a dead domain method.
- **Login result doesn't carry `mustChangePassword`**: `LoginResult` has no flag for forced change.
- **Login doesn't accept a temporary credential**: Login only hashes against `PasswordHash`.

### 2.4 Trader portal — what exists today

`frontend/src/app/features/trader/`:

- `trader-shell.ts` (388 lines): Angular standalone, OnPush, Signals. Sidebar with brand, nav section "Trading", `navItems: Dashboard | Operaciones | Calendario | Settings`, user card + logout at bottom.
- `trader.routes.ts`: lazy-loaded `dashboard`, `trades`, `calendar`, `settings`.
- `dashboard/dashboard.page.ts`: real `/api/trades/dashboard` + `/api/trades` (Sprint 1).
- `trades/trades-list.page.ts` + `create-trade-form.ts`: real backend (Sprint 1.6).
- `analytics/analytics.page.ts`: real backend, period filter.
- `calendar/calendar.page.ts`: real monthly heatmap (Sprint 1.6).
- `settings/settings.page.ts`: Accounts + Instruments tabs (Sprint 1.5C).
- Orphaned duplicates (`trades-list/`, older `analytics/`) flagged in PROJECT-STATUS, not load-bearing.
- `authGuard: CanMatchFn` checks `auth.isAuthenticated()`. No must-change-password guard yet.

### 2.5 Admin portal — what exists today

- `features/admin/admin-shell.ts` (8 lines, placeholder "Próximamente: gestión de usuarios y suscripciones").
- `features/admin/admin.routes.ts` (4 lines, declared but **not wired into `app.routes.ts`**).
- Backend: `src/2.Modules/Admin/JadeCapital.Admin.{Application,Infrastructure}/` contain only `.csproj` — no Domain, no Api, no Contracts, no `.cs` source.
- No `AdminOnly` authorization policy. `UserRole.Admin` enum value exists but has no consumer.

### 2.6 Public portal — out of scope, untouched

- `features/public/landing/landing-page.ts` (571 lines), `pricing/pricing-page.ts` (46 lines, hard-coded `PLANS` const), `faq/faq-page.ts`. Reached via `'/'`, `'/pricing'` (no `authGuard`).
- `src/2.Modules/PublicPortal/` is empty scaffold — pricing copy lives in frontend TS.
- **No product/code modifications** to PublicPortal. Pricing tiers in the public-facing `PLANS` array are **marketing copy only**, not authoritative.

### 2.7 Infrastructure — `.env`, `docker-compose.yml`, SQL migrations

- `.env.example` (35 lines) — Postgres/Redis/MinIO/JWT/Stripe blocks. No SMTP block.
- `docker-compose.yml` — 7 services: postgres, redis, minio, migrate, api, frontend. No mailpit/mailhog.
- `infrastructure/postgres/migrations/` — 5 SQL files (`0001_InitialIdentitySchema`, `0002_TradingSchema`, `0003_TradingConfigurationSchema`, `0004_AccountMarketType`, `0005_InstrumentMultiAssetClass`). Harness at `infrastructure/postgres/migrate.Dockerfile` runs `psql` with `ON_ERROR_STOP=1`. All migrations are idempotent.

---

## 3. Master Prompt → Codebase Mapping (Trader OS)

This is the requirement-traceability matrix. Every Master Prompt Trader OS capability maps to one of four buckets: **reuse as-is**, **extend**, **new in Trader portal**, **explicitly out of scope**. No Master Prompt Trader capability is silently carved out — they are either planned in a specific SDD change or excluded for a stated reason.

| MP § | Trader OS capability | Existing code | Action | SDD change |
|---|---|---|---|---|
| §1 | ANALYZE→PLAN→CONTROL→REGISTER→EVALUARE→LEARN loop (V1, no execution) | Trader portal already on this loop via Dashboard → Trades → Calendar | Reuse | n/a (foundation) |
| §7 | Sidebar navigation: Dashboard / Operations / Accounts / Journal / Analytics / P&L Calendar / Strategies / Pattern Scanner / Risk Center / Trade Planner / Markets / Alerts / AI Copilot / Settings | Trader shell has Dashboard / Operaciones / Calendario / Settings | **Extend** sidebar with the missing entries | Wave 2 (Journal+Risk) → Wave 3 (Strategies+Planner+Alerts) → Wave 4 (Scanner+Markets+AI) |
| §7 | Top bar: Global Search / Quick Filters / +Registrar operación / Alertas / AI Copilot / Perfil | Trader shell currently has none of these | New | Wave 3 |
| §9 | Accounts module | Trading module: `trading.accounts`, `OpenAccountCommand`, `Deactivate/Reactivate/DeleteAccount` | **Reuse** — do not duplicate | n/a |
| §10 | Operations (Trades) — Forex + Binary fields with separate math | Trading module: `trading.trades`, separate `r_multiple` calc, separate binary payout math | **Reuse** — do not duplicate | n/a |
| §11 | Dashboard — KPIs (Equity, Net P&L, P&L %, Win Rate, Expectancy, Profit Factor, Current Drawdown), selector (all accounts / specific), period (Today/7D/30D/Month/Year/Custom), Equity Curve, Performance by Market, Risk Status, Últimas operaciones, P&L Calendar resumido, Insights, Upcoming Alerts | `DashboardPage` exists with basic KPIs + equity curve from `/api/trades/dashboard` | **Extend**: add Risk Status, Upcoming Alerts, Insights, period selector with all options | Wave 1 PR-1c |
| §12 | P&L Calendar — Day/Week/Month/Year/Heatmap views, drawer per day, metric selector (P&L, P&L %, R, Win Rate, Trades, Drawdown, Disciplina), weekday×hour heatmap, calendar-year heatmap | `CalendarPage` exists with monthly heatmap from `/api/trades/calendar` | **Extend**: add Year view, weekday×hour heatmap, calendar-year heatmap, per-day drawer, metric selector | Wave 1 PR-1a |
| §13 | Journal — PRE/IN/POST-trade model, trade thesis / strategy / setup / market condition / confidence / emotion / checklist / screenshots (PRE); seguí el plan / quality score / repetiría / errores / aciertos / lección / emoción posterior (POST); classification GOOD_TRADE_GOOD_RESULT, GOOD_TRADE_BAD_RESULT, BAD_TRADE_GOOD_RESULT, GOOD_TRADE_BAD_RESULT (independiente del resultado); DisciplineScore 0–10 | None | **New** in Trader portal; `trading.journal_entries` table + DisciplineScore field | Wave 2 PR-2a |
| §14 | Risk Center — `maximum_risk_per_trade`, `maximum_daily_loss`, `maximum_weekly_loss`, `maximum_monthly_loss`, `maximum_drawdown`, `maximum_trades_per_day`, `maximum_consecutive_losses`, `maximum_open_risk`; states `NORMAL / WARNING / HIGH_RISK / STOP_TRADING`; prominent STOP TRADING banner; `RiskViolation` | None | **New** in Trader portal; `trading.risk_profiles` + `trading.risk_snapshots` + `trading.risk_violations` | Wave 2 PR-2b |
| §15 | Analytics — tabs: Overview / Performance / Strategies / Markets / Time / Risk / Behavior / Accounts / Binary; Performance metrics (Net P&L, Gross Profit/Loss, Win Rate, Loss Rate, BE Rate, Avg Win/Loss, Largest Win/Loss, Payoff Ratio, Profit Factor, Expectancy, Expectancy R, Avg R, Total R); Risk metrics (Max DD, Avg DD, Recovery Factor, Avg Risk, Max Risk, Risk of Ruin, Consecutive W/L); Execution metrics (MAE/MFE/Entry Eff/Exit Eff); Consistency; Behavioral (trades/day, overtrading, off-plan, post-loss/post-win); Temporal (hour/weekday/session/month/timeframe/duration); Cross analytics (Strategy × Hour, etc.) | `AnalyticsPage` exists with basic Overview (KPIs, donut, line chart, top 5, breakdown) | **Extend**: add Strategies / Markets / Time / Risk / Behavior / Accounts / Binary tabs; add MAE/MFE when execution data exists; add cross-analytics | Wave 1 PR-1b |
| §16 | Strategies / Playbook — Strategy + StrategyVersion + Setup + StrategyRule entities; status `DRAFT/BACKTEST/FORWARD_TEST/LIVE/PAUSED/ARCHIVED`; overview/rules/setups/trades/performance/versions detail; per-version performance isolation | None | **New** in Trader portal; `trading.strategies` + `trading.strategy_versions` + `trading.setups` | Wave 3 PR-3a |
| §17–§22 | Harmonic Pattern Scanner — `IPatternDetectionEngine`, `IHarmonicPatternDetector`; patterns Gartley, Bat, Butterfly, Crab, Deep Crab, AB=CD, Cypher; ratios in configuration (NOT hardcoded); pivot detection with ZigZag-style depth/deviation/backstep/minimum_move; Pattern Score 0–100 (geometry 40 + fibonacci 25 + prz 15 + confluence 20); state machine DETECTED/FORMING/APPROACHING_PRZ/PRZ_REACHED/CONFIRMATION/ACTIVE/COMPLETED/INVALIDATED/EXPIRED; PatternDetail view with X/A/B/C/D/PRZ/Invalidation/Targets; confluences (S/R, EMA, RSI, structure, fibonacci, volume, session, higher TF trend) | None | **New** as a `Scanner` bounded context that consumes `IMarketDataProvider`; ratio config via `trading.harmonic_ratio_configs`; persist detections | Wave 4 PR-4a (Scanner) |
| §23 | Alert Center — categories PATTERN/RISK/STRATEGY/MARKET/ACCOUNT/SYSTEM; states UNREAD/READ/DISMISSED/ACTIONED; user preferences per category; V1 in-app only | None | **New** in Trader portal; `trading.alerts` + `trading.alert_preferences` | Wave 3 PR-3b |
| §24 | Trade Planner — `TradePlan` + `TradePlanChecklist`; states DRAFT/READY/EXECUTED/CANCELLED/EXPIRED; "Marcar como ejecutado" creates a real trade from plan data; never auto-create | None | **New** in Trader portal; `trading.trade_plans` + `trading.trade_plan_checklists` | Wave 3 PR-3c |
| §25 | Scanner→Trade flow: PatternDetection → Pattern Alert → Pattern Detail → Create Trade Plan → Risk Validation → Trader executes externally → Register Trade → Journal → Analytics | Not wired today (Scanner missing) | **New** as cross-cutting flow once Scanner + Planner + Journal exist | Wave 4 (post-Scanner) |
| §26 | Pattern Analytics — performance per pattern type / instrument / timeframe / direction / session / strategy / account; user-local performance | None | **New** | Wave 4 PR-4a |
| §27 | Binary Analytics — Win Rate / Avg Payout / Break-even WR (1/(1+payout_decimal)) / Expected Value / Total Stake / Net Profit / ROI / Longest Win/Loss Streak / Performance by Expiration/Asset/Setup/Hour/Session / CALL vs PUT | Trading module already computes per-trade binary payouts; aggregation queries missing | **Extend**: add Binary aggregation queries + tab on AnalyticsPage | Wave 1 PR-1b (Analytics extension) |
| §28 | Market Data abstraction — `IMarketDataProvider` with `GetSymbols/GetCandles/GetLatestPrice/SubscribeQuotes`; Candle model (symbol, timeframe, ts, OHLCV); timeframes M1/M5/M15/M30/H1/H4/D1 | None | **New** as `MarketData` bounded context; pattern scanner + economic calendar consume this interface | Wave 4 PR-4b |
| §29 | Economic Calendar — country / currency / title / impact (LOW/MEDIUM/HIGH) / event_time / forecast / previous / actual; integration with Journal/Planner/Risk/Alerts | None | **New** in Trader portal; `trading.economic_events` + provider abstraction | Wave 4 PR-4c |
| §30 | AI Copilot — `IAIProvider` + `ITradingInsightService`; queries must be answered from backend analytics, never invented; future questions only | None | **New** as `AI` module shell (interface + 1-2 queries) | Wave 5 (low priority, master prompt says "DESPUÉS de tener datos suficientes") |
| §31 | CSV Imports — ImportJob + ImportMapping + ImportError; flow Upload→Preview→Map Columns→Validate→Import→Summary; duplicate detection via `external_trade_id` + `import_hash`; deterministic datasets | None | **New** in Trader portal | Wave 5 PR-5a |
| §32 | `/api/v1/*` versioning | Currently `/api/*` | **Out of scope** — supersede master prompt, keep current path. Tracked in docs only. |
| §33 | Security: PBKDF2 hash + JWT short-lived + refresh rotation + rate-limit + workspace isolation + audit logs + CORS restricted + secure headers + secrets via env + "Nunca persistir passwords en texto plano" + "Preparar 2FA" | Most present (PBKDF2, JWT, refresh, rate-limit `auth-strict`); audit logs partial; workspace isolation N/A (single-tenant); 2FA not started | **Extend** with password recovery (this exploration) and audit-event table (later); 2FA deferred | Wave 0 PR-0a/PR-0b/PR-0c |
| §34 | Tests: Unit / Integration / API; P&L/R/Binary/Drawdown/PF/Expectancy/Calendar/Workspace/Risk/Harmonic Ratios/Pattern state transitions/Duplicate imports | Identity + Trading unit tests + Auth integration tests + HealthCheck tests present; Risk/Harmonic/Pattern missing | **Extend** per module as built | each Wave's PRs |
| §35 | Seed data — 4 demo accounts, 50+ trades, varios días/sesiones/journals/patrones | Seeds for trading instruments exist; demo workspace + trader + trades missing | **New** | Wave 1 PR-1d (Demo seed) |
| §36 | Responsive: desktop-first 1440px+, tablet, mobile; mobile prioritizes Dashboard/Alerts/Trades/Calendar/Journal | Trader shell mobile media queries present for shell only | **Extend** per page | each page PR |
| §37 | Performance — no `SELECT *` on analytics; indexes on workspace_id, account_id, instrument_id, opened_at, closed_at, strategy_id, setup_id, pattern_type, timeframe, detected_at | Trading SQL has `(account_id, opened_at DESC)` and `(instrument_id, opened_at DESC)` | **Extend** per new table | each Wave's SQL migration |
| §38 | Observability — structured logging for request/error/import/pattern_detection/risk_violation/background_job; no secrets in logs; health checks `/health` `/health/ready` | Serilog structured logging + HealthChecks (`self`, `postgres`, `redis`) present | **Extend** with pattern/risk/import sources | per Wave |
| §39 | Docker compose: postgres/redis/minio/backend/frontend; later worker/redis/email | Compose present; mailpit absent | **Extend** with mailpit service for dev | Wave 0 PR-0b |
| §40 | Versioned migrations; no manual BD changes; seed separated from schema | Existing SQL migrations are idempotent and versioned | **Reuse** | n/a |
| §41 | Phases 0–16 | Phases 0–2 done (Foundation, Accounts, Operations). Phase 3 (Dashboard) partial. Phase 4 (Calendar) partial. Phase 7 (Analytics) partial. Phases 5/6/8/9/10/11/12/13/14/15/16 not yet implemented. | Phased per this roadmap | each Wave |
| §45 | Prohibitions: no microservices, no broker execution, no float/double for money, no duplicate P&L math frontend, no invented metrics, no Pattern Score as probability, no hardcoded workspace/currency/broker | All honored. Trading module uses `Money`/`Currency` VOs and `decimal` everywhere. | **Reuse** | n/a |
| §46 | SOLID / Clean Architecture / DDD where it adds value / Strong typing / Immutability / Explicit rules / Testability / Observability | Already enforced via Clean/Vertical Slices + `Result<T>` + MediatR + `Entity<TId>` | **Reuse** | n/a |

### 3.1 Out of scope — Master Prompt items NOT in Trader OS adaptation

These are excluded for stated reasons (not silently dropped):

- **V1 broker execution** (MP §1): explicit V1 design decision.
- **Public portal copy / pricing source-of-truth** (MP §7 §8 §11): the public `PLANS` const is marketing copy; authoritative subscription state lives in `billing.subscriptions` once built.
- **Multi-tenant Workspace** (MP §5): single-tenant until Billing activation lands.
- **`/api/v1/*` versioning** (MP §32): keep current `/api/*`.
- **2FA / OAuth / SSO / MagicLink / passwordless** (MP §33): not requested. Future-proofable.
- **Light mode** (MP §8): tokens prepared; light theme deferred.
- **Multi-locale / i18n** (MP §36): current copy is Spanish.
- **Soft delete** (MP §6 §45): not in scope.
- **Microservice extraction** (MP §4 §48): V1 monolith.
- **Kubernetes** (MP §39): not in scope.
- **OpenTelemetry / metrics** (MP §38): Serilog only for now.
- **CI/CD pipelines** (MP §49): not in scope.
- **AI Copilot substantive features** (MP §30): interface only, defer until data sufficient.

---

## 4. Architecture Ownership Decisions

### 4.1 Subscription domain — Billing is the natural owner

The Master Prompt places `Subscriptions/` in the module tree (MP §4) and Billing csproj already exists as scaffold (`Application/Domain/Infrastructure/Contracts`). The Clean/Vertical Slice architecture dictates that **domain aggregates live with the bounded context that owns their invariants**. There is no precedent for putting subscription state inside Identity (Identity owns credential lifecycle, not commercial lifecycle). Options:

| Approach | Bounded-context ownership | Pros | Cons | Effort |
|---|---|---|---|---|
| **A. `billing` schema owns Subscription + Plan; Admin is a thin presentation layer that consumes Billing.Application contracts** | Billing = domain owner; Admin = consumer | Honors MP §4 boundary. Clean dependency direction (Admin → Billing). Future Stripe/Billing activation slots in directly. Admin endpoints become a thin "authorize, then delegate" layer. | Requires `Admin → Billing.Application.Contracts` dependency (acceptable for presentation layer). | Medium (~300 LOC across Domain + Application + thin Admin.Api). |
| B. `admin` schema owns Subscription + Plan | Admin = domain owner | Self-contained | Violates MP §4. Forces future Billing module to either re-model or migrate. Splits commercial lifecycle from auth context. | High (rebuild when Billing lands). |
| C. `identity` schema gains `subscription_tier` column on `users` | Identity = domain owner | Smallest diff | Reverses spirit of ADR-0003 ("tiers/planes se modelarán en Billing cuando exista"). No room for plan dates, status history, trial windows. Privilege escalation risk (Subscription writes from User aggregate). | Low but unmaintainable. |

**Recommendation: Approach A.** Billing owns the Subscription aggregate (`SubscriptionTier`, `SubscriptionStatus`, `UserSubscription`, `Plan`); Billing.Application owns the admin-relevant commands/queries (`GetSubscriptionsQuery`, `GetSubscriptionByIdQuery`, `AdminChangeSubscriptionTierCommand`, `AdminCancelSubscriptionCommand`); Admin.Api is a thin csproj that registers these endpoints under `/api/admin/subscriptions/*` with `RequireAuthorization("AdminOnly")`. Admin.Api may depend on Billing.Application.Contracts — that is the only allowed downward arrow.

### 4.2 Module composition rule for the Host

Current Host composition is hand-wired (`AddIdentityInfrastructure`, `AddTradingInfrastructure`, `MapAuthEndpoints`, `MapAccountEndpoints`, etc.). Each new module follows the same pattern:

```
services.AddBillingInfrastructure(builder.Configuration);
services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(typeof(...Billing...Handler).Assembly));
services.AddAssemblyValidators(typeof(...Billing...Validator).Assembly);
app.MapBillingAdminEndpoints();
app.MapAdminEndpoints();   // thin presentation layer
```

No generic `IModule` interface in this slice — that is documented as a deferred ADR.

### 4.3 Trader portal — extend existing bounded context (Trading) for journal/risk/strategies/scanner/alerts

Journal, Risk Center, Strategies, Alerts, Trade Planner, Economic Calendar, Pattern Scanner, CSV Imports are **Trader product features**. They share the same workspace and FK to `identity.users`. Cleanest ownership: they live in the **Trading bounded context** as new aggregates under the `trading` schema, not in separate modules. Rationale:

- Trading already owns `Account`, `Instrument`, `Trade`. Journal entries, risk profiles, strategies, alerts, plans, and pattern detections all FK to `trading.trades` or `identity.users` and are queried together (e.g., Journal for a trade, Risk status of an account).
- Splitting into separate modules would force cross-context joins via contracts, increasing churn.
- Master Prompt §4 explicitly groups these inside the trading-domain mental model.

`Scanner`/`MarketData` are an exception: they need provider abstractions (`IMarketDataProvider`, `IPatternDetectionEngine`) that can be consumed by both Trading (for Alert generation) and future AI. They become their own bounded contexts (`MarketData`, `Scanner`) with `Trading.Application` as the primary consumer.

### 4.4 Password recovery lives in Identity

`MustChangePassword`, `TempPasswordHash`, `TempPasswordExpiresAt`, `PasswordHistoryEntry`, the `IEmailSender` dependency are all credential-lifecycle concerns. Identity owns them. `IEmailSender` lives in **`JadeCapital.Shared.Infrastructure`** (cross-cutting, reusable for future 2FA/email verification/alerts).

---

## 5. Trader Portal Roadmap (multiple SDD changes, each = a chained PR wave)

Each **SDD change** is a complete OpenSpec proposal→spec→design→tasks→apply→verify→archive cycle. Within each change, work is split into chained PRs of ≤400 changed lines each.

### 5.1 Wave 0 — Authentication + Subscription foundation (FIRST implementable change)

**SDD change: `jade-trader-os-core-portals`** (this artifact's namesake).

| PR | Scope | Files | LOC | Verified by |
|---|---|---|---|---|
| **PR-0a** | Password recovery — domain + schema | SQL `0006_PasswordRecovery.sql`; `User.cs` extensions; `PasswordHistoryEntry.cs` (new); `IdentityDomainErrors.cs` adds; `UserConfiguration.cs` adds; `PasswordHistoryConfiguration.cs` (new); 3 new domain unit-test files | ~340 | `dotnet test tests/UnitTests/JadeCapital.Identity.UnitTests` |
| **PR-0b** | Password recovery — application + API + email transport | `IEmailSender` + `IPasswordHistoryRepository` + `MailKitSmtpEmailSender` (prod) + `MailpitSmtpEmailSender` (dev profile) + `InMemoryCapturingEmailSender` (test); `ForgotPasswordCommand/Handler`; `ChangePasswordCommand/Handler`; `LoginResult` + `LoginHandler` branches; `AuthEndpoints.cs` adds 2 routes; `IdentityModuleRegistration.cs` wires; 1 new integration test (`PasswordRecoveryFlowTests`); `docker-compose.yml` adds `mailpit` service; `.env.example` adds `Mail__*` block | ~390 | `dotnet test` (including `MailpitSmtpEmailSender`-backed integration test), `dotnet build` |
| **PR-0c** | Frontend — Trader portal auth flow | `forgot-password.page.ts` (new); `change-password-required.page.ts` (new); `change-password.page.ts` (new); `auth.state.ts` extends; `auth.guard.ts` + `must-change-password.guard.ts` (new); `error.interceptor.ts` branches; `auth.routes.ts` extends; `login.page.ts` wire; `trader-shell.ts` adds top banner | ~360 | `npm --prefix frontend build`, Playwright/manual e2e against Mailpit |
| **PR-0d** | Billing subscription domain foundation | `Billing.Domain/Subscriptions/{Subscription,Plan,UserSubscription}`; `Billing.Domain/Enums/{SubscriptionTier,SubscriptionStatus,PlanInterval}`; `Billing.Domain/Common/SubscriptionErrors.cs`; `Billing.Application/Abstractions/ISubscriptionRepository.cs`; `Billing.Application/Features/Subscriptions/{GetSubscriptionsQuery,GetSubscriptionByIdQuery,AdminChangeSubscriptionTierCommand,AdminCancelSubscriptionCommand,AdminExtendTrialCommand}/{Command|Query,Handler}.cs`; `Billing.Application/_Common/BillingApplicationErrors.cs`; `Billing.Application/Contracts/SubscriptionDtos.cs` (new); SQL migration `0007_BillingSubscriptions.sql`; `Billing.Infrastructure/Persistence/{BillingDbContext,Configurations,Repositories}.cs`; `Billing.Infrastructure/DependencyInjection/BillingModuleRegistration.cs`; `Admin.Api/Endpoints/AdminSubscriptionEndpoints.cs` (thin csproj hosting `/api/admin/subscriptions/*`); Host `Program.cs` adds wiring + `AdminOnly` policy; 4 new unit tests + 1 new integration test (`AdminAuthorizationTests`) | ~395 | `dotnet test`, `dotnet build` |
| **PR-0e** | Frontend — Admin subscription management UI | `admin-shell.ts` expansion; `admin.routes.ts` extends; `admin.guard.ts` (new); `core/api/billing-admin-api.service.ts`; `core/state/admin-subscriptions.state.ts`; `features/admin/subscriptions/subscriptions-list.page.ts`; `features/admin/subscriptions/subscription-detail.page.ts`; `app.routes.ts` wire `/admin` | ~390 | `npm --prefix frontend build`, manual e2e with seeded Admin user |

### 5.2 Wave 1 — Trader portal extension (reuse existing + extend)

**SDD change: `jade-trader-os-trader-extensions-1`.**

| PR | Scope | LOC |
|---|---|---|
| **PR-1a** | Calendar extensions (Year view, weekday×hour heatmap, calendar-year heatmap, per-day drawer, metric selector). Extends `CalendarPage`; new `CalendarApiService`; new SQL view or analytics query for year aggregation; new `MarketType` filter. | ~380 |
| **PR-1b** | Analytics extensions (Strategies / Markets / Time / Risk / Behavior / Accounts / Binary tabs; cross-analytics; MAE/MFE placeholders; binary analytics aggregations). Extends `AnalyticsPage`; new `AnalyticsApiService` methods; new SQL aggregation queries; new domain service `AnalyticsAggregator` for cross-cuts. | ~390 |
| **PR-1c** | Dashboard extensions (Risk Status panel, Upcoming Alerts panel, Insights, period selector with all options Today/7D/30D/Month/Year/Custom, account selector). Extends `DashboardPage`; consumes `GET /api/analytics/overview` extension + new `GET /api/analytics/risk-status` + `GET /api/alerts/upcoming`. | ~380 |
| **PR-1d** | Demo seed (demo trader workspace, 4 demo accounts FTMO/IC Markets/Binary Real/Binance, 50+ trades with forex/binary/crypto mix, journals, risk snapshots, sample patterns). New `trading.demo.seeder` BackgroundService. SQL seed file `0008_DemoSeed.sql` + C# seed runner. | ~390 |

### 5.3 Wave 2 — Trader new modules: Journal + Risk Center

**SDD change: `jade-trader-os-journal-risk`.**

| PR | Scope | LOC |
|---|---|---|
| **PR-2a** | Journal module. New `JournalEntry` aggregate (PRE/IN/POST, trade FK, DisciplineScore 0–10, classification enum); SQL `0009_TradingJournal.sql`; `OpenJournalEntryCommand`, `UpdateJournalEntryCommand`, `CloseJournalEntryCommand`, `GetJournalByTradeIdQuery`, `GetJournalListQuery`; `JournalEndpoints` at `/api/journal`; 4 unit tests; frontend `features/trader/journal/journal-list.page.ts` + `journal-entry.page.ts`; new `JournalApiService` + `JournalState`. | ~395 |
| **PR-2b** | Risk Center. New `RiskProfile` aggregate (per-user rules + states), `RiskSnapshot`, `RiskViolation`; SQL `0010_TradingRisk.sql`; `RiskEvaluator` domain service that consumes TradeCreated events; `GetRiskStatusQuery`, `UpdateRiskProfileCommand`, `GetRiskViolationsQuery`; `RiskEndpoints` at `/api/risk`; 4 unit tests + integration; frontend `features/trader/risk/risk-center.page.ts` + persistent STOP_TRADING banner in TraderShell when status HIGH_RISK or STOP_TRADING. | ~395 |

### 5.4 Wave 3 — Trader new modules: Strategies + Alerts + Trade Planner

**SDD change: `jade-trader-os-strategies-alerts-planner`.**

| PR | Scope | LOC |
|---|---|---|
| **PR-3a** | Strategies / Playbook. New `Strategy` + `StrategyVersion` + `Setup` + `StrategyRule` aggregates; SQL `0011_TradingStrategies.sql`; CRUD commands + per-version performance isolation queries; `/api/strategies` endpoints; 4 unit tests; frontend `features/trader/strategies/strategies-list.page.ts` + `strategy-detail.page.ts` with Overview/Rules/Setups/Trades/Performance/Versions tabs. | ~390 |
| **PR-3b** | Alert Center. New `Alert` aggregate + `AlertPreference`; SQL `0012_TradingAlerts.sql`; `GetAlertsQuery` (paginated, filter by category/status), `MarkAlertReadCommand`, `DismissAlertCommand`, `UpdateAlertPreferencesCommand`; `/api/alerts` endpoints; 4 unit tests; frontend `features/trader/alerts/alerts-center.page.ts` with PATTERN/RISK/STRATEGY/MARKET/ACCOUNT/SYSTEM tabs. | ~390 |
| **PR-3c** | Trade Planner. New `TradePlan` + `TradePlanChecklist` aggregates; SQL `0013_TradingPlanner.sql`; `CreateTradePlanCommand`, `UpdateTradePlanCommand`, `MarkPlanExecutedCommand` (creates a real Trade from plan data — explicit user action only); `GetPlansQuery`; `/api/plans` endpoints; 4 unit tests; frontend `features/trader/planner/planner-list.page.ts` + `plan-detail.page.ts` with DRAFT/READY/EXECUTED/CANCELLED/EXPIRED states and "Marcar como ejecutado" action that links to `CreateTradeForm`. | ~395 |

### 5.5 Wave 4 — Scanner + MarketData + Economic Calendar

**SDD change: `jade-trader-os-scanner-marketdata`.**

| PR | Scope | LOC |
|---|---|---|
| **PR-4a** | Harmonic Pattern Scanner. New bounded contexts `MarketData` + `Scanner`. `MarketData.Domain/IMarketDataProvider` + `Candle` model; `Scanner.Domain/{HarmonicPattern,PatternPoint,PatternDetection,PatternConfluence}`; SQL `0014_MarketDataSchema.sql` + `0015_ScannerSchema.sql`; `IPatternDetectionEngine`, `IHarmonicPatternDetector` (Gartley/Bat/Butterfly/Crab/Deep Crab/AB=CD/Cypher); ratios in config table; Pattern Score 0–100 with subscores; state machine DETECTED→…→COMPLETED/INVALIDATED/EXPIRED; `/api/scanner/patterns` endpoints; `PatternAnalytics` (cross-analytics); 4 unit tests; frontend `features/trader/scanner/scanner-list.page.ts` + `pattern-detail.page.ts`. | ~395 |
| **PR-4b** | MarketData provider implementations. `MarketData.Infrastructure/{StubMarketDataProvider,SmtpMarketDataProvider}` with config swap; Symbol/Timeframe enums; indexes on `(symbol, timeframe, timestamp)`; admin-bypass tests. | ~390 |
| **PR-4c** | Economic Calendar. New `EconomicEvent` aggregate; SQL `0016_TradingEconomicCalendar.sql`; CRUD + provider abstraction; `/api/calendar/economic` endpoints; frontend `features/trader/calendar-economic/calendar-economic.page.ts`. | ~390 |

### 5.6 Wave 5 — Imports + AI shell

**SDD change: `jade-trader-os-imports-ai`.**

| PR | Scope | LOC |
|---|---|---|
| **PR-5a** | CSV Imports. New `ImportJob` + `ImportMapping` + `ImportError`; SQL `0017_TradingImports.sql`; Upload/Preview/Map/Validate/Import/Summary flow; `external_trade_id` + `import_hash` for dedup; deterministic test fixtures; `/api/imports` endpoints; 4 unit tests; frontend `features/trader/imports/imports.page.ts` with wizard. | ~395 |
| **PR-5b** | AI Copilot shell. New `AI.Domain/IAIProvider` + `ITradingInsightService` interface; `AI.Application/Features/Insights/GetInsightQuery` (refuses to invent metrics, only answers from backend analytics); `/api/ai/insights` endpoint; 2 unit tests; frontend `features/trader/ai/ai-copilot.page.ts` minimal shell. | ~380 |

### 5.7 Future / explicitly deferred

- **Multi-tenant Workspace** (MP §5) — requires schema-wide migration.
- **Stripe / Billing activation** (MP §41 Phase 14, §45 no broker execution) — requires a separate SDD change with security review; not in this roadmap.
- **2FA / OAuth / SSO / passwordless** (MP §33) — interface-ready via `IEmailSender`; future change.
- **Replays / Simulator** (MP §41 Phase 16) — explicitly deferred by master prompt itself.
- **OpenTelemetry / metrics beyond Serilog** (MP §38) — separate infrastructure change.
- **Soft delete / multi-locale / light mode / Kubernetes / microservice extraction** — not in roadmap.

---

## 6. Admin Portal Roadmap (subscription administration only)

The Admin portal's scope is **strictly subscription administration**. Per user-confirmed scope: "Focus on the Administrator portal specifically for subscription administration." Therefore:

- **IN scope**: list/search subscriptions, view subscription detail, change tier, cancel, extend trial, view subscription history. Admin endpoints are routed under `/api/admin/subscriptions/*` and gated by `RequireAuthorization("AdminOnly")`.
- **OUT of scope (explicitly)**: user role changes, user suspension/reactivation, user impersonation, audit log viewer, user invitation/management. These belong to a future "user administration" SDD change if/when requested.
- **Strictly necessary identity lookup**: to associate a subscription with its owner when the Admin UI displays "user X has subscription Y", a minimal read-only projection `GET /api/admin/users/{id}/summary` is allowed — this returns `email` + `displayName` + `currentSubscriptionTier` only, no role/status mutations, no impersonation token. This is a 1-endpoint concession to make the Admin UI usable without re-introducing a full user-admin slice.

### 6.1 Wave 0 PR-0d — Subscription admin backend (described in §5.1)

Wave 0 establishes the Billing domain + the thin Admin presentation layer. Subsequent Admin work is incremental:

- **Wave 1 PR-1e** (in Wave 1 SDD change): Admin subscription analytics — MRR, churn, trial-expiring-soon. Cross-cutting aggregation queries. ≈380 LOC.
- **Wave 3 PR-3d** (in Wave 3 SDD change): Admin alert preferences — broadcast alerts to all Admin users. ≈380 LOC.

---

## 7. Password Recovery Flow (security-sensitive, defaults resolved)

### 7.1 Flow (defaults — assumed, not asked)

1. User clicks "¿Olvidaste tu contraseña?" on `login.page.ts` → routes to `/auth/forgot-password`.
2. `ForgotPasswordPage` (new) collects email, calls `POST /api/auth/forgot-password { email }`.
3. `ForgotPasswordHandler` runs in constant time. If user exists, generates a 16-character temp password using `RandomNumberGenerator.GetBytes(16)` encoded as Base32 (Crockford alphabet — `0OIl1` excluded for visual clarity), sets `TempPasswordHash` (PBKDF2 with same params as regular passwords), sets `TempPasswordExpiresAt = UtcNow + 24h`, sets `MustChangePassword = true`. Revokes any previous temp hash. Dispatches `IEmailSender.SendPasswordResetEmailAsync`.
4. Response is always `200 OK { ok: true }` regardless of email existence. Logs at Information level only the email and a redacted user-id hash; **never logs the temp password**.
5. `IEmailSender` is bound to one of three implementations by configuration:
   - `MailKitSmtpEmailSender` (production): real SMTP via `Mail__Host`, `Mail__Port`, `Mail__Username`, `Mail__Password`, `Mail__From`, `Mail__UseStartTls`.
   - `MailpitSmtpEmailSender` (dev): same SMTP client, pointed at Mailpit container (`mailpit:1025`). Captured emails visible at `localhost:8025`. No logs.
   - `InMemoryCapturingEmailSender` (tests): exposes `IReadOnlyList<CapturedEmail>` for assertions. Never logs the body. Used by integration tests.
6. User logs in with email + temp password. `LoginHandler` verifies against `TempPasswordHash` (after also checking `PasswordHash` for backward compat), consumes it (sets `TempPasswordHash = null`, leaves `MustChangePassword = true`), and returns `LoginResult { MustChangePassword = true, PasswordChangeDeadline = UtcNow + 24h }` along with **short-lived access (5 min)** and **single-use refresh tokens**.
7. `AuthState` sees `mustChangePassword: true`, sets its signal, and `authGuard`/`mustChangePasswordGuard` redirect to `/auth/change-password-required`.
8. `ChangePasswordRequiredPage` (persistent forced-change screen — no trader-shell nav available) collects new password (and confirm). Calls `POST /api/auth/change-password { currentPassword, newPassword }`.
9. `ChangePasswordHandler` validates new against `PasswordPolicy.MeetsComplexity`, hashes new, **compares against last 5 password hashes** (`IPasswordHistoryRepository.GetRecentHashesAsync(userId, 5, ct)` — rejection returns `Error.Conflict("auth.password_reused", …)`), persists the new hash, **revokes ALL refresh tokens for the user**, and re-issues one. Sets `MustChangePassword = false`. Pushes the previous hash to history (keep 5 max).
10. `AuthState` clears `mustChangePassword` and routes to `/app/dashboard`.

### 7.2 Defaults assumed (no user input required)

- **History depth = 5** applies to **all** password changes (forced and voluntary).
- **All refresh tokens revoked** on any successful password change.
- **Dedicated `auth-forgot` rate-limit policy**: 5 requests / hour / IP, named distinctly from `auth-strict` (10/min/IP). Forgot-password and change-password both apply `auth-forgot`.
- **Persistent forced-change screen**: no nav available until change completes.
- **Temp password**: 16 chars, Base32 (Crockford) of 128 random bits, excludes `0OIl1`, single-use, 24h expiry, whichever comes first.
- **Local SMTP capture (Mailpit) in dev** + **SMTP provider in prod via configuration**.
- **In-memory `InMemoryCapturingEmailSender` for tests**.
- **Logs NEVER contain plaintext credentials.** Email capture transport is the only path that ever sees the temp password; Serilog sees only `email + userIdHash + correlationId`.

These are proposal defaults. If any needs to change, the user can override at proposal/spec time.

### 7.3 Genuine product decisions surfaced to user

Only items that are NOT safely defaulted by security/architecture best-practice:

1. **Email subject/body branding**: copy wording in Spanish for the reset email (security phrasing, link expiration language). Not architectural.
2. **Reset-email language locale**: Spanish only for V1, or also English? Marketing decision.
3. **Maximum failed temp-password attempts before lockout**: should temp-password attempts count against the existing `MaxFailedLoginAttempts=5` lockout, or use a separate counter? Recommendation: count against the same counter (prevents brute-forcing the temp).
4. **Should Admin be able to trigger a password reset for a user?** (Useful for support but raises social-engineering risk.) Recommendation: defer to a future SDD change.

These are the only items where user input is genuinely required.

---

## 8. Affected Areas — Wave 0 / First Implementable Change (PR-0a through PR-0e)

### 8.1 Backend — Identity module changes (PR-0a + PR-0b)

- `src/2.Modules/Identity/JadeCapital.Identity.Domain/Users/User.cs` — add `MustChangePassword`, `TempPasswordHash`, `TempPasswordExpiresAt`, methods `IssueTemporaryPassword(plaintext, expiry)`, `ConsumeTemporaryPassword(now)`, `VerifyTempPassword(plaintext, now)`, `CompletePasswordChange(newPasswordHash, history)`, `PushPasswordHistoryEntry(hash)`.
- `src/2.Modules/Identity/JadeCapital.Identity.Domain/Authentication/PasswordHistoryEntry.cs` (new) — `Entity<Guid>` with `UserId`, `PasswordHash`, `CreatedAt`.
- `src/2.Modules/Identity/JadeCapital.Identity.Domain/Common/IdentityDomainErrors.cs` — add `TempPassword.Expired`, `TempPassword.AlreadyUsed`, `Password.Reused`, `Email.SendFailed`.
- `src/2.Modules/Identity/JadeCapital.Application/Abstractions/IEmailSender.cs` (new) — `SendPasswordResetEmailAsync(to, displayName, tempPassword, ct)`.
- `src/2.Modules/Identity/JadeCapital.Identity.Application/Abstractions/IPasswordHistoryRepository.cs` (new) — `GetRecentHashesAsync(userId, count, ct)`, `AddAsync(entry, ct)`.
- `src/2.Modules/Identity/JadeCapital.Identity.Application/_Common/IdentityApplicationErrors.cs` — add `ChangePassword.PasswordReused`, `ForgotPassword.RateLimited`.
- `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/ForgotPassword/{ForgotPasswordCommand,ForgotPasswordHandler}.cs` (new) — constant-time `Success` return.
- `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/ChangePassword/{ChangePasswordCommand,ChangePasswordHandler}.cs` (new) — current password verify, complexity, history check, revoke all refresh tokens, re-issue one.
- `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/Login/LoginCommand.cs` — extend `LoginResult` with `bool MustChangePassword`, `DateTimeOffset? PasswordChangeDeadline`, short-lived access token variant.
- `src/2.Modules/Identity/JadeCapital.Identity.Application/Features/Auth/Login/LoginHandler.cs` — branch on `user.MustChangePassword` and `VerifyTempPassword`, consume temp on success, set short TTL.
- `src/2.Modules/Identity/JadeCapital.Identity.Api/Endpoints/AuthEndpoints.cs` — add `POST /api/auth/forgot-password` (anonymous, `auth-forgot` rate-limit); add `POST /api/auth/change-password` (authenticated, `auth-forgot` rate-limit).
- `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/DependencyInjection/IdentityModuleRegistration.cs` — register `IPasswordHistoryRepository`, `IEmailSender` (config-bound), `IPasswordGenerator`.
- `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/Configurations/UserConfiguration.cs` — 3 new column mappings.
- `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/Configurations/PasswordHistoryConfiguration.cs` (new) — table + index.
- `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/Repositories.cs` — `PasswordHistoryRepository`.
- `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Security/PasswordGenerator.cs` (new) — Crockford Base32 generator.
- `src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Email/` (new) — `MailKitSmtpEmailSender`, `MailpitSmtpEmailSender`, `InMemoryCapturingEmailSender` (test-only assembly).
- `infrastructure/postgres/migrations/2026MMDD_0006_PasswordRecovery.sql` (new) — idempotent.
- `docker-compose.yml` — add `mailpit` service (`axllent/mailpit:latest`, ports 1025 SMTP + 8025 UI), depends_on `api` profile.
- `.env.example` — add `Mail__Host`, `Mail__Port`, `Mailpit__Enabled`, `Mail__Username`, `Mail__Password`, `Mail__From`, `Mail__UseStartTls`.
- Tests: `ForgotPasswordHandlerTests.cs`, `ChangePasswordHandlerTests.cs`, `PasswordHistoryTests.cs`, `TemporaryPasswordTests.cs`, `PasswordGeneratorTests.cs`, integration `PasswordRecoveryFlowTests.cs`.

### 8.2 Backend — Billing + Admin modules (PR-0d)

- `src/2.Modules/Billing/JadeCapital.Billing.Domain/` (new `.cs`) — `Plan`, `UserSubscription`, `Enums/{SubscriptionTier,SubscriptionStatus,PlanInterval}`, `Common/BillingDomainErrors.cs`.
- `src/2.Modules/Billing/JadeCapital.Billing.Application/Abstractions/ISubscriptionRepository.cs` (new).
- `src/2.Modules/Billing/JadeCapital.Billing.Application/_Common/BillingApplicationErrors.cs` (new).
- `src/2.Modules/Billing/JadeCapital.Billing.Application/Features/Subscriptions/{GetSubscriptionsQuery,GetSubscriptionByIdQuery,AdminChangeSubscriptionTierCommand,AdminCancelSubscriptionCommand,AdminExtendTrialCommand}/{Command|Query,Handler}.cs` (new).
- `src/2.Modules/Billing/JadeCapital.Billing.Contracts/SubscriptionDtos.cs` (new) — `SubscriptionDto`, `SubscriptionListDto`, `ChangeTierRequest`, `ExtendTrialRequest`.
- `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/Persistence/{BillingDbContext,Configurations/SubscriptionConfiguration,Configurations/PlanConfiguration,Repositories/SubscriptionRepository}.cs` (new).
- `src/2.Modules/Billing/JadeCapital.Billing.Infrastructure/DependencyInjection/BillingModuleRegistration.cs` (new `.cs`).
- `src/2.Modules/Admin/JadeCapital.Admin.Api/JadeCapital.Admin.Api.csproj` (new csproj, depends on Billing.Application.Contracts).
- `src/2.Modules/Admin/JadeCapital.Admin.Api/Endpoints/AdminSubscriptionEndpoints.cs` (new) — thin delegation to Billing handlers; `RequireAuthorization("AdminOnly")`.
- `src/2.Modules/Admin/JadeCapital.Admin.Api/Abstractions/IUserIdentityReadOnlyService.cs` (new) — single read-only projection for owner identity (`email`, `displayName`, `currentTier`).
- `src/2.Modules/Admin/JadeCapital.Admin.Api/UserIdentityReadOnlyService.cs` (new) — queries `identity.users` for owner lookup only (no mutation, no impersonation).
- `src/1.Api/JadeCapital.Host/Program.cs` — add `services.AddBillingInfrastructure`, add `AdminOnly` policy, `app.MapAdminEndpoints()`.
- `infrastructure/postgres/migrations/2026MMDD_0007_BillingSubscriptions.sql` (new).
- Tests: 4 Billing handler unit tests, 1 Admin authorization integration test (`AdminAuthorizationTests`).

### 8.3 Frontend — Trader auth flow (PR-0c)

- `frontend/src/app/features/auth/forgot-password/forgot-password.page.ts` (new).
- `frontend/src/app/features/auth/change-password-required/change-password-required.page.ts` (new).
- `frontend/src/app/features/auth/change-password/change-password.page.ts` (new).
- `frontend/src/app/core/state/auth.state.ts` — `forgotPassword()`, `changePassword(current, next)`, `mustChangePassword` signal. Extend `AuthResponse` with `mustChangePassword` + `passwordChangeDeadline`.
- `frontend/src/app/core/guards/auth.guard.ts` — augment CanMatch for `mustChangePassword` redirect.
- `frontend/src/app/core/guards/must-change-password.guard.ts` (new).
- `frontend/src/app/core/interceptors/error.interceptor.ts` — branch on `auth.password_change_required`.
- `frontend/src/app/features/auth/auth.routes.ts` — add 3 routes.
- `frontend/src/app/features/auth/login/login.page.ts` — wire `<a class="forgot">` to `/auth/forgot-password`.
- `frontend/src/app/features/trader/trader-shell.ts` — top warning banner when `mustChangePassword()`.

### 8.4 Frontend — Admin subscription UI (PR-0e)

- `frontend/src/app/features/admin/admin-shell.ts` — sidebar expansion (mirror TraderShell pattern).
- `frontend/src/app/features/admin/admin.routes.ts` — add `subscriptions`, `subscriptions/:id`.
- `frontend/src/app/features/admin/admin.guard.ts` (new) — `auth.user()?.role === 'Admin'`.
- `frontend/src/app/core/api/billing-admin-api.service.ts` (new).
- `frontend/src/app/core/state/admin-subscriptions.state.ts` (new).
- `frontend/src/app/features/admin/subscriptions/subscriptions-list.page.ts` (new).
- `frontend/src/app/features/admin/subscriptions/subscription-detail.page.ts` (new).
- `frontend/src/app/app.routes.ts` — add `{ path: 'admin', canMatch: [adminGuard], loadChildren: … }`.

### 8.5 Files NOT touched (preserved)

- All `features/public/**` files.
- All `features/auth/register/register.page.ts`.
- All `features/trader/{dashboard,trades,calendar,analytics,settings}/**` files.
- All `src/2.Modules/Trading/**` files.
- All `src/2.Modules/PublicPortal/**` files (empty scaffold).
- `docs/PROJECT-STATUS.md` (will be updated by a separate docs-only PR at the end of Wave 0).

---

## 9. Approaches considered (with resolved defaults)

### 9.1 Architecture ownership of Subscription

Decision: **Billing owns Subscription + Plan. Admin is a thin presentation layer.** See §4.1 comparison.

### 9.2 Email transport (security-critical)

| Approach | Pros | Cons | Status |
|---|---|---|---|
| Plaintext in Serilog (dev) | Simple | **Credentials in log files — prohibited** | **REJECTED** |
| **`MailKitSmtpEmailSender` (prod via config) + `MailpitSmtpEmailSender` (dev container) + `InMemoryCapturingEmailSender` (tests)** | Real SMTP, captured-in-Mailpit UI for dev, deterministic capture for tests | MailKit dep + Mailpit container | **CHOSEN** |
| `SmtpClient` from `System.Net.Mail` | No new dep | Obsolete | Rejected |

**Default resolved**: MailKit SMTP via configuration; Mailpit in dev; in-memory for tests. No plaintext in logs anywhere.

### 9.3 Password history storage

**Default resolved**: separate `identity.password_history` table, last 5 hashes per user, indexed `(user_id, created_at DESC)`. Insert path enforces `Take(5)` after `OrderByDescending(CreatedAt)`.

### 9.4 Temporary password properties

**Default resolved**: 16-char Crockford Base32 (excludes `0OIl1`), single-use + 24h expiry (whichever first), 128-bit entropy, generated by `RandomNumberGenerator`.

### 9.5 Tokens during must-change window

**Default resolved**: issue short-TTL access token (5 min) + single-use refresh token. On successful password change, revoke ALL refresh tokens and re-issue one. Trader shell uses the short-TTL token only to render the forced-change page.

### 9.6 Rate-limit policies

**Default resolved**:
- `auth-strict` (existing, 10/min/IP): login, register, refresh, logout.
- `auth-forgot` (new, 5/hour/IP): forgot-password, change-password.

### 9.7 IEmailSender placement

**Default resolved**: `JadeCapital.Shared.Infrastructure.Abstractions.IEmailSender` (cross-cutting; reusable for 2FA/email-verification/alerts). Not inside Identity (avoids coupling credential-lifecycle bounded context to a delivery mechanism).

### 9.8 Trader portal module ownership

**Default resolved**: Journal / Risk / Strategies / Alerts / Trade Planner / Economic Calendar / Pattern Scanner live in the Trading bounded context under the `trading` schema. MarketData and Scanner get their own bounded contexts only where they need provider abstractions consumed across modules.

### 9.9 Module composition in Host

**Default resolved**: continue hand-wired composition (`AddBillingInfrastructure`, `MapAdminEndpoints`). No `IModule` interface in this slice — that is its own deferred ADR.

### 9.10 PublicPortal pricing

**Default resolved**: PublicPortal's `PLANS` const stays as marketing copy. It is **not** authoritative. Billing owns the authoritative plan catalog. PublicPortal is untouched.

---

## 10. Roadmap Summary (coherent, autonomous slices)

| Wave | SDD change | PRs | Total LOC | Dependencies |
|---|---|---|---|---|
| 0 | `jade-trader-os-core-portals` (this artifact) | 0a / 0b / 0c / 0d / 0e (5 PRs) | ~1,925 | None |
| 1 | `jade-trader-os-trader-extensions-1` | 1a / 1b / 1c / 1d (4 PRs) | ~1,540 | Wave 0 |
| 2 | `jade-trader-os-journal-risk` | 2a / 2b (2 PRs) | ~790 | Wave 0 |
| 3 | `jade-trader-os-strategies-alerts-planner` | 3a / 3b / 3c (3 PRs) | ~1,175 | Wave 0 |
| 4 | `jade-trader-os-scanner-marketdata` | 4a / 4b / 4c (3 PRs) | ~1,175 | Wave 0 + 2 (Scanner consumes Risk status) |
| 5 | `jade-trader-os-imports-ai` | 5a / 5b (2 PRs) | ~775 | Wave 0 |

Each PR ≤400 LOC. Each Wave is one SDD change. Each change is independently archivable.

---

## 11. Risks (consistent, no contradictions)

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Plaintext temp password leaked to logs | Low (controls in design) | Critical (credential compromise) | **No plaintext in Serilog anywhere.** Email capture transport is the only path that ever sees the temp password; `IEmailSender` implementations must not log bodies; `InMemoryCapturingEmailSender` exposes the list to tests only. |
| Email enumeration via timing | Medium | Medium | `ForgotPasswordHandler` returns `Success` in constant time regardless of email existence; `auth-forgot` rate-limit (5/hour/IP). |
| Race: user requests forgot-password twice in <1s | Low | Medium | `User.IssueTemporaryPassword` revokes prior temp hash on every new issuance; only one active temp per user. |
| User forgets temp password email | Medium | Low | Re-request invalidates prior temp. No account lock; max 5 failed-login attempts still applies per `MaxFailedLoginAttempts=5`. |
| Password history grows unboundedly | Low | Low | Insert path enforces `Take(5)` after `OrderByDescending(CreatedAt)`. |
| Mailpit / SMTP unreachable in dev | Medium | Low | `InMemoryCapturingEmailSender` for tests. Dev startup asserts Mailpit container reachable; otherwise clear error. |
| Trader shell regression when must-change guard activates | Medium | High | `mustChangePasswordGuard` allows `/auth/change-password-required` always; integration test asserts Trader-shell nav works after change. |
| Admin endpoint accessed by trader | Medium | High | `RequireAuthorization("AdminOnly")` at endpoint; integration test asserts 403 with trader token. |
| Billing owned subscription collides with future Stripe activation | Low | Medium | Billing schema is `billing.*` from day one; Stripe slot lives in Billing.Infrastructure. ADR notes the boundary. |
| Chained PR drift across slices | Low | High | Each PR runs full `dotnet test` (test_command unchanged). |
| `docs/PROJECT-STATUS.md` already stale | High | Low | Separate docs-only PR after Wave 0 closes. |
| Trader portal scope creep reintroduces broker execution | Low | Critical | Master Prompt §1 is non-negotiable; explicit guard in each Wave's tasks; ADR-0005 (V1 no broker execution) when introduced. |
| Pattern Score mis-presented as "probability of success" | Low (already guarded by MP §20) | Medium | Frontend must display "Pattern Score" (not "win probability"); spec forbids probability phrasing; tests assert UI copy. |
| AI Copilot invented metrics | Low (MP §30 forbids) | High | `ITradingInsightService` interface MUST source numbers via injected query services; tests assert refusals on out-of-scope questions. |

---

## 12. Genuine Open Product Decisions (the only ones surfaced to user)

Surfacing only items that are NOT safely defaulted by security/architecture best-practice. The rest are resolved in §9.

1. **Reset-email copy and branding** (Spanish wording, security phrasing, link expiration language) — copy decision, not architectural.
2. **Email locale** (Spanish only for V1, or also English) — marketing decision.
3. **Temp-password failed attempts and lockout** — count against the existing `MaxFailedLoginAttempts=5` (recommended), or use a separate counter for temp passwords only? Affects UX in brute-force scenario.
4. **Admin-triggered password reset for users** — useful for support, raises social-engineering risk. Recommendation: defer to a future SDD change; not in Wave 0.

---

## 13. First Implementable Change (the proposal-ready slice)

**The First SDD change is `jade-trader-os-core-portals`** (Wave 0). It is the smallest change that:

- Establishes the password recovery foundation everything else depends on (5 PRs).
- Establishes the Billing subscription domain that Wave 1's "subscription status in Dashboard / Risk" needs (PR-0d).
- Establishes the Admin presentation layer that Wave 1's subscription analytics needs (PR-0e).

It is autonomous: each of its 5 PRs has a clear start, finish, verification, and rollback story. It does not depend on any future Wave. It survives a partial rollback (e.g., dropping PR-0e leaves the admin shell as it was today with no regression).

`/sdd-propose` should target this change. The proposal carries forward:

- **Affected-area list** (§8) with concrete paths/symbols.
- **Resolved approaches** (§9).
- **Resolved defaults** (§7.2).
- **Rollout plan** (§10 Wave 0 row + §5.1 PR table).
- **Rollback story per PR** (each PR is independently revertible without breaking later PRs).
- **Auth, Admin, and Trader-shell regression protection** is explicit in §8.5.

Subsequent SDD changes (`jade-trader-os-trader-extensions-1`, `jade-trader-os-journal-risk`, etc.) follow as separate `/sdd-propose` calls in their respective waves.
