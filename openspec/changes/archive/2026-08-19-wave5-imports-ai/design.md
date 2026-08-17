# Design — Wave 5 (Imports + AI Coaching + AI Risk Advisor)

## Architecture Overview

Wave 5 introduces three orthogonal capabilities that close the data-import + intelligence loop:

1. **Importers** (5a) — pluggable row parsers (`IImportRowParser`) that read CSV / MT4 / MT5 trade history streams and persist into `trading.trades` with dedupe, batch transactions, and observable progress via `ImportJob` aggregate. The parsers live in Infrastructure; the streaming pipeline lives in Application.
2. **AI coaching via Ollama** (5b) — `IAIProvider` interface in Shared.Kernel, `OllamaHttpClient` impl in Infrastructure (HttpClient-based, configurable). A `CoachingPrompt` aggregate persists the prompt + raw response + latency. A `CoachingPromptService` BackgroundService wakes daily at 03:00 UTC and generates one narrative prompt per active user.
3. **AI risk advisor pre-trade** (5c) — `IAIRiskAdvisor` interface in Application, `OllamaAIRiskAdvisor` impl in Infrastructure. The advisor is **invoked from `OpenTradeHandler` BEFORE the trade is persisted**, returns a structured `{action: "allow"|"warning"|"block", reason}` JSON parsed from Ollama output. The advisor result is persisted alongside the `PreTradeChecklist` row.

The three capabilities share one cross-cutting concern:

- **Local Ollama only** — Wave 5 ships with one provider impl (`OllamaHttpClient`). Cloud providers (OpenAI, Claude, Gemini) land in Wave 6. The `IAIProvider` interface is the swap point; `OllamaHttpClient` is registered as singleton via DI and the rest of the system never references `OllamaHttpClient` directly.
- **Advisory is advisory, never gating** — the advisor is invoked, the result is persisted, but `OpenTradeHandler` uses the result to either (a) reject with 422 `ai_risk.blocked` if `action = "block"`, (b) attach the advisory to the persisted checklist if `action = "warning"`, or (c) silently proceed if `action = "allow"`. The FE shows a modal for warning/block scenarios. No advisor = no impact (legacy path).

The new domain entities live in `Trading.Domain/Imports/`, `Trading.Domain/Ai/`. The application layer adds handlers in `Trading.Application/Features/Imports/`, `Trading.Application/Features/Ai/`. Infrastructure extends `TradingDbContext` with three new tables, registers `IAIProvider`, and adds one BackgroundService. Host wires two endpoint groups (`/api/imports`, `/api/ai`, `/api/coaching/ai-prompts`). Frontend adds the imports page, the coaching tab extension, and the checklist advisory section.

## New Value Objects / Records (Shared.Kernel)

### `IAIProvider` (Shared.Kernel/Ai/IAIProvider.cs)

```csharp
public interface IAIProvider
{
    Task<Result<PromptResponse>> GenerateAsync(PromptRequest request, CancellationToken ct = default);

    /// <summary>Health probe — must NOT throw on transient failures.</summary>
    Task<bool> IsHealthyAsync(CancellationToken ct = default);
}

public sealed record PromptRequest(
    string Prompt,
    string? SystemContextJson = null,
    string Model = "llama3.1:8b",
    decimal Temperature = 0.3m,
    int MaxTokens = 512);

public sealed record PromptResponse(
    string Content,
    string Model,
    int LatencyMs,
    int PromptTokens,
    int CompletionTokens);

public enum AIProviderKind : byte
{
    Ollama = 0,
    OpenAi = 1,    // Wave 6
    Claude = 2,    // Wave 6
    Stub = 255,
}
```

**Why Shared.Kernel**: mirrors `IQuoteProvider` precedent. The wire shape (`PromptRequest/Response`) is cross-module stable. Trading owns the impl, but Billing or Identity could later consume the same abstraction.

### `ImportFormat` (Shared.Kernel/Imports/ImportFormat.cs)

```csharp
public enum ImportFormat : byte
{
    Csv = 0,
    Mt4 = 1,
    Mt5 = 2,
    Unknown = 255,
}
```

### `IImportRowParser` (Shared.Kernel/Imports/IImportRowParser.cs)

```csharp
public interface IImportRowParser
{
    ImportFormat Format { get; }

    /// <summary>Confidence score 0..1 — first parser with >= 0.8 wins.</summary>
    double CanParse(string fileName, Stream head);

    IAsyncEnumerable<ImportRow> ParseAsync(Stream body, CancellationToken ct = default);
}

public sealed record ImportRow(
    int LineNumber,
    string? TicketId,
    string Symbol,
    decimal Volume,
    string VolumeCurrency,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    decimal EntryPrice,
    decimal? ExitPrice,
    decimal? PnlAmount,
    string PnlCurrency,
    ImportDirection Direction,
    ImportRowStatus Status,
    string? Notes);
```

**Why Shared.Kernel**: the parser contract is cross-module stable (Trading Application owns the `StreamImportService`, but it consumes `IImportRowParser` via DI). Mirror precedent: `IQuoteProvider`, `IVirusScanner`.

## New Aggregates (Trading.Domain)

### `ImportJob` (Trading.Domain/Imports/ImportJob.cs)

```csharp
public class ImportJob : AggregateRoot<Guid>
{
    public const int MaxFileSizeBytes = 10 * 1024 * 1024;  // 10 MiB
    public const int BatchSize = 50;

    public UserId UserId { get; }
    public Guid AccountId { get; }
    public ImportFormat Format { get; private set; }
    public string FileName { get; }
    public long FileSizeBytes { get; }
    public string FileSha256 { get; }
    public ImportJobStatus Status { get; private set; }
    public int RowsTotal { get; private set; }
    public int RowsImported { get; private set; }
    public int RowsSkipped { get; private set; }
    public int RowsErrored { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? FinishedAt { get; private set; }

    public static Result<ImportJob> Begin(
        UserId userId, Guid accountId, ImportFormat format, string fileName,
        long fileSizeBytes, string sha256, IClock clock);

    public Result RecordProgress(int imported, int skipped, int errored);
    public Result Complete();
    public Result Fail(string errorMessage);
}

public enum ImportJobStatus : byte
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
}
```

