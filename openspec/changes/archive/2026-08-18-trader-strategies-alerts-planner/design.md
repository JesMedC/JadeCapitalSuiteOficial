# Design — Wave 3 (Trader Strategies + Alerts + Planner)

## Architecture Overview

Wave 3 adds three independent capabilities on top of Wave 2's journal + behavioral layer: **Strategies** (catalog + analytics), **Alerts** (rule-based background evaluation), and **Planner** (weekly cadence). Each lives in its own bounded context within `Trading.Application` but shares the user scoping pattern with the rest of the module.

The new domain entities live in `Trading.Domain/Strategies/`, `Trading.Domain/Alerts/`, and `Trading.Domain/Planner/`. The application layer adds handlers in `Trading.Application/Features/Strategies/`, `Trading.Application/Features/Alerts/`, and `Trading.Application/Features/Planner/`. The infrastructure layer extends `TradingDbContext` with three new tables and adds a `BackgroundService` for alert evaluation. The API layer adds three endpoint groups. The frontend adds three new pages.

The alert rule registry is a **shared infrastructure** pattern (same shape as `ICoachingRule`), but `IAlertRule` lives in `Shared.Kernel` because the wire-shape `Alert` record is cross-module-stable. `ICoachingRule` stays in `Trading.Application` because its context is Trading-specific.

## New Aggregates

### `Strategy` (Trading.Domain/Strategies/Strategy.cs)

```csharp
public class Strategy : AggregateRoot<Guid>
{
    public const int MaxNameLength = 64;
    public const int MaxDescriptionLength = 1000;
    public const int MaxRulesLength = 2000;

    public UserId UserId { get; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public Symbol? Symbol { get; private set; }      // nullable
    public Timeframe? Timeframe { get; private set; } // nullable
    public string? Rules { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<Strategy> Create(...);
    public Result Update(...);
    public Result SoftDelete();
    public Result Activate();
}
```

**Invariants**:
- Name trimmed, 1..MaxNameLength chars.
- Description ≤ MaxDescriptionLength.
- Rules ≤ MaxRulesLength.
- Symbol must exist in `trading.instruments` if non-null (validated in handler, not domain).
- Timeframe must be a valid enum value if non-null.
- Soft-delete sets `IsActive = false`; can be re-activated via `Activate()`.

**Domain Events**:
- `StrategyCreatedDomainEvent(StrategyId, UserId, At)`
- `StrategyUpdatedDomainEvent(StrategyId, At)`
- `StrategySoftDeletedDomainEvent(StrategyId, At)`

### `Trade.StrategyId` (additive)

```csharp
public class Trade : AggregateRoot<Guid>
{
    // ... existing fields ...
    public Guid? StrategyId { get; private set; }  // NEW

    public Result SetStrategy(Guid? strategyId, IClock clock);  // NEW (or use UpdateMetadata)
}
```

The new field is **additive only**: no domain behavior change, no migration of existing data. Open trades and Closed trades both accept retro-tagging. The `SetStrategy` method validates:
- `strategyId == null` → untag (set to null).
- `strategyId != Guid.Empty` → set. (Existence check in handler via repository.)

We add it as a dedicated method (not via `UpdateMetadata`) to keep the API surface explicit and to allow future evolution (e.g. history tracking).

### `Alert` (Trading.Domain/Alerts/Alert.cs)

```csharp
public class Alert : AggregateRoot<Guid>
{
    public UserId UserId { get; }
    public string RuleId { get; private set; } = default!;
    public AlertSeverity Severity { get; private set; }
    public string Title { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public string? CtaRoute { get; private set; }
    public string? CtaLabel { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; }

    public static Result<Alert> Create(
        UserId userId, string ruleId, AlertSeverity severity,
        string title, string body, string? ctaRoute, string? ctaLabel,
        DateTimeOffset? expiresAt, IClock clock);

    public Result Acknowledge(IClock clock);
}
```

**Wire shape** (cross-module, in Shared.Kernel):
```csharp
public sealed record AlertWire(
    Guid Id,
    string RuleId,
    AlertSeverity Severity,
    string Title,
    string Body,
    string? CtaRoute,
    string? CtaLabel,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt);
```

