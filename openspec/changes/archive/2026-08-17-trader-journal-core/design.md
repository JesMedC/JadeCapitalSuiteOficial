# Design — Wave 2 (Trader Journal Core)

## Architecture Overview

Wave 2 adds a journal layer on top of Wave 1's risk-aware trader. The new domain entities live in `Trading.Domain/Journal/` and `Trading.Domain/Behavioral/`. The application layer adds handlers in `Trading.Application/Features/Journal/` and `Trading.Application/Features/Behavioral/`. The infrastructure layer extends `TradingDbContext` with new tables and EF configurations. The API layer adds three endpoint groups. The frontend adds `/app/journal` and a patterns/coaching section on the dashboard.

The coaching rule registry is a **shared infrastructure** pattern (same shape as `IClock`, `IEmailSender`). New rules can be added without touching the registry consumer.

## New Aggregates

### `JournalEntry` (Trading.Domain/Journal/JournalEntry.cs)

```csharp
public class JournalEntry : AggregateRoot<Guid>
{
    public UserId UserId { get; }
    public LocalDate Date { get; }
    public string Timezone { get; }
    public Mood? MoodPre { get; private set; }
    public Mood? MoodDuring { get; private set; }
    public Mood? MoodPost { get; private set; }
    public string? PremarketPlan { get; private set; }
    public string? PostmarketReflection { get; private set; }
    public IReadOnlyList<string> Tags { get; }

    public static Result<JournalEntry> CreateOrUpdate(
        UserId userId,
        LocalDate date,
        string timezone,
        Mood? moodPre, Mood? moodDuring, Mood? moodPost,
        string? premarketPlan, string? postmarketReflection,
        IReadOnlyList<string>? tags,
        IClock clock);
}
```

**Invariants**:
- At most one entry per `(user_id, local_date)`.
- `Mood` values ∈ [1, 5].
- `PremarketPlan` length ≤ 2000 chars; `PostmarketReflection` length ≤ 5000 chars.
- `Tags` array length ≤ 10, each tag ≤ 32 chars.

**Domain Events**:
- `JournalEntryCreatedDomainEvent(EntryId, UserId, Date, At)`
- `JournalEntryUpdatedDomainEvent(EntryId, UserId, At)`

### `BehavioralEvent` (Trading.Domain/Behavioral/BehavioralEvent.cs)

`BehavioralEvent` is a **value object** (record), not an aggregate root — events are computed on read in Wave 2.

```csharp
public enum Severity { Low, Medium, High }

public sealed record BehavioralEvent(
    string RuleId,
    Severity Severity,
    IReadOnlyList<Guid> TradeIds,
    DateTimeOffset OccurredAt,
    string Message);

public sealed record BehavioralAggregations(
    IReadOnlyDictionary<string, EmotionalityBucket> ByEmotionality);

public sealed record EmotionalityBucket(
    int Count, decimal WinRate, decimal TotalPnl);
```

### `ICoachingRule` (Shared.Kernel/Coaching/ICoachingRule.cs)

```csharp
public interface ICoachingRule
{
    string RuleId { get; }
    int Priority { get; }
    IReadOnlyList<CoachingPrompt> Evaluate(CoachingContext ctx);
}

public sealed record CoachingPrompt(
    string RuleId,
    Severity Severity,
    string Title,
    string Body,
    Cta Cta,
    DateTimeOffset OccurredAt);

public sealed record Cta(string Route, string Label);

public sealed record CoachingContext(
    Guid UserId,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    IReadOnlyList<Trade> Trades,
    IReadOnlyList<JournalEntry> Journals);
```

## New Value Objects

### `Mood` (Trading.Domain/Journal/Mood.cs)

```csharp
public readonly record struct Mood
{
    public byte Value { get; }     // 1..5
    public static Result<Mood> Create(byte value);
    public static Mood FromTrusted(byte value);  // for hydration
    public string Label => Value switch {
        1 => "Fearful", 2 => "Anxious", 3 => "Neutral", 4 => "Confident", 5 => "Euphoric",
        _ => "Unknown" };
}
```

### `LocalDate` (Shared.Kernel/Time/LocalDate.cs)

```csharp
public readonly record struct LocalDate
{
    public int Year { get; }
    public int Month { get; }
    public int Day { get; }
    public static LocalDate From(DateOnly date);
    public static LocalDate From(DateTimeOffset utc, string ianaTimezone);
    public string Iso8601 => $"{Year:0000}-{Month:00}-{Day:00}";
}
```

## Application Layer

### Journal handlers
- `CreateOrUpdateJournalEntryHandler` (command + handler): upserts today's entry. Resolves `LocalDate` from `X-User-Timezone` header.
- `GetTodayJournalEntryQuery` (query + handler): returns today's entry or null.
- `GetJournalEntriesByRangeQuery` (query + handler): returns entries in `[from, to]`.
- `DeleteJournalEntryHandler`: hard delete.

### Behavioral handlers
- `GetBehavioralAnalyticsQuery` (query + handler): returns events + aggregations for `period`.

### Coaching handlers
- `GetCoachingPromptsQuery` (query + handler): iterates `IEnumerable<ICoachingRule>`, applies each rule, aggregates + sorts by severity desc + occurredAt desc.

## Infrastructure Layer