**Invariants**:
- `FileSizeBytes <= 10 MiB`.
- `RowsImported + RowsSkipped + RowsErrored <= RowsTotal` (counters are monotonic).
- `Status` transitions: `Pending → InProgress → (Completed | Failed | Cancelled)`. No back-transitions.
- `FileSha256` is computed at start (SHA-256 of the request body); re-uploading the same file in the same session → second job skipped automatically.

### `CoachingPrompt` (Trading.Domain/Ai/CoachingPrompt.cs)

```csharp
public class CoachingPrompt : AggregateRoot<Guid>
{
    public UserId UserId { get; }
    public string PromptText { get; }
    public string ContextJson { get; }
    public string ProviderResponse { get; }
    public string Model { get; }
    public int LatencyMs { get; }
    public PromptSeverity Severity { get; }
    public CoachingPromptKind Kind { get; }  // Rule = 0 (legacy), Ai = 1
    public DateTimeOffset CreatedAt { get; }

    public static Result<CoachingPrompt> Create(
        UserId userId, string promptText, string contextJson,
        PromptResponse response, PromptSeverity severity, IClock clock);
}

public enum PromptSeverity : byte { Low = 0, Medium = 1, High = 2 }
```

### `AIRiskAdvice` (Trading.Domain/Ai/AIRiskAdvice.cs)

```csharp
public class AIRiskAdvice : AggregateRoot<Guid>
{
    public UserId UserId { get; }
    public Guid? TradeId { get; }      // null for manual advisory (no trade yet)
    public string ContextJson { get; }
    public string ProviderResponse { get; }
    public AIRiskAction ParsedAction { get; }
    public string Reason { get; }
    public DateTimeOffset CreatedAt { get; }

    public static Result<AIRiskAdvice> Create(
        UserId userId, Guid? tradeId, string contextJson,
        PromptResponse response, string reason, IClock clock);
}

public enum AIRiskAction : byte { Allow = 0, Warning = 1, Block = 2 }
```

## Modified Aggregates

### `PreTradeChecklist` (additive column for AI advisory)

Wave 1c introduced `PreTradeChecklist`. Wave 5c adds one nullable JSONB column:

```csharp
public class PreTradeChecklist : AggregateRoot<Guid>
{
    // existing Wave 1c fields unchanged...

    /// <summary>AI advisory attached at OpenTrade time (Wave 5c).</summary>
    public string? AIRiskAdvisoryJson { get; private set; }

    public Result AttachAIRiskAdvisory(string advisoryJson);
}
```

The advisory is JSON-serialized as `{ action: "warning"|"block"|"allow", reason: "...", model: "...", latency_ms: N, created_at: "..." }`. The `AttachAIRiskAdvisory` is additive — legacy checklists without advisory remain valid.

### `Trade` (no domain changes, but `OpenTradeHandler` is modified)

The `Trade` aggregate itself is unchanged. Only `OpenTradeHandler` gains an optional `IAIRiskAdvisor` dependency.

## EF Configuration

### `ImportJobConfiguration` (NEW)

```csharp
internal sealed class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> b)
    {
        b.ToTable("import_jobs");
        b.HasKey(j => j.Id);
        b.Property(j => j.Id).HasColumnName("id");
        b.Property(j => j.UserId).HasColumnName("user_id").IsRequired();
        b.Property(j => j.AccountId).HasColumnName("account_id").IsRequired();
        b.Property(j => j.Format).HasColumnName("format").HasConversion<byte>();
        b.Property(j => j.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
        b.Property(j => j.FileSizeBytes).HasColumnName("file_size_bytes");
        b.Property(j => j.FileSha256).HasColumnName("file_sha256").HasMaxLength(64).IsRequired();
        b.Property(j => j.Status).HasColumnName("status").HasConversion<byte>();
        b.Property(j => j.RowsTotal).HasColumnName("rows_total");
        b.Property(j => j.RowsImported).HasColumnName("rows_imported");
        b.Property(j => j.RowsSkipped).HasColumnName("rows_skipped");
        b.Property(j => j.RowsErrored).HasColumnName("rows_errored");
        b.Property(j => j.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
        b.Property(j => j.StartedAt).HasColumnName("started_at");
        b.Property(j => j.FinishedAt).HasColumnName("finished_at");

        b.Ignore(j => j.DomainEvents);

        b.HasIndex(j => new { j.UserId, j.Status })
            .HasDatabaseName("ix_import_jobs_user_status");
        b.HasIndex(j => j.FileSha256)
            .HasDatabaseName("ix_import_jobs_sha")
            .HasFilter("status IN (0, 1, 2)");  // pending/in-progress/completed
    }
}
```

### `CoachingPromptConfiguration` (NEW)

```csharp
internal sealed class CoachingPromptConfiguration : IEntityTypeConfiguration<CoachingPrompt>
{
    public void Configure(EntityTypeBuilder<CoachingPrompt> b)
    {
        b.ToTable("coaching_prompts_ai");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.UserId).HasColumnName("user_id").IsRequired();
        b.Property(p => p.PromptText).HasColumnName("prompt_text").HasMaxLength(4000).IsRequired();
        b.Property(p => p.ContextJson).HasColumnName("context_json").HasColumnType("jsonb").IsRequired();
        b.Property(p => p.ProviderResponse).HasColumnName("provider_response").HasColumnType("jsonb").IsRequired();
        b.Property(p => p.Model).HasColumnName("model").HasMaxLength(64).IsRequired();
        b.Property(p => p.LatencyMs).HasColumnName("latency_ms");
        b.Property(p => p.Severity).HasColumnName("severity").HasConversion<byte>();
        b.Property(p => p.Kind).HasColumnName("kind").HasConversion<byte>();
        b.Property(p => p.CreatedAt).HasColumnName("created_at");

        b.Ignore(p => p.DomainEvents);

        b.HasIndex(p => new { p.UserId, p.CreatedAt })
            .HasDatabaseName("ix_coaching_ai_user_created");
    }
}
```