**Invariants**:
- Title ≤ 120 chars, body ≤ 500 chars.
- Severity ∈ {Low, Medium, High}.
- Acknowledge is idempotent (calling twice doesn't change `AcknowledgedAt`).
- `RuleId` is the canonical identifier (matches `ICoachingRule.RuleId` style).

### `PlannerSession` (Trading.Domain/Planner/PlannerSession.cs)

```csharp
public class PlannerSession : AggregateRoot<Guid>
{
    public const int MaxNotesLength = 500;

    public UserId UserId { get; }
    public LocalDate Date { get; }
    public TimeOnly? PlannedStartTime { get; private set; }
    public TimeOnly? PlannedEndTime { get; private set; }
    public Symbol? Symbol { get; private set; }     // nullable
    public PlannerStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<PlannerSession> Create(...);
    public Result UpdateStatus(PlannerStatus status, IClock clock);
    public Result UpdateTimes(TimeOnly? start, TimeOnly? end);
    public Result UpdateNotes(string? notes);
}
```

**Invariants**:
- `Date` ∈ [1900-01-01, 2100-12-31].
- `PlannedEndTime > PlannedStartTime` if both set.
- `Notes` ≤ 500 chars.
- `Symbol` nullable.
- `Status` ∈ {Planned, Completed, Skipped, Cancelled}.

## New Value Objects

### `Timeframe` (Shared.Kernel/Enums/Timeframe.cs)

```csharp
public enum Timeframe : byte
{
    Unspecified = 0,
    M1  = 1,   // 1-minute
    M5  = 2,
    M15 = 3,
    M30 = 4,
    H1  = 5,   // 1-hour
    H4  = 6,
    D1  = 7,   // daily
    W1  = 8,   // weekly
    MN  = 9,   // monthly
}
```

Stored as `SMALLINT` (mapped to byte via value converter). The `Unspecified = 0` allows nullable semantics on the DB column.

### `AlertSeverity` (Shared.Kernel/Coaching/AlertSeverity.cs)

```csharp
public enum AlertSeverity : byte
{
    Low = 1,
    Medium = 2,
    High = 3,
}
```

Identical numeric ordinals to `Coaching.Severity`. Kept as a distinct type to avoid cross-module enum coupling (same rule as `CoachingModels.cs`).

### `PlannerStatus` (Shared.Kernel/Enums/PlannerStatus.cs)

```csharp
public enum PlannerStatus : byte
{
    Planned   = 0,
    Completed = 1,
    Skipped   = 2,
    Cancelled = 3,
}
```

### `IAlertRule` (Shared.Kernel/Alerts/IAlertRule.cs)

```csharp
public interface IAlertRule
{
    string RuleId { get; }
    int Priority { get; }
    AlertSeverity DefaultSeverity { get; }
    IReadOnlyList<Alert> Evaluate(AlertContext ctx);
}

public sealed record AlertContext(
    Guid UserId,
    DateTimeOffset Now,
    IReadOnlyList<Trade> ClosedTrades,
    IReadOnlyList<Trade> OpenTrades,
    IReadOnlyList<JournalEntry> Journals);
```

**Why Shared.Kernel**: `Alert` is the wire shape consumed both by Trading and (future) Admin. The Application layer references `IAlertRule` instances registered as singletons in `Shared.Infrastructure`.

### `AlertRegistry` (Trading.Application/Alerts/AlertRegistry.cs)

```csharp
public sealed class AlertRegistry
{
    private readonly IReadOnlyList<IAlertRule> _rules;

    public AlertRegistry(IEnumerable<IAlertRule> rules)
    {
        _rules = rules.OrderBy(r => r.Priority).ToList();
    }

    public IReadOnlyList<Alert> EvaluateForUser(AlertContext ctx)
    {
        var collected = new List<Alert>();
        foreach (var rule in _rules)
        {
            try
            {
                collected.AddRange(rule.Evaluate(ctx));
            }
            catch (Exception ex)
            {
                // Log + continue; one rule failing must not break the batch.
            }
        }
        return collected;
    }
}
```

Same shape as `CoachingRuleRegistry` (Wave 2d.1). Try/catch around each rule for resilience (the BackgroundService iterates many users; one bad rule must not crash the host).

## Application Layer

### Strategy handlers (`Trading.Application/Features/Strategies/`)
- `CreateStrategyHandler` (command + handler): validates name uniqueness via `IStrategyRepository.ExistsByNameAsync(userId, name)`, persists.
- `UpdateStrategyHandler`: validates name uniqueness if changed, persists.
- `SoftDeleteStrategyHandler`: sets `IsActive = false`.
- `ReactivateStrategyHandler`: sets `IsActive = true` (admin-style).
- `GetStrategiesHandler` (query): returns active strategies paginated.
- `GetStrategyByIdHandler`: returns single strategy.
- `GetStrategyAnalyticsHandler`: queries `ITradeRepository` for closed trades with `strategy_id = X`, computes aggregate.
- `SetTradeStrategyHandler` (command, in `TradeStrategies/`): updates `Trade.StrategyId`, validates strategy belongs to same user.

### Alert handlers (`Trading.Application/Features/Alerts/`)
- `GetAlertsHandler` (query): returns alerts (filtered by `activeOnly`).
- `GetAlertByIdHandler`: returns single.
- `AcknowledgeAlertHandler`: sets `AcknowledgedAt`.

### Alert background evaluation (`Trading.Application/Features/Alerts/Internal/`)
- `EvaluateAlertsForUserHandler` (internal): builds `AlertContext` from repos, calls `AlertRegistry`, persists new alerts (with dedup via `ON CONFLICT DO NOTHING`).

### Planner handlers (`Trading.Application/Features/Planner/`)
- `CreatePlannerSessionHandler`: validates + persists.
- `UpdatePlannerSessionHandler`: updates status/times/notes.
- `GetPlannerSessionsByWeekHandler`: ISO week range query + JOIN with closed trades for comparison.

## Infrastructure Layer

### EF Configuration

**`StrategyConfiguration`** (NEW):
```csharp
internal sealed class StrategyConfiguration : IEntityTypeConfiguration<Strategy>
{
    public void Configure(EntityTypeBuilder<Strategy> b)
    {
        b.ToTable("strategies");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.UserId).HasColumnName("user_id").IsRequired();

        b.Property(s => s.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        b.Property(s => s.Description).HasColumnName("description").HasMaxLength(1000);
        b.Property(s => s.Rules).HasColumnName("rules").HasColumnType("text");

        b.Property(s => s.Symbol).HasColumnName("symbol").HasMaxLength(20)
            .HasConversion(new SymbolConverter(), new SymbolComparer());
        b.Property(s => s.Timeframe).HasColumnName("timeframe").HasConversion<byte?>();
        b.Property(s => s.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();

        b.Ignore(s => s.DomainEvents);

        b.HasIndex(s => new { s.UserId, s.Name })
            .HasDatabaseName("ix_strategies_user_name");

        b.HasIndex(s => s.Id).HasDatabaseName("ix_strategies_user_active")
            .HasFilter("is_active = true");
    }
}
```

**`AlertConfiguration`** (NEW): similar to `JournalEntryConfiguration`. `Body` mapped to `VARCHAR(500)`. Severity as `SHORT`. Indexes `ix_alerts_user_active` (acknowledged_at IS NULL) and `ix_alerts_user_all`.

**`PlannerSessionConfiguration`** (NEW): `Date` as DATE via `LocalDate` converter. `Time` types as TIME (Npgsql native). `Status` as SMALLINT.

**`TradeConfiguration`** (modified):
```csharp
// NEW in slice 3a:
b.Property(t => t.StrategyId).HasColumnName("strategy_id").IsRequired(false);
b.HasOne<Strategy>().WithMany().HasForeignKey(t => t.StrategyId)
    .OnDelete(DeleteBehavior.SetNull);
b.HasIndex(t => new { t.UserId, t.StrategyId })
    .HasDatabaseName("ix_trades_user_strategy")
    .HasFilter("strategy_id IS NOT NULL");
```

### `AlertEvaluationBackgroundService` (NEW)

```csharp
public sealed class AlertEvaluationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _period = TimeSpan.FromMinutes(5);
    private readonly Random _jitter = new();

    public AlertEvaluationBackgroundService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial jitter (0..30s) to avoid thundering herd at startup.
        await Task.Delay(_jitter.Next(0, 30_000), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var evaluator = scope.ServiceProvider.GetRequiredService<EvaluateAlertsForAllUsersHandler>();
                await evaluator.RunAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Log; never let the background crash.
            }

            await Task.Delay(_period + TimeSpan.FromSeconds(_jitter.Next(-30, 30)), stoppingToken);
        }
    }
}
```

Iteration: load all `UserId` from `identity.users` (with `IsActive = true`), then for each user spawn a separate scope, build `AlertContext`, call `AlertRegistry.EvaluateForUser`, persist new alerts.

### DI Registration (`TradingModuleRegistration`)

```csharp
// Wave 3 additions:
services.AddScoped<IStrategyRepository, StrategyRepository>();
services.AddScoped<IAlertRepository, AlertRepository>();
services.AddScoped<IPlannerSessionRepository, PlannerSessionRepository>();

// Alert registry (singleton):
services.AddSingleton<IAlertRule, NoTradesInDaysRule>();
services.AddSingleton<IAlertRule, DrawdownExceededRule>();
services.AddSingleton<IAlertRule, RRAverageBelowRule>();
services.AddSingleton<IAlertRule, CurrentPriceNearStopRule>();
services.AddSingleton<IAlertRule, OpenTradeOffPlanRule>();
services.AddSingleton<AlertRegistry>();

// Background service:
services.AddHostedService<AlertEvaluationBackgroundService>();
```

## API Endpoints

### Strategies (`MapStrategiesEndpoints`)
- `GET /api/strategies?page=1&pageSize=50` → `StrategyDto[]`.
- `GET /api/strategies/{id}` → `StrategyDto`.
- `POST /api/strategies` → 201 + `StrategyDto`.
- `PATCH /api/strategies/{id}` → `StrategyDto`.
- `DELETE /api/strategies/{id}` → 204.
- `GET /api/strategies/{id}/analytics` → `StrategyAnalyticsDto`.
- `PATCH /api/trades/{tradeId}/strategy` (in `MapTradeStrategyEndpoints` or co-located) → `TradeDto`.

### Alerts (`MapAlertsEndpoints`)
- `GET /api/alerts?activeOnly=true` → `AlertDto[]`.
- `GET /api/alerts/{id}` → `AlertDto`.
- `PATCH /api/alerts/{id}/ack` → `AlertDto`.

### Planner (`MapPlannerEndpoints`)
- `GET /api/planner?week=YYYY-Www` → `PlannerSessionDto[]` (with `comparison` payload).
- `POST /api/planner` → 201 + `PlannerSessionDto`.
- `PATCH /api/planner/{id}` → `PlannerSessionDto`.

All: `RequireAuthorization`, `api-general` rate limit.

## Frontend Layer

### New files

**Strategies** (`frontend/src/app/features/trader/strategies/`):
- `strategies-page.ts` — list + create/edit form + analytics panel inline (standalone, Signals, OnPush, SCSS).
- `strategies.routes.ts` — sub-routes (`/strategies`, `/strategies/:id`).
- `state/strategies.state.ts` — Signal-based store.
- `api/strategies.service.ts` — HTTP wrapper (8 methods).
- `__tests__/strategies-page.spec.ts` — 4 specs.

**Alerts** (`frontend/src/app/features/trader/alerts/`):
- `alerts-page.ts` — list of active alerts + ack button + severity badges.
- `alerts.routes.ts`.
- `state/alerts.state.ts`.
- `api/alerts.service.ts`.
- `__tests__/alerts-page.spec.ts` — 3 specs.

**Planner** (`frontend/src/app/features/trader/planner/`):
- `planner-page.ts` — weekly grid with sessions planned vs actual trades.
- `planner.routes.ts`.
- `state/planner.state.ts` (with week-navigation logic).
- `api/planner.service.ts`.
- `__tests__/planner-page.spec.ts` — 3 specs.

### Modified

- `frontend/src/app/features/trader/trader.routes.ts` — add 3 lazy routes.
- `frontend/src/app/features/trader/trader-shell.ts` — **nav update decision** (see below).

### Nav Update Decision

**The problem**: After Wave 3, the trader has 8 candidate nav items:
1. Dashboard
2. Trades
3. Strategies (NEW)
4. Journal
5. Patterns
6. Planner (NEW)
7. Alerts (NEW)
8. Settings

But `jcs-mobile-nav` (Wave 1.5) supports 5 items max in the bottom bar on mobile.

**Options considered**:
- **(A) Replace one of the existing 5 with a new entry.** Risk: lose a feature the trader uses.
- **(B) Add a 6th item with overflow drawer.** Adds complexity to mobile-first design.
- **(C) Use a top-tab bar on specific pages** (e.g. Patterns page has tabs for "Patterns / Strategies / Planner / Alerts"). Pro: each page is self-contained. Con: navigation requires extra tap.
- **(D) Reduce to 5 core items + a "Tools" overflow menu.** Pro: bottom-nav stays clean. Con: extra tap for overflowed items.

**Decision**: **Option (D)**, the Wave 1.5 precedent. The 5 bottom-nav items stay:
1. Dashboard
2. Trades
3. Diario (Journal)
4. Patrones (Patterns)
5. Settings

Strategies, Planner, and Alerts become accessible from:
- A "Más" (/app/settings or a dedicated /app/tools route) entry from Settings.
- LinkCards inside the relevant parent pages (e.g. Dashboard shows "Tenés X alertas activas →" linking to /app/alerts; Patterns page shows "Ver strategies" linking to /app/strategies).
- The `trader-shell` topbar adds a secondary context menu with the 3 new entries (visible on tablet+).

**Rationale**: mobile-first UX favors 5 bottom items (Wave 1.5 decision). The overflow mechanism (Settings → Tools sub-page) is a 1-tap cost. Wave 4 may introduce a more sophisticated mega-menu or a `material-top-app-bar` with a drawer.

## Cross-Module Concerns

- **Timezone resolution**: `PlannerSession` uses `LocalDate` (Wave 2a) for the date. `Time` columns are timezone-naive (the user means "10:00 local"). The handler renders them in `X-User-Timezone` (header pattern from Wave 2a).
- **PII redaction**: Alert copy uses qualitative language only. Strategy analytics DO expose absolute P&L (the trader owns their own data) — this is exception, not normal. Documented in the strategy spec.
- **No Identity.Domain coupling**: All `UserId` resolution goes through the same cross-module pattern as Wave 1 (read from JWT claim `NameIdentifier`).
- **Background service isolation**: The BackgroundService creates a fresh scope per user iteration to avoid cross-user contamination. Errors in one user's evaluation MUST NOT block the next.

## Migration Sequencing

```
migration 0015_strategies_and_friends.sql (single file, all 3 tables + 1 FK)
  --
  -- trading.strategies (NEW)
  -- trading.alerts (NEW)
  -- trading.planner_sessions (NEW)
  -- trading.trades.strategy_id (NEW FK nullable, ADDITIVE)
  --
  BEGIN;
  CREATE TABLE IF NOT EXISTS trading.strategies (...);
  CREATE INDEX IF NOT EXISTS ux_strategies_user_name_active ON trading.strategies (user_id, lower(name)) WHERE is_active = true;
  CREATE TABLE IF NOT EXISTS trading.alerts (...);
  CREATE UNIQUE INDEX IF NOT EXISTS ux_alerts_user_rule_day ON trading.alerts (user_id, rule_id, ((created_at AT TIME ZONE 'UTC')::date));
  CREATE TABLE IF NOT EXISTS trading.planner_sessions (...);
  ALTER TABLE trading.trades ADD COLUMN IF NOT EXISTS strategy_id UUID NULL;
  ALTER TABLE trading.trades ADD CONSTRAINT fk_trades_strategy FOREIGN KEY (strategy_id) REFERENCES trading.strategies(id) ON DELETE SET NULL;
  CREATE INDEX IF NOT EXISTS ix_trades_user_strategy ON trading.trades (user_id, strategy_id) WHERE strategy_id IS NOT NULL;
  COMMIT;
```

**Single migration file** (0015) for all 3 tables + 1 FK. Rationale: 3 tables are atomic — releases together. Future Wave 4+ can add migration 0016+ independently. **Alternative**: 3 separate migrations (0015 strategies, 0016 alerts, 0017 planner) — chosen only if each slice ships independently across weeks. For Wave 3 chained delivery, single file is cleaner.

## DI Composition Impact (Program.cs)

```csharp
// Wave 3 additions:
app.MapStrategiesEndpoints();
app.MapAlertsEndpoints();
app.MapPlannerEndpoints();
app.MapTradeStrategyEndpoint();  // PATCH /api/trades/{id}/strategy

// HostedService:
builder.Services.AddHostedService<AlertEvaluationBackgroundService>();
```

These run after `MapCoachingPromptsEndpoint()` and before `MapTradeMfeMaeEndpoint()` for visual ordering in `Program.cs`.

## Sequence Diagram (BackgroundService iteration)

```
Timer fires (5 min + jitter)
  ↓
AlertEvaluationBackgroundService.ExecuteAsync
  ↓
CreateAsyncScope
  ↓
EvaluateAlertsForAllUsersHandler.RunAsync
  ↓
For each user in identity.users (IsActive = true):
  ↓
  Create nested scope
  ↓
  Load ClosedTrades + OpenTrades + Journals via repos
  ↓
  Build AlertContext
  ↓
  AlertRegistry.EvaluateForUser(ctx)
  ↓
  For each new Alert:
    INSERT INTO trading.alerts (...) ON CONFLICT (user_id, rule_id, date) DO NOTHING;
  ↓
Dispose scopes
  ↓
Sleep until next iteration
```

Key invariants:
- New scope per user (no cross-user blobs).
- `ON CONFLICT DO NOTHING` makes the dedup idempotent.
- If a single rule throws, the rest of the rules for that user still run (try/catch in `AlertRegistry`).
- If a single user fails, the next user still runs (try/catch in handler loop).

## Key Files to Be Created (slice breakdown)

**Slice 3a (Strategies)**:
- Backend: `Trading.Domain/Strategies/Strategy.cs`, `Trading.Application/Features/Strategies/*` (8 handlers), `Persistence/Configurations/StrategyConfiguration.cs`, `StrategyRepository.cs`, `005_strategies_aggregate.cs`, `Persistence/Repositories/StrategyRepository.cs`, `Api/Endpoints/StrategyEndpoints.cs`, `Api/Endpoints/TradeStrategyEndpoint.cs`.
- Modify: `Trade.cs` (add `StrategyId`), `TradeConfiguration.cs` (add mapping), `TradingDbContext.cs` (add DbSet).
- Frontend: `strategies/strategies-page.ts`, `strategies.service.ts`, `strategies.state.ts`, `__tests__/strategies-page.spec.ts` (4 specs).

**Slice 3b (Alerts)**:
- Backend: `Shared.Kernel/Coaching/AlertSeverity.cs`, `Shared.Kernel/Alerts/IAlertRule.cs`, `Shared.Kernel/Alerts/AlertContext.cs`, `Shared.Kernel/Alerts/AlertWire.cs`, `Trading.Domain/Alerts/Alert.cs`, `Trading.Application/Alerts/AlertRegistry.cs`, `Trading.Application/Features/Alerts/*` (4 handlers), `Trading.Application/Features/Alerts/Rules/*` (5 rules), `AlertEvaluationBackgroundService.cs`, `AlertConfiguration.cs`, `AlertRepository.cs`, `AlertEndpoints.cs`.
- Frontend: `alerts/alerts-page.ts`, `alerts.service.ts`, `alerts.state.ts`, `__tests__/alerts-page.spec.ts` (3 specs).

**Slice 3c (Planner)**:
- Backend: `Shared.Kernel/Enums/PlannerStatus.cs`, `Trading.Domain/Planner/PlannerSession.cs`, `Trading.Application/Features/Planner/*` (3 handlers), `PlannerSessionConfiguration.cs`, `PlannerSessionRepository.cs`, `PlannerEndpoints.cs`.
- Frontend: `planner/planner-page.ts`, `planner.service.ts`, `planner.state.ts`, `__tests__/planner-page.spec.ts` (3 specs).

**Slice 3d (E2E)**:
- `trader-shell.ts` (nav update + "Tools" sub-page).
- `trader.routes.ts` (3 lazy routes).
- Smoke E2E from Tailscale.