### EF Configuration
- `trading.journal_entries`: `OwnsOne` for `Mood` VO; `tags` mapped as `TEXT[]` (Npgsql native); partial unique index `ux_journal_user_date`.
- `trading.trades`: add `mfe_amount`, `mae_amount`, `mfe_currency`, `mae_currency` columns (migration 0014).

### DI Registration (TradingModuleRegistration)
- `AddScoped<IJournalEntryRepository, JournalEntryRepository>`
- `AddScoped<IBehavioralAnalyzer, BehavioralAnalyzer>`
- `AddSingleton<ICoachingRule, RevengeTradeRule>` (5 singletons)
- `AddSingleton<ICoachingRule, OvertradingDayRule>`
- `AddSingleton<ICoachingRule, TiltSequenceRule>`
- `AddSingleton<ICoachingRule, LongBreakRule>`
- `AddSingleton<ICoachingRule, PreMarketPlanMissRule>`
- `AddSingleton<CoachingRuleRegistry>()` — exposes `Evaluate(CoachingContext) -> IReadOnlyList<CoachingPrompt>`.

## API Endpoints (all `RequireAuthorization`, `api-general` rate limit)

### Journal
- `GET /api/journal/today` → `JournalEntryDto | null` (404 if absent).
- `GET /api/journal?from=YYYY-MM-DD&to=YYYY-MM-DD` → `JournalEntryDto[]`.
- `POST /api/journal/today` → upsert, returns `JournalEntryDto`.
- `DELETE /api/journal/{id}` → 204.

### Behavioral
- `GET /api/trades/behavioral?period=7d|30d|90d|all` → `{ events: BehavioralEvent[], aggregations: BehavioralAggregations, period, windowStart, windowEnd }`.

### Coaching
- `GET /api/coaching/prompts?period=7d|30d|90d|all` → `{ prompts: CoachingPromptDto[], period }`.

### MFE/MAE
- `GET /api/trades/{tradeId}/mfe-mae` → `{ tradeId, direction, isWinner, mfeAmount, maeAmount, currency, aggregate: { mfe_long_winners, mfe_long_losers, mfe_short_winners, mfe_short_losers, mae_long_winners, ... } }`.

## Frontend Layer

### New files
- `frontend/src/app/features/trader/journal/journal-page.ts` — daily journal form (pre/post + mood × 3 + tags). Standalone, Signals, OnPush, SCSS. Uses `<jcs-mobile-nav>` pattern. Mobile-first.
- `frontend/src/app/features/trader/journal/journal.routes.ts` — sub-routes.
- `frontend/src/app/features/trader/journal/state/journal.state.ts` — Signal-based store.
- `frontend/src/app/features/trader/journal/api/journal.service.ts` — HTTP wrapper.
- `frontend/src/app/features/trader/journal/__tests__/journal-page.spec.ts` — 4 specs.
- `frontend/src/app/features/trader/patterns/patterns-page.ts` — behavioral events + aggregations display.
- `frontend/src/app/features/trader/patterns/__tests__/patterns-page.spec.ts` — 3 specs.
- `frontend/src/app/features/trader/coaching/coaching-prompts.component.ts` — re-usable in dashboard and journal pages.

### Modified
- `frontend/src/app/features/trader/trader-shell.ts` — add `Journal` and `Patterns` to `navItems` (5 items total).
- `frontend/src/app/features/trader/dashboard/dashboard.page.ts` — embed `<jcs-coaching-prompts>` at the top.

## Cross-Module Concerns

- **Timezone resolution**: `X-User-Timezone` HTTP header is read by a custom middleware or ASP.NET Core action filter. Default `UTC`. The handler passes timezone into `LocalDate.From(utc, ianaTimezone)`.
- **PII redaction**: Coaching prompt copy MUST NOT include raw P&L amounts. The PiiLogScrubber (Wave 0) covers logging; the prompt generation rule code never receives raw amounts and never includes them in output.
- **No Identity.Domain coupling**: All `UserId` resolution goes through the same cross-module pattern as Wave 1 (read from JWT claim `NameIdentifier`).

## Migration Sequencing

```
migration 0013 (journal-daily):  CREATE TABLE trading.journal_entries ...
migration 0014 (mfe-mae-charts): ALTER TABLE trading.trades ADD COLUMN mfe_amount ...
```

Both idempotent and additive. `migrate.Dockerfile` gets new `COPY + psql -f` lines following the pattern established in Wave 1 (with `\"` escapes).

## DI Composition Impact (Program.cs)

- `RegisterServicesFromAssemblies(typeof(AddJournalEntryHandler).Assembly)` — already covered by `RegisterServicesFromAssemblies(... typeof(GetTradingMetricsHandler).Assembly)` since both are in Trading.Application.
- `app.MapJournalEndpoints()`, `app.MapBehavioralEndpoints()`, `app.MapCoachingPromptsEndpoint()`, `app.MapTradeMfeMaeEndpoint()` — new lines after `MapTraderReviewEndpoints()`.
- `AddCoachingRuleRegistry()` — new line in `TradingModuleRegistration`.

## Sequence Diagram (per-trade MFE/MAE on close)

```
Trader closes trade -> CloseTradeHandler -> Trade.Close() appends TradeClosedDomainEvent
                                                ↓
                                AppendMfeMaeOnCloseHandler (new)
                                                ↓
                                Computes MFE/MAE approximation
                                                ↓
                                Updates trade.mfe_amount, mae_amount
                                in same UoW transaction
```

The MFE/MAE approximation runs in the same transaction as the close to avoid double-write race conditions.