### `AIRiskAdviceConfiguration` (NEW)

```csharp
internal sealed class AIRiskAdviceConfiguration : IEntityTypeConfiguration<AIRiskAdvice>
{
    public void Configure(EntityTypeBuilder<AIRiskAdvice> b)
    {
        b.ToTable("ai_risk_advice");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).HasColumnName("id");
        b.Property(a => a.UserId).HasColumnName("user_id").IsRequired();
        b.Property(a => a.TradeId).HasColumnName("trade_id");
        b.Property(a => a.ContextJson).HasColumnName("context_json").HasColumnType("jsonb").IsRequired();
        b.Property(a => a.ProviderResponse).HasColumnName("provider_response").HasColumnType("jsonb").IsRequired();
        b.Property(a => a.ParsedAction).HasColumnName("parsed_action").HasConversion<byte>();
        b.Property(a => a.Reason).HasColumnName("reason").HasMaxLength(500);
        b.Property(a => a.CreatedAt).HasColumnName("created_at");

        b.Ignore(a => a.DomainEvents);

        b.HasIndex(a => new { a.UserId, a.TradeId })
            .HasDatabaseName("ix_ai_risk_advice_user_trade");
    }
}
```

### `PreTradeChecklistConfiguration` (extended)

```csharp
internal sealed class PreTradeChecklistConfiguration : IEntityTypeConfiguration<PreTradeChecklist>
{
    public void Configure(EntityTypeBuilder<PreTradeChecklist> b)
    {
        // existing Wave 1c mappings...

        // Wave 5c: AI advisory column
        b.Property(c => c.AIRiskAdvisoryJson).HasColumnName("ai_advisory").HasColumnType("jsonb");
    }
}
```

## DI Composition

### `TradingModuleRegistration` (NEW additions)

```csharp
// Wave 5 — AI provider
services.AddSingleton<IAIProvider, OllamaHttpClient>();   // singleton: HttpClient lifecycle
services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));

// Wave 5 — Importers
services.AddScoped<IImportRowParser, CsvImportRowParser>();
services.AddScoped<IImportRowParser, Mt4ImportRowParser>();
services.AddScoped<StreamImportService>();
services.AddScoped<IImportJobRepository, ImportJobRepository>();
services.AddScoped<BeginImportHandler>();
services.AddScoped<GetImportStatusHandler>();
services.AddScoped<IImportRowDedupeService, ImportRowDedupeService>();

// Wave 5 — AI coaching
services.AddScoped<GenerateCoachingPromptHandler>();
services.AddScoped<ICoachingPromptRepository, CoachingPromptRepository>();
services.AddHostedService<CoachingPromptService>();   // daily 03:00 UTC

// Wave 5 — AI risk advisor
services.AddScoped<IAIRiskAdvisor, OllamaAIRiskAdvisor>();
services.AddScoped<GetPreTradeAdviceHandler>();
services.AddScoped<IAIRiskAdviceRepository, AIRiskAdviceRepository>();
services.AddHostedService<ExpiredAIRiskAdviceSweeper>();   // optional: Wave 5c.1.1
```

### `Program.cs` (NEW additions)

```csharp
// AI endpoints
app.MapAiEndpoints();          // GET /api/ai/health, POST /api/ai/risk-advice, GET /api/ai/risk-advice/{id}
app.MapImportEndpoints();      // POST /api/imports/csv, GET /api/imports/{id}
app.MapCoachingPromptsEndpointExtension();   // extends /api/coaching/prompts with AI section
```

All require `RequireAuthorization()` and `api-general` rate limit (same precedent as Wave 4).

## Ollama Implementation Detail

### `OllamaHttpClient` (Trading.Infrastructure/Ai/OllamaHttpClient.cs)

```csharp
public sealed class OllamaHttpClient : IAIProvider
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaHttpClient> _logger;

    public OllamaHttpClient(HttpClient http, IOptions<OllamaOptions> options, ILogger<OllamaHttpClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/'));
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public async Task<Result<PromptResponse>> GenerateAsync(PromptRequest req, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Ollama /api/generate expects: { model, prompt, stream: false, options: { temperature, num_predict } }
            // System context is prepended to the prompt (Ollama has no separate system role).
            var fullPrompt = req.SystemContextJson is null
                ? req.Prompt
                : req.SystemContextJson + "\n\n" + req.Prompt;

            var body = new
            {
                model = req.Model,
                prompt = fullPrompt,
                stream = false,
                options = new
                {
                    temperature = (double)req.Temperature,
                    num_predict = req.MaxTokens
                }
            };

            using var httpResponse = await _http.PostAsJsonAsync("/api/generate", body, ct);
            httpResponse.EnsureSuccessStatusCode();

            var response = await httpResponse.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: ct);

            return Result<PromptResponse>.Success(new PromptResponse(
                Content: response!.Response,
                Model: response.Model ?? req.Model,
                LatencyMs: (int)stopwatch.ElapsedMilliseconds,
                PromptTokens: response.PromptEvalCount ?? 0,
                CompletionTokens: response.EvalCount ?? 0));
        }
        catch (TaskCanceledException ex) when (ct.IsCancellationRequested)
        {
            return Result<PromptResponse>.Failure(new AIError("ai.timeout", "Ollama request cancelled", ex));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Ollama unreachable at {BaseUrl}", _options.BaseUrl);
            return Result<PromptResponse>.Failure(new AIError("ai.unavailable", $"Ollama unreachable: {ex.Message}", ex));
        }
        catch (Exception ex)
        {
            return Result<PromptResponse>.Failure(new AIError("ai.internal_error", ex.Message, ex));
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync("/api/tags", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

public sealed record OllamaOptions
{
    public string BaseUrl { get; init; } = "http://localhost:11434";
    public string Model { get; init; } = "llama3.1:8b";
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxTokens { get; init; } = 512;
}

public sealed record OllamaGenerateResponse(
    string Model,
    string Response,
    bool Done,
    int? PromptEvalCount,
    int? EvalCount);
```

**Hosted HttpClient registration**:

```csharp
services.AddHttpClient<IAIProvider, OllamaHttpClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/'));
    client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
})
.AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(3,
    attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt))));  // 200ms, 400ms, 800ms
```

### `OllamaAIRiskAdvisor` (Trading.Infrastructure/Ai/OllamaAIRiskAdvisor.cs)

```csharp
public sealed class OllamaAIRiskAdvisor : IAIRiskAdvisor
{
    private readonly IAIProvider _provider;
    private readonly IAIRiskAdviceRepository _repo;
    private readonly IUserTradingContextProvider _context;
    private readonly OllamaOptions _options;

    public async Task<Result<AIRiskAdvice>> AdviseAsync(
        AIRiskAdviceRequest req, CancellationToken ct = default)
    {
        // 1. Compose structured prompt template
        var context = await _context.GetUserContextAsync(req.UserId, 7, ct);
        var prompt = AIRiskAdvisorPrompt.Render(req, context);

        // 2. Call Ollama with 5s timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        var result = await _provider.GenerateAsync(new PromptRequest(
            Prompt: prompt,
            Model: _options.Model,
            Temperature: 0.2m,        // low temperature — deterministic
            MaxTokens: 256), cts.Token);

        if (result.IsFailure) return Result<AIRiskAdvice>.Failure(result.Error);

        // 3. Parse the JSON response: { action, reason }
        var parsed = AIRiskAdvisorResponseParser.Parse(result.Value.Content);

        // 4. Build and persist aggregate
        var advice = AIRiskAdvice.Create(req.UserId, req.TradeId,
            SerializeContext(context), result.Value, parsed.Reason, /*clock*/);

        if (advice.IsSuccess)
            await _repo.AddAsync(advice.Value, ct);

        return advice;
    }
}
```

### `AIRiskAdvisorPrompt` (Trading.Application/Ai/AIRiskAdvisorPrompt.cs)

```csharp
public static class AIRiskAdvisorPrompt
{
    /// <summary>
    /// Renders a structured prompt with explicit delimiter markers.
    /// Prevents prompt injection from the trade context by isolating user content.
    /// </summary>
    public static string Render(AIRiskAdviceRequest req, UserTradingContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a risk-advisor for a trading system. Your role is advisory only.");
        sb.AppendLine("You will be given the user's recent trading context and the proposed trade.");
        sb.AppendLine("Respond ONLY with a JSON object: {\"action\": \"allow\"|\"warning\"|\"block\", \"reason\": \"<one-sentence explanation, max 200 chars>\"}");
        sb.AppendLine();
        sb.AppendLine("--- USER TRADING CONTEXT (last 7 days) ---");
        sb.AppendLine(JsonSerializer.Serialize(ctx, new JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine();
        sb.AppendLine("--- PROPOSED TRADE ---");
        sb.AppendLine($"Symbol: {req.TradeSymbol}");
        sb.AppendLine($"Direction: {req.Direction}");
        sb.AppendLine($"Volume: {req.Volume} {req.VolumeCurrency}");
        sb.AppendLine($"Entry: {req.EntryPrice}");
        sb.AppendLine($"Stop: {(req.StopLoss?.ToString() ?? "not set")}");
        sb.AppendLine($"RiskRewardAtEntry: {req.RiskRewardAtEntry}");
        sb.AppendLine($"SetupQuality: {req.SetupQuality}");
        sb.AppendLine();
        sb.AppendLine("--- ADVISORY JSON ---");
        return sb.ToString();
    }
}

public static class AIRiskAdvisorResponseParser
{
    public static (AIRiskAction Action, string Reason) Parse(string content)
    {
        try
        {
            // Ollama sometimes wraps JSON in markdown code fences. Strip them.
            var trimmed = Regex.Replace(content, @"```(?:json)?\s*|\s*```", "").Trim();
            var json = JsonDocument.Parse(trimmed).RootElement;

            var actionStr = json.GetProperty("action").GetString();
            var reasonStr = json.GetProperty("reason").GetString() ?? "(no reason given)";

            var action = actionStr?.ToLowerInvariant() switch
            {
                "allow" => AIRiskAction.Allow,
                "warning" => AIRiskAction.Warning,
                "block" => AIRiskAction.Block,
                _ => AIRiskAction.Allow  // safe default
            };
            return (action, reasonStr.Length > 500 ? reasonStr[..500] : reasonStr);
        }
        catch
        {
            // Malformed JSON → safe default (allow + note the parse failure)
            return (AIRiskAction.Allow, "AI returned unparseable response — proceeding without advisory");
        }
    }
}
```

### `CoachingPromptTemplate` (Trading.Application/Ai/CoachingPromptTemplate.cs)

```csharp
public static class CoachingPromptTemplate
{
    public static string Render(UserTradingContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a trading coach. The user has had this activity in the last 7 days:");
        sb.AppendLine();
        sb.AppendLine("--- USER TRADING CONTEXT ---");
        sb.AppendLine(JsonSerializer.Serialize(new
        {
            days_analyzed = 7,
            closed_trades = ctx.ClosedTradeCount,
            winners = ctx.Winners,
            losers = ctx.Losers,
            win_rate = ctx.WinRate,
            avg_rr = ctx.AverageRiskReward,
            avg_pnl = ctx.AveragePnl,
            instruments_traded = ctx.InstrumentsTraded,
            violations = ctx.CoachingViolations  // from Wave 3b rules
        }, new JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine();
        sb.AppendLine("Generate a single coaching message:");
        sb.AppendLine("- Tone: respectful, specific, non-judgmental.");
        sb.AppendLine("- Length: 50-150 words.");
        sb.AppendLine("- Include 1 actionable suggestion tied to the data.");
        sb.AppendLine("- DO NOT include absolute P&L numbers (no '$450' or similar).");
        sb.AppendLine("- Return ONLY the message text — no JSON, no headers.");
        return sb.ToString();
    }
}
```

## Frontend

### New files

**Imports** (`frontend/src/app/features/trader/imports/`):
- `imports-page.ts` — standalone Signals OnPush SCSS con: drop zone (drag-and-drop), file picker fallback, progress bar (subscribes to polling endpoint at 1s interval), result summary (imported/skipped/errored + last error).
- `api/imports.service.ts` — HTTP wrapper (2 methods: `uploadCsv(file)`, `getStatus(jobId)`).
- `state/imports.state.ts` — Signals: `currentJob`, `progress`, `status`, `error`.
- `routes/imports.routes.ts` — sub-routes.
- `__tests__/imports-page.spec.ts` — 4 specs.

**Coaching tab extension** (`frontend/src/app/features/trader/coaching/`):
- `coaching-page.ts` — MODIFIED: agrega sección "AI prompts (7d)" debajo de los rule-based prompts.
- `api/coaching.service.ts` — EXTENDED: agrega `getAiPrompts(period)` method.
- `__tests__/coaching-page.spec.ts` — 2 new specs for AI section.

**Pre-trade checklist extension** (`frontend/src/app/features/trader/checklist/`):
- `pre-trade-checklist-page.ts` — MODIFIED: agrega sección "AI Advisory" entre el form del checklist y el botón "Open trade". Muestra el advisory parseado (action + reason) cuando el handler lo devuelve. Modal de override si `action = "warning"|"block"`.
- `api/risk-advice.service.ts` — HTTP wrapper (3 methods: `getHealth`, `requestAdvice`, `getCachedAdvice`).
- `__tests__/pre-trade-checklist-page.spec.ts` — 3 new specs for advisory section.

### Modified

- `frontend/src/app/features/trader/trader.routes.ts` — add `imports` lazy route.
- `frontend/src/app/features/trader/trader-shell.ts` — add `Imports` nav entry (after Journal, 10 items total).
- `frontend/src/app/core/realtime/` — new `ollama-health.interval.ts` — pings `GET /api/ai/health` every 60s; signals the global state when Ollama goes up/down.

## Cross-Module Concerns

### `OpenTradeHandler` modification (5c.1)

```csharp
// Wave 1c (legacy):
internal sealed class OpenTradeHandler : IRequestHandler<OpenTradeCommand, Result<TradeDto>>
{
    private readonly IClock _clock;
    private readonly ITradeRepository _trades;
    // ... other deps

    public async Task<Result<TradeDto>> Handle(OpenTradeCommand cmd, CancellationToken ct)
    {
        // Validate checklist (legacy path)
        // Open trade
    }
}

// Wave 5c (modified):
internal sealed class OpenTradeHandler : IRequestHandler<OpenTradeCommand, Result<TradeDto>>
{
    private readonly IClock _clock;
    private readonly ITradeRepository _trades;
    private readonly IAIRiskAdvisor _advisor;  // NEW — optional via factory pattern
    // ...

    public async Task<Result<TradeDto>> Handle(OpenTradeCommand cmd, CancellationToken ct)
    {
        // 1. Validate checklist (legacy path — unchanged)
        // 2. NEW: If checklist present, ask advisor
        AIRiskAdvice? advice = null;
        if (cmd.PreTradeChecklist is not null)
        {
            var advisorReq = AIRiskAdviceRequest.From(cmd);
            var advisorResult = await _advisor.AdviseAsync(advisorReq, ct);

            if (advisorResult.IsFailure)
            {
                _logger.LogWarning("AI advisor failed: {Error}", advisorResult.Error.Message);
                // Silent fallback — proceed without advisory
            }
            else
            {
                advice = advisorResult.Value;
                if (advice.ParsedAction == AIRiskAction.Block)
                {
                    return Result<TradeDto>.Failure(new AIRiskBlocked(advice.Reason));
                    // → mapped to 422 with error.code = "ai_risk.blocked"
                }
                // Warning → proceed but attach advisory to checklist
                cmd.PreTradeChecklist.AttachAIRiskAdvisory(SerializeAdvice(advice));
            }
        }

        // 3. Open trade (legacy path — unchanged; reads the modified checklist)
    }
}
```

**DI safe fallback**: `IAIRiskAdvisor` is registered in `TradingModuleRegistration`. If the registration is missing (legacy code), the `OpenTradeHandler` ctor has a nullable overload that receives `IAIRiskAdvisor?` — DI resolves it as null in that case, and the handler skips step 2 entirely. This guarantees no breakage for pre-Wave-5 callers.

### `CoachingPromptService` (5b.2)

```csharp
public sealed class CoachingPromptService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CoachingPromptService> _logger;
    private static readonly TimeSpan Period = TimeSpan.FromHours(24);
    private static readonly TimeSpan Jitter = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan TargetRunTime = TimeSpan.FromHours(3);  // 03:00 UTC

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial jitter to wait until next 03:00 UTC ± 30min.
        var initialDelay = ComputeInitialDelay(DateTimeOffset.UtcNow, TargetRunTime, Jitter);
        await Task.Delay(initialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CoachingPromptService tick failed; will retry next cycle");
            }

            await Task.Delay(Period + Randomize(Jitter), stoppingToken);
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var userIds = scope.ServiceProvider.GetRequiredService<IUserActivityProvider>()
            .GetActiveUserIdsWithMinTrades(minClosedTrades: 5, windowDays: 7, ct);

        var handler = scope.ServiceProvider.GetRequiredService<GenerateCoachingPromptHandler>();
        int count = 0;
        foreach (var userId in userIds)
        {
            try
            {
                await handler.Handle(new GenerateCoachingPromptCommand(userId), ct);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Coaching prompt failed for user {UserId}", userId);
                // continue — one user's failure doesn't abort the loop
            }
        }
        return count;
    }

    private static TimeSpan ComputeInitialDelay(DateTimeOffset now, TimeSpan target, TimeSpan jitter)
    {
        var today = new DateTimeOffset(now.Date, TimeSpan.Zero).Add(target);
        var targetToday = today > now ? today : today.AddDays(1);
        var delay = targetToday - now;
        return delay + Randomize(jitter);
    }

    private static TimeSpan Randomize(TimeSpan span) =>
        TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * span.TotalMilliseconds);
}
```

**Testing seam**: `RunOnceAsync` is `public` so unit tests can drive the loop deterministically without waiting for the daily tick.

### Dedupe service (5a)

```csharp
public interface IImportRowDedupeService
{
    Task<int> FilterExistingAsync(UserId userId, Guid accountId, IAsyncEnumerable<ImportRow> rows, CancellationToken ct);
    /// <summary>
    /// Returns the count of NEW rows (not in trading.trades already).
    /// The skipped rows are silently dropped — the ImportJob's RowsSkipped counter is incremented.
    /// </summary>
}
```

The composite dedupe key is `(user_id, account_id, ticket_id)` for MT4/MT5 (where `ticket_id` is unique) and `(user_id, account_id, opened_at, symbol, entry_price)` for CSV (where ticket_id may be absent). The dedupe check is a single SQL `WHERE ... IN (...)` query per batch of 50.

### Streaming import (5a)

```csharp
public sealed class StreamImportService
{
    public async Task<ImportJob> ExecuteAsync(ImportJob job, Stream body, IImportRowParser parser, CancellationToken ct)
    {
        // 1. Detect parser
        job.MarkInProgress();
        await _repo.UpdateAsync(job, ct);

        // 2. Stream rows
        var batch = new List<ImportRow>(ImportJob.BatchSize);
        var totalRows = 0;
        var imported = 0; var skipped = 0; var errored = 0;

        await foreach (var row in parser.ParseAsync(body, ct))
        {
            batch.Add(row);
            totalRows++;

            if (batch.Count >= ImportJob.BatchSize)
            {
                var result = await PersistBatchAsync(job, batch, ct);
                imported += result.Imported;
                skipped += result.Skipped;
                errored += result.Errored;

                job.RecordProgress(imported, skipped, errored);
                await _repo.UpdateAsync(job, ct);

                batch.Clear();
            }
        }

        // Tail batch
        if (batch.Count > 0)
        {
            var result = await PersistBatchAsync(job, batch, ct);
            imported += result.Imported; skipped += result.Skipped; errored += result.Errored;
        }

        job.Complete();
        await _repo.UpdateAsync(job, ct);
        return job;
    }

    private async Task<DedupeResult> PersistBatchAsync(ImportJob job, List<ImportRow> batch, CancellationToken ct)
    {
        // 1. Dedupe — single SELECT existing keys
        var newRows = await _dedupe.FilterExistingAsync(job.UserId, job.AccountId, batch.ToAsyncEnumerable(), ct);

        // 2. Persist in transaction
        try
        {
            await using var tx = await _db.BeginTransactionAsync(ct);
            foreach (var row in newRows)
            {
                var trade = Trade.ImportFromRow(row, job.UserId, job.AccountId, _clock);
                _trades.Add(trade);
            }
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new DedupeResult(newRows.Count, batch.Count - newRows.Count, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch import failed (will fail import job)");
            throw;
        }
    }
}
```

**Error rollback**: if any batch fails, ALL prior batches remain committed (committed=true on success). The ImportJob is marked `Failed` with `ErrorMessage = ex.Message`. The user can re-upload with dedupe catching the previously-imported rows.

## Migration Sequencing

Three separate migration files (one per slice that ships independently):

```sql
-- 0019_import_jobs.sql (Wave 5a.1)
BEGIN;
CREATE TABLE IF NOT EXISTS trading.import_jobs (
    id               UUID PRIMARY KEY,
    user_id          UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
    account_id       UUID NOT NULL REFERENCES trading.accounts(id) ON DELETE RESTRICT,
    format           SMALLINT NOT NULL,
    file_name        VARCHAR(255) NOT NULL,
    file_size_bytes  BIGINT NOT NULL,
    file_sha256      CHAR(64) NOT NULL,
    status           SMALLINT NOT NULL DEFAULT 0,
    rows_total       INTEGER NOT NULL DEFAULT 0,
    rows_imported    INTEGER NOT NULL DEFAULT 0,
    rows_skipped     INTEGER NOT NULL DEFAULT 0,
    rows_errored     INTEGER NOT NULL DEFAULT 0,
    error_message    VARCHAR(2000),
    started_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    finished_at      TIMESTAMPTZ,
    CONSTRAINT ck_import_jobs_size CHECK (file_size_bytes > 0 AND file_size_bytes <= 10485760),
    CONSTRAINT ck_import_jobs_format CHECK (format IN (0, 1, 2, 255)),
    CONSTRAINT ck_import_jobs_status CHECK (status BETWEEN 0 AND 4)
);
CREATE INDEX IF NOT EXISTS ix_import_jobs_user_status
    ON trading.import_jobs (user_id, status);
CREATE INDEX IF NOT EXISTS ix_import_jobs_sha
    ON trading.import_jobs (file_sha256)
    WHERE status IN (0, 1, 2);
COMMENT ON TABLE trading.import_jobs IS 'Trade history import jobs (Wave 5a). One row per import attempt; rows_imported + rows_skipped = total rows processed in batches of 50.';
COMMIT;

-- 0020_coaching_prompts_ai.sql (Wave 5b.2)
BEGIN;
CREATE TABLE IF NOT EXISTS trading.coaching_prompts_ai (
    id                UUID PRIMARY KEY,
    user_id           UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
    prompt_text       VARCHAR(4000) NOT NULL,
    context_json      JSONB NOT NULL,
    provider_response JSONB NOT NULL,
    model             VARCHAR(64) NOT NULL,
    latency_ms        INTEGER NOT NULL,
    severity          SMALLINT NOT NULL DEFAULT 0,
    kind              SMALLINT NOT NULL DEFAULT 1,  -- 1 = AI
    created_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ck_coaching_ai_severity CHECK (severity BETWEEN 0 AND 2)
);
CREATE INDEX IF NOT EXISTS ix_coaching_ai_user_created
    ON trading.coaching_prompts_ai (user_id, created_at DESC);
COMMENT ON TABLE trading.coaching_prompts_ai IS 'AI-generated coaching prompts (Wave 5b). One row per prompt; populated by CoachingPromptService daily BG.';
COMMIT;

-- 0021_ai_risk_advice.sql (Wave 5c.1)
BEGIN;
CREATE TABLE IF NOT EXISTS trading.ai_risk_advice (
    id                UUID PRIMARY KEY,
    user_id           UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
    trade_id          UUID,
    context_json      JSONB NOT NULL,
    provider_response JSONB NOT NULL,
    parsed_action     SMALLINT NOT NULL,
    reason            VARCHAR(500),
    created_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ck_ai_risk_action CHECK (parsed_action BETWEEN 0 AND 2),
    CONSTRAINT fk_ai_risk_trade
        FOREIGN KEY (trade_id) REFERENCES trading.trades(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS ix_ai_risk_advice_user_trade
    ON trading.ai_risk_advice (user_id, trade_id);
ALTER TABLE trading.pre_trade_checklists
    ADD COLUMN IF NOT EXISTS ai_advisory JSONB;
COMMENT ON TABLE trading.ai_risk_advice IS 'AI-generated pre-trade risk advisories (Wave 5c). Each row is one advisory invocation; result attached to pre_trade_checklists.ai_advisory if applicable.';
COMMENT ON COLUMN trading.pre_trade_checklists.ai_advisory IS 'AI risk advisor output (Wave 5c). NULL if advisor did not run or returned allow-without-reason.';
COMMIT;
```

All three migrations are idempotent (`IF NOT EXISTS` / `ADD COLUMN IF NOT EXISTS`). They can be re-run safely. Wire en `migrate.Dockerfile` happy + retry path with `\\\"` escape.

## Sequence Diagrams

### CSV import flow

```
Trader                     FE                    POST /api/imports/csv       StreamImportService        IImportRowParser (CSV)
  |                        |                            |                              |                            |
  |--drag&drop file------->|                            |                              |                            |
  |                        |--multipart/form-data------>|                              |                            |
  |                        |                            |--Begin(ImportJob)--------->|                            |
  |                        |                            |<--job (Pending)-----------|                            |
  |                        |                            |--ExecuteAsync(job, stream)-->                            |
  |                        |                            |                              |--detect parser--------->|
  |                        |                            |                              |<--CSV parser------|        |
  |                        |                            |                              |--ParseAsync(rows)------->|
  |                        |                            |                              |<--IAsyncEnumerable<>-|
  |                        |                            |                              |  [batch of 50 rows]   |
  |                        |                            |                              |--Dedupe(SELECT)         |
  |                        |                            |                              |--BeginTransaction       |
  |                        |                            |                              |--INSERT trades           |
  |                        |                            |                              |--Commit                  |
  |                        |                            |                              |--RecordProgress          |
  |                        |                            |<--job.InProgress update--|                            |
  |<----- 1s poll -------|--(GET /api/imports/{id})-->|                              |                            |
  |                        |<--{status: 1, imported: 50, ...}--|                              |                            |
  |                        | ...                          |                              |                            |
  |<----- 1s poll -------|--(GET /api/imports/{id})-->|                              |                            |
  |                        |<--{status: 2, imported: 980, skipped: 20}-|                              |                            |
```

### Coaching prompt BG flow

```
CoachingPromptService (cron 03:00 UTC ±30min)         IUserActivityProvider        GenerateCoachingPromptHandler       IAIProvider (Ollama)         CoachingPromptRepository
            |                                                    |                              |                              |                                |
            |--GetActiveUserIdsWithMinTrades(5, 7d)------------>|                              |                              |                                |
            |<--[userId_1, userId_2, ...]---------------------|                              |                              |                                |
            | for each userId:                                 |                              |                              |                                |
            |   |--Handle(Generate(userId))------------------>|                              |                                |
            |                                                  |--GenerateAsync(prompt)------>|                                |
            |                                                  |<--PromptResponse------------|<--POST /api/generate-------|
            |                                                  |--Create+Add---------------->|                                |
            |                                                  |                              |                                |
            | end loop                                         |                              |                                |
```

### Pre-trade AI advisory flow

```
Trader              FE           POST /api/trades      OpenTradeHandler         IAIRiskAdvisor             IAIProvider (Ollama)        PreTradeChecklistRepo
  |                 |                  |                      |                        |                            |                              |
  |--submit form-->|                  |                      |                        |                            |                              |
  |                 |--with checklist->|                      |                        |                            |                              |
  |                 |                  |--OpenTrade(cmd)---->|                        |                            |                              |
  |                 |                  |                      |--AdviseAsync(req)---->|                            |                              |
  |                 |                  |                      |                        |--GenerateAsync(prompt)-->|
  |                 |                  |                      |                        |<--PromptResponse----------|
  |                 |                  |                      |                        |--Parse JSON {action,reason}|
  |                 |                  |                      |                        |--Build AIRiskAdvice        |
  |                 |                  |                      |                        |--Persist advice            |
  |                 |                  |                      |<--advice (action=warning)-|                            |                              |
  |                 |                  |                      |--AttachAIRiskAdvisory()---------------------------------->|
  |                 |                  |                      |--Trade.Open()                                         |
  |                 |                  |                      |--Persist trade + checklist                           |
  |                 |                  |                      |--Commit                                              |
  |                 |                  |<--201 + trade+advisory|                        |                              |                              |
  |                 |<--show advisory--|                      |                        |                            |                              |
```

If `action = "block"`, `OpenTradeHandler` short-circuits: returns `Result.Failure(new AIRiskBlocked(reason))` which maps to HTTP 422 with `error.code = "ai_risk.blocked"`. The FE shows the modal.

## Key Files to Be Created (slice breakdown)

**Slice 5a.1 (~700 líneas):**
- Backend: `Shared.Kernel/Imports/ImportFormat.cs` + `IImportRowParser.cs` + `ImportRow.cs`, `Trading.Domain/Imports/ImportJob.cs`, `Trading.Application/Features/Imports/BeginImport/*.cs` + `StreamImportService.cs` + `IImportJobRepository.cs`, `Trading.Infrastructure/Imports/CsvImportRowParser.cs` + `ImportJobRepository.cs` + `ImportJobConfiguration.cs`, `Trading.Api/Endpoints/ImportEndpoints.cs`, migration `0019_import_jobs.sql`.
- Frontend: `imports-page.ts` + `imports.service.ts` + `imports.state.ts` + `__tests__/imports-page.spec.ts` (4 specs).
- Tests: ~15 unit/integration.

**Slice 5a.2 (~500 líneas):**
- Backend: `Trading.Infrastructure/Imports/Mt4ImportRowParser.cs` (handles both MT4 and MT5 — they share a trade-history CSV format with slight header variations). Format detection in `StreamImportService.cs`. Tests with MT4 sample data (15 trades incl. edge cases).
- Tests: ~10 unit.

**Slice 5b.1 (~500 líneas):**
- Backend: `Shared.Kernel/Ai/IAIProvider.cs` + `PromptRequest.cs` + `PromptResponse.cs` + `AIProviderKind.cs`, `Trading.Infrastructure/Ai/OllamaHttpClient.cs` + `OllamaOptions.cs`, `Trading.Application/Abstractions/IUserTradingContextProvider.cs` (minimal impl for tests), `Trading.Api/Endpoints/AiEndpoints.cs` (only `GET /api/ai/health` in 5b.1), config binding in `Program.cs`. Tests with `HttpMessageHandler` mock (no real Ollama required).
- Tests: ~12 unit.

**Slice 5b.2 (~700 líneas):**
- Backend: `Trading.Domain/Ai/CoachingPrompt.cs`, `Trading.Application/Features/Coaching/GenerateCoachingPromptHandler.cs` + `CoachingPromptTemplate.cs` + `CoachingPromptRepository.cs`, `Trading.Infrastructure/Ai/CoachingPromptService.cs` (BackgroundService), `CoachingPromptConfiguration.cs`, `Trading.Api/Endpoints/CoachingEndpoints.cs` (extended with `GET /api/coaching/ai-prompts`), migration `0020_coaching_prompts_ai.sql`.
- Frontend: `coaching-page.ts` extension (1 section) + `coaching.service.ts` extension (`getAiPrompts` method) + 2 jest specs.
- Tests: ~15 unit.

**Slice 5c.1 (~600 líneas):**
- Backend: `Trading.Domain/Ai/AIRiskAdvice.cs` + `AIRiskAction.cs`, `Trading.Application/Ai/IAIRiskAdvisor.cs` + `GetPreTradeAdviceHandler.cs` + `AIRiskAdvisorPrompt.cs` + `AIRiskAdvisorResponseParser.cs` + `IUserTradingContextProvider.cs` (full impl), `Trading.Infrastructure/Ai/OllamaAIRiskAdvisor.cs` + `AIRiskAdviceRepository.cs` + `AIRiskAdviceConfiguration.cs`, modification to `OpenTradeHandler.cs` (additive `IAIRiskAdvisor?` ctor), extension to `PreTradeChecklistConfiguration.cs` (`ai_advisory jsonb` column), `Trading.Api/Endpoints/AiEndpoints.cs` extensions (`POST /api/ai/risk-advice` + `GET /api/ai/risk-advice/{trade_id}`), migration `0021_ai_risk_advice.sql`.
- Frontend: `pre-trade-checklist-page.ts` extension (AI advisory section + override modal) + `risk-advice.service.ts` + 3 jest specs. `trader-shell.ts` nav unchanged (10 items).
- Tests: ~15 unit (including 4-5 for OpenTradeHandler with both nullable + non-null advisor).

**Slice 5c.2 (~300 líneas, optional E2E):**
- `scripts/wave5-smoke.sh` — 3 E2E probes idempotentes: CSV upload + Ollama health + AI advisory block.
- `frontend/src/app/core/realtime/ollama-health.interval.ts` — 60s poll + signal.
- All tasks marked + verify report.

**Total Wave 5**: ~3,000 lines, 5 PRs chained.

## Per-Slice Path Budget (mandatory ≤ 32 paths per PR)

| Slice | Files created | Files modified | Total paths |
|---|---:|---:|---:|
| 5a.1 | 12 | 4 | 16 |
| 5a.2 | 4 | 2 | 6 |
| 5b.1 | 6 | 3 | 9 |
| 5b.2 | 8 | 5 | 13 |
| 5c.1 | 11 | 6 | 17 |
| 5c.2 (optional) | 3 | 2 | 5 |

Each slice is well under 32 paths. The largest is 5c.1 at 17 paths (below limit). Wave 4 precedent (4d = 25 files) shows 32 paths is comfortable even for cross-cutting slices.
