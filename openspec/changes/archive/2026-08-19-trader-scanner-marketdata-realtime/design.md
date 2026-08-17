# Design — Wave 4 (Trader Scanner + MarketData + Realtime)

## Architecture Overview

Wave 4 introduces four orthogonal capabilities that close the operational loop:

1. **MarketData abstraction** (`IQuoteProvider` in Shared.Kernel) — a deterministic in-memory provider stub that the rest of the system consumes as if it were a live feed. Wave 6 swaps the provider implementation without touching consumers.
2. **Realtime push** (SignalR `QuoteHub`) — server-side broadcast of quote updates to subscribed clients grouped by symbol. The `QuoteBroadcastService` BackgroundService polls the provider every 5s and pushes only changed quotes.
3. **Scanner** — user-owned filter aggregates + scan runner that joins `trading.instruments` with cached metrics to produce a ranked `ScanResult[]`.
4. **Attachments lifecycle** — completes the Wave 1d `IAttachmentStorage` with quota enforcement, 90-day cleanup sweep, on-demand thumbnails, and a virus scan stub.

The four capabilities share two cross-cutting concerns:
- **Wave 3b `CurrentPriceNearStopRule` is rewired** to consume `IQuoteProvider.GetQuoteAsync(symbol)` instead of `EntryPrice` proxy. No requirement change at the alerts spec level (alert semantics are the same); implementation change documented in design + delta spec.
- **No domain events dispatcher** (Wave 2 finding). BackgroundServices use `IServiceProvider.GetRequiredService<T>()` directly rather than `INotificationHandler<T>` event handlers.

The new domain entities live in `Shared.Kernel/MarketData/` (cross-module), `Trading.Domain/Scanner/`, `Trading.Domain/Storage/`. The application layer adds handlers in `Trading.Application/Features/Scanner/`, `Trading.Application/Features/MarketData/`, `Trading.Application/Features/Realtime/`, `Trading.Application/Features/Storage/`. Infrastructure extends `TradingDbContext` with new tables, registers `IQuoteProvider` and `IVirusScanner`, and adds two BackgroundServices. API adds four endpoint groups plus the SignalR hub. Frontend adds the scanner page, SignalR client, watchlist component, and dashboard integration.

## New Value Objects / Records (Shared.Kernel)

### `Quote` (Shared.Kernel/MarketData/Quote.cs)

```csharp
public sealed record Quote(
    string Symbol,
    decimal Bid,
    decimal Ask,
    decimal Spread,
    decimal Volume24h,
    DateTimeOffset Timestamp,
    QuoteSource Source);

public enum QuoteSource : byte
{
    Stub = 0,    // InMemoryQuoteProvider (Wave 4)
    Mock = 1,    // scripted mock for integration tests
    Live = 2,    // future live feed
    Broker = 3,  // Wave 6 broker integration
}
```

**Cross-module wire shape**: `Quote` lives in `Shared.Kernel` because it's consumed by Trading (alert rules, scanner), Frontend (HTTP + SignalR), and (future) Wave 6 broker integrations. Keeping it in Shared.Kernel avoids Trading owning a record that other modules reference.

### `IQuoteProvider` (Shared.Kernel/MarketData/IQuoteProvider.cs)

```csharp
public interface IQuoteProvider
{
    Task<Quote?> GetQuoteAsync(string symbol, CancellationToken ct = default);
    Task<IReadOnlyList<Quote>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken ct = default);
}
```

**Why Shared.Kernel**: same reasoning as `Quote`. The interface is the abstraction over the data source; both Trading (scanner, alert rules) and Frontend (via API proxy) consume it indirectly. A future Wave 6 module can implement a `BrokerQuoteProvider` without Trading owning the abstraction.

### `AttachmentQuota` (Shared.Kernel/Storage/AttachmentQuota.cs)

```csharp
public sealed record AttachmentQuota(
    long MaxTotalBytes = 52_428_800,    // 50 MiB
    int MaxAttachmentCount = 100,
    int ExpirationDays = 90);
```

**Why Shared.Kernel**: the quota is cross-module (Storage enforces, handlers check, endpoint reports). A record is cleaner than config (stringly-typed).

### `IVirusScanner` (Shared.Kernel/Storage/IVirusScanner.cs)

```csharp
public interface IVirusScanner
{
    Task<ScanResult> ScanAsync(Stream content, string mimeType, CancellationToken ct = default);
}

public enum ScanResult : byte
{
    NotScanned = 0,
    Clean = 1,
    Infected = 2,
    Error = 3,
}
```

**Why Shared.Kernel**: pluggable hook for Wave 6 ClamAV. The Trading module owns the impl (`VirusScannerNoOp`) but the interface is kernel-level.

## New Aggregates (Trading.Domain)

### `ScannerFilter` (Trading.Domain/Scanner/ScannerFilter.cs)

```csharp
public class ScannerFilter : AggregateRoot<Guid>
{
    public const int MaxNameLength = 64;

    public UserId UserId { get; }
    public string Name { get; private set; } = default!;
    public decimal MinSpread { get; private set; }
    public decimal? MaxSpread { get; private set; }
    public decimal MinVolume { get; private set; }
    public decimal MinRiskReward { get; private set; }
    public VolatilityWindow VolatilityWindow { get; private set; }
    public IReadOnlyList<ActiveHoursWindow> ActiveHours { get; private set; } = Array.Empty<ActiveHoursWindow>();
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<ScannerFilter> Create(...);
    public Result Update(...);
    public Result SoftDelete();
    public Result Activate();
}

public enum VolatilityWindow : byte
{
    Unspecified = 0,
    D1 = 7,
    W1 = 8,
    MN = 9,
}

public sealed record ActiveHoursWindow(
    byte DayOfWeek,    // 0=Sunday .. 6=Saturday
    byte StartHour,    // 0..23
    byte EndHour);     // 1..24 (exclusive end)
```

**Invariants**:
- Name trimmed, 1..MaxNameLength chars, unique per `(UserId, lower(name)) WHERE IsActive = true`.
- `MinSpread >= 0`; `MaxSpread > MinSpread` if set.
- `MinVolume > 0`; `MinRiskReward > 0`.
- `VolatilityWindow` must be one of `D1, W1, MN` (or `Unspecified`).
- `ActiveHours` empty or array of valid windows (`StartHour < EndHour`, `DayOfWeek 0..6`).
- Soft-delete sets `IsActive = false`; can be re-activated via `Activate()`.

### `ScanResult` (Trading.Domain/Scanner/ScanResult.cs)

```csharp
public sealed record ScanResult(
    string Symbol,
    decimal CurrentSpread,
    decimal DailyVolume,
    decimal HistoricalRiskReward,
    decimal Volatility,
    double MatchScore,
    IReadOnlyList<string> MatchedCriteria);
```

Read-only value object — no domain behavior. `MatchedCriteria` enumerates which filter criteria the instrument met (e.g. `["spread", "volume", "riskReward"]`).

## Modified Aggregates

### `Trade.StrategyId` — unchanged

Wave 3a already added `StrategyId` nullable FK. Wave 4 doesn't touch it.

### `Trade.MfeAmount/MaeAmount` — unchanged

Wave 3c already persists these. Wave 4 doesn't touch them.

### `TradeAttachment` (additive columns)

Wave 1d introduced `TradeAttachment`. Wave 4 adds:

```csharp
public class TradeAttachment : AggregateRoot<Guid>
{
    // existing Wave 1d fields...
    public string? ThumbnailObjectKey { get; private set; }  // NEW
    public long Bytes { get; private set; }                    // NEW
    public DateTimeOffset? ExpiresAt { get; private set; }    // NEW
    public DateTimeOffset? VirusScannedAt { get; private set; } // NEW
    public ScanResult ScanResult { get; private set; }        // NEW

    public Result SetThumbnail(string objectKey);
    public Result MarkScanned(ScanResult result, IClock clock);
    public Result SetExpiration(DateTimeOffset expiresAt);
}
```

All four new fields are additive; existing data remains valid (defaults applied by migration).

### `CurrentPriceNearStopRule` (modified implementation, not interface)

```csharp
// Wave 3b — uses EntryPrice proxy:
internal sealed class CurrentPriceNearStopRule : IAlertRule
{
    private readonly IClock _clock;
    public CurrentPriceNearStopRule(IClock clock) { _clock = clock; }

    public IReadOnlyList<Alert> Evaluate(AlertContext ctx)
    {
        // Wave 3b (proxy):
        foreach (var trade in ctx.OpenTrades.Where(t => t.StopLossPrice.HasValue))
        {
            var currentPrice = trade.EntryPrice;  // PROXY
            if (Math.Abs(currentPrice - trade.StopLossPrice!.Value) / trade.StopLossPrice.Value < 0.01m)
            {
                // emit alert with proxy copy
            }
        }
    }
}

// Wave 4c (modified):
internal sealed class CurrentPriceNearStopRule : IAlertRule
{
    private readonly IClock _clock;
    private readonly IQuoteProvider _quotes;
    public CurrentPriceNearStopRule(IClock clock, IQuoteProvider quotes)
    {
        _clock = clock;
        _quotes = quotes;
    }

    public IReadOnlyList<Alert> Evaluate(AlertContext ctx)
    {
        foreach (var trade in ctx.OpenTrades.Where(t => t.StopLossPrice.HasValue))
        {
            Quote? quote;
            try
            {
                quote = _quotes.GetQuoteAsync(trade.Symbol, default).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                continue;  // silent skip — provider transient error
            }
            if (quote is null) continue;

            var currentPrice = (quote.Bid + quote.Ask) / 2m;
            if (Math.Abs(currentPrice - trade.StopLossPrice!.Value) / trade.StopLossPrice.Value < 0.01m)
            {
                // emit alert with honest copy referencing StopLossPrice
            }
        }
    }
}
```

The `IAlertRule` interface is unchanged. The change is constructor injection (add `IQuoteProvider`) and the data source for `currentPrice`. No new test setup beyond passing a mock provider.

## EF Configuration

### `ScannerFilterConfiguration` (NEW)

```csharp
internal sealed class ScannerFilterConfiguration : IEntityTypeConfiguration<ScannerFilter>
{
    public void Configure(EntityTypeBuilder<ScannerFilter> b)
    {
        b.ToTable("scanner_filters");
        b.HasKey(f => f.Id);
        b.Property(f => f.Id).HasColumnName("id");
        b.Property(f => f.UserId).HasColumnName("user_id").IsRequired();
        b.Property(f => f.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        b.Property(f => f.MinSpread).HasColumnName("min_spread").HasColumnType("numeric(10,5)");
        b.Property(f => f.MaxSpread).HasColumnName("max_spread").HasColumnType("numeric(10,5)");
        b.Property(f => f.MinVolume).HasColumnName("min_volume").HasColumnType("numeric(24,8)");
        b.Property(f => f.MinRiskReward).HasColumnName("min_risk_reward").HasColumnType("numeric(6,2)");
        b.Property(f => f.VolatilityWindow).HasColumnName("volatility_window").HasConversion<byte>();
        b.Property(f => f.IsActive).HasColumnName("is_active");
        b.Property(f => f.CreatedAt).HasColumnName("created_at");
        b.Property(f => f.UpdatedAt).HasColumnName("updated_at");

        // ActiveHours is a JSONB column with owned-type mapping.
        b.OwnsMany(f => f.ActiveHours, ah =>
        {
            ah.ToJson("active_hours");
            ah.Property(w => w.DayOfWeek).HasJsonPropertyName("dayOfWeek");
            ah.Property(w => w.StartHour).HasJsonPropertyName("startHour");
            ah.Property(w => w.EndHour).HasJsonPropertyName("endHour");
        });

        b.Ignore(f => f.DomainEvents);

        b.HasIndex(f => new { f.UserId, f.Name })
            .HasDatabaseName("ix_scanner_filters_user_name")
            .HasFilter("is_active = true");
    }
}
```

### `QuoteCacheConfiguration` (NEW)

```csharp
internal sealed class QuoteCacheConfiguration : IEntityTypeConfiguration<QuoteCacheEntry>
{
    public void Configure(EntityTypeBuilder<QuoteCacheEntry> b)
    {
        b.ToTable("quotes_cache");
        b.HasKey(q => q.Symbol);
        b.Property(q => q.Symbol).HasColumnName("symbol").HasMaxLength(20).IsRequired();
        b.Property(q => q.Bid).HasColumnName("bid").HasColumnType("numeric(18,8)");
        b.Property(q => q.Ask).HasColumnName("ask").HasColumnType("numeric(18,8)");
        b.Property(q => q.Spread).HasColumnName("spread").HasColumnType("numeric(18,8)");
        b.Property(q => q.Volume24h).HasColumnName("volume_24h").HasColumnType("numeric(24,8)");
        b.Property(q => q.Source).HasColumnName("source").HasConversion<byte>();
        b.Property(q => q.CachedAt).HasColumnName("cached_at");
    }
}
```

### `TradeAttachmentConfiguration` (extended)

```csharp
internal sealed class TradeAttachmentConfiguration : IEntityTypeConfiguration<TradeAttachment>
{
    public void Configure(EntityTypeBuilder<TradeAttachment> b)
    {
        // existing Wave 1d mappings...
        // Wave 4d additions:
        b.Property(a => a.ThumbnailObjectKey).HasColumnName("thumbnail_object_key").HasMaxLength(255);
        b.Property(a => a.Bytes).HasColumnName("bytes").HasDefaultValue(0L);
        b.Property(a => a.ExpiresAt).HasColumnName("expires_at");
        b.Property(a => a.VirusScannedAt).HasColumnName("virus_scanned_at");
        b.Property(a => a.ScanResult).HasColumnName("scan_result").HasConversion<byte>();

        b.HasIndex(a => a.ExpiresAt)
            .HasDatabaseName("ix_trade_attachments_expires_sweep")
            .HasFilter("is_active = true AND expires_at IS NOT NULL");
    }
}
```

## DI Composition

### `TradingModuleRegistration` (NEW additions)

```csharp
// Wave 4 — Scanner
services.AddScoped<IScannerFilterRepository, ScannerFilterRepository>();
services.AddScoped<IScannerRunner, ScannerRunner>();
services.AddScoped<CreateScannerFilterHandler>();
services.AddScoped<UpdateScannerFilterHandler>();
services.AddScoped<SoftDeleteScannerFilterHandler>();
services.AddScoped<GetScannerFiltersHandler>();
services.AddScoped<GetScannerFilterByIdHandler>();
services.AddScoped<RunScanHandler>();

// Wave 4 — MarketData
services.AddSingleton<IQuoteProvider, InMemoryQuoteProvider>();  // singleton: provider is stateless (seed + clock)
services.AddScoped<IQuoteCacheRepository, QuoteCacheRepository>();
services.AddScoped<GetQuoteHandler>();
services.AddScoped<GetQuotesBulkHandler>();
services.AddScoped<RefreshQuoteCacheHandler>();  // used by QuoteBroadcastService

// Wave 4 — Realtime
services.AddScoped<QuoteHub>();  // SignalR resolves per-call
services.AddHostedService<QuoteBroadcastService>();

// Wave 4 — Attachments
services.AddSingleton<IVirusScanner, VirusScannerNoOp>();  // stub, swap in Wave 6
services.AddScoped<AttachmentQuotaEnforcer>();
services.AddScoped<AttachmentUsageQuery>();
services.AddScoped<GetThumbnailHandler>();
services.AddHostedService<AttachmentLifecycleService>();
```

### `Program.cs` (NEW additions)

```csharp
// SignalR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 32 * 1024;  // 32 KB
});

// CORS — SignalR needs the same origins
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", p => p
        .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>())
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());  // SignalR requires credentials for cookies
});

// Endpoints
app.MapScannerEndpoints();
app.MapQuoteEndpoints();
app.MapAttachmentEndpoints();
app.MapHub<QuoteHub>("/hubs/quotes");
```

## SignalR Hub (Trading.Api)

### `QuoteHub` (Trading.Api/Hubs/QuoteHub.cs)

```csharp
public sealed class QuoteHub : Hub<IQuoteClient>
{
    private readonly ILogger<QuoteHub> _logger;

    public QuoteHub(ILogger<QuoteHub> logger) { _logger = logger; }

    public async Task SubscribeToSymbols(IEnumerable<string> symbols)
    {
        foreach (var symbol in symbols.Select(NormalizeSymbol))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"symbol-{symbol}");
            _logger.LogDebug("Connection {ConnId} subscribed to {Symbol}", Context.ConnectionId, symbol);
        }
    }

    public async Task UnsubscribeFromSymbols(IEnumerable<string> symbols)
    {
        foreach (var symbol in symbols.Select(NormalizeSymbol))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"symbol-{symbol}");
        }
    }

    public override async Task OnConnectedAsync()
    {
        // Auth is enforced by the JWT bearer middleware on /hubs/quotes
        _logger.LogDebug("Connection {ConnId} connected for {UserId}", Context.ConnectionId, Context.UserIdentifier);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // SignalR auto-removes the connection from all groups on disconnect
        _logger.LogDebug(exception, "Connection {ConnId} disconnected", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private static string NormalizeSymbol(string s) => s.Trim().ToUpperInvariant();
}

public interface IQuoteClient
{
    Task OnQuoteUpdate(Quote quote);
    Task OnError(string code, string message);
}
```

### `QuoteBroadcastService` (Trading.Infrastructure/Realtime/QuoteBroadcastService.cs)

```csharp
public sealed class QuoteBroadcastService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<QuoteHub, IQuoteClient> _hubContext;
    private readonly ConcurrentDictionary<string, Quote> _lastQuotes = new();
    private readonly TimeSpan _period = TimeSpan.FromSeconds(5);
    private readonly Random _jitter = new();

    public QuoteBroadcastService(IServiceScopeFactory scopeFactory, IHubContext<QuoteHub, IQuoteClient> hubContext)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial jitter (0..30s) to avoid thundering herd at startup.
        await Task.Delay(_jitter.Next(0, 30_000), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BroadcastTickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Log; never let the background crash.
            }

            await Task.Delay(_period + TimeSpan.FromMilliseconds(_jitter.Next(-500, 500)), stoppingToken);
        }
    }

    private async Task BroadcastTickAsync(CancellationToken ct)
    {
        // Discover currently-subscribed symbols from active SignalR groups.
        // SignalR doesn't expose group enumeration directly; we keep a side-channel
        // ConcurrentDictionary<string, byte> populated by QuoteHub.SubscribeToSymbols.
        var symbols = ActiveSubscriptions.Snapshot();  // thread-safe snapshot

        if (symbols.Count == 0) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IQuoteProvider>();
        var cache = scope.ServiceProvider.GetRequiredService<IQuoteCacheRepository>();

        var quotes = await provider.GetQuotesAsync(symbols, ct);

        foreach (var quote in quotes)
        {
            // Change detection: skip if unchanged from last tick.
            if (_lastQuotes.TryGetValue(quote.Symbol, out var prev) && QuotesEqual(prev, quote))
                continue;

            _lastQuotes[quote.Symbol] = quote;

            // Write-through cache update.
            await cache.UpsertAsync(quote, ct);

            // Push to subscribers.
            await _hubContext.Clients.Group($"symbol-{quote.Symbol}").OnQuoteUpdate(quote);
        }
    }

    private static bool QuotesEqual(Quote a, Quote b) =>
        a.Bid == b.Bid && a.Ask == b.Ask && a.Volume24h == b.Volume24h;
}

// Side-channel for tracking subscribed symbols across the hub's stateless boundaries.
public static class ActiveSubscriptions
{
    private static readonly ConcurrentDictionary<string, byte> _symbols = new();

    public static void Track(IEnumerable<string> symbols)
    {
        foreach (var s in symbols.Select(x => x.Trim().ToUpperInvariant()))
            _symbols.TryAdd(s, 0);
    }

    public static void Untrack(IEnumerable<string> symbols)
    {
        foreach (var s in symbols.Select(x => x.Trim().ToUpperInvariant()))
            _symbols.TryRemove(s, out _);
    }

    public static IReadOnlyList<string> Snapshot() => _symbols.Keys.ToList();
}
```

**Why the side-channel**: SignalR's `IHubContext` doesn't expose group enumeration — the broadcast service can't ask "which symbols have active subscribers?". The side-channel is populated by `QuoteHub.SubscribeToSymbols`/`UnsubscribeFromSymbols` and read by the broadcast loop. It's a known limitation; a future Wave could move this to a Redis pub/sub if multi-instance scaling is needed.

## Frontend

### New dependencies (`frontend/package.json`)

```json
{
  "dependencies": {
    "@microsoft/signalr": "^8.0.7"
  }
}
```

(Angular 19 already supports `@microsoft/signalr` 8.x.)

### New files

**Scanner** (`frontend/src/app/features/trader/scanner/`):
- `scanner-page.ts` — standalone Signals OnPush SCSS with: list of saved filters (left panel) + create form (inline) + run button + results table with match score column.
- `scanner.routes.ts` — sub-routes (`/scanner`).
- `state/scanner.state.ts` — Signals: `filters`, `selectedFilterId`, `results`, `isRunning`, `error`.
- `api/scanner.service.ts` — HTTP wrapper (6 methods: list, getById, create, update, delete, run).
- `__tests__/scanner-page.spec.ts` — 4 specs.

**Quotes (SignalR)** (`frontend/src/app/features/trader/quotes/`):
- `api/quotes.service.ts` — HTTP wrapper (3 methods: getBySymbol, getBulk).
- `api/quotes-signalr.service.ts` — `HubConnection` wrapper with reconnect-with-backoff (1s → 2s → 4s → 8s → 16s → 30s, infinite retry).
- `state/quotes.state.ts` — Signals: `quotes = signal<Map<string, Quote>>`, `subscribedSymbols`, `connectionStatus: 'connected'|'reconnecting'|'disconnected'`.
- `__tests__/quotes-signalr.service.spec.ts` — 3 specs (subscribe, unsubscribe, reconnect).

**Watchlist component** (`frontend/src/app/shared/watchlist/`):
- `jcs-watchlist.component.ts` — standalone OnPush component with `@Input() symbols: string[]`, `@Output() symbolClick: EventEmitter<string>`. Subscribes on init, unsubscribes on destroy. Renders a row per symbol with bid/ask/spread/timestamp.
- `jcs-watchlist.component.scss` — table styling with mobile-first (horizontal scroll on narrow screens).
- `__tests__/jcs-watchlist.spec.ts` — 3 specs.

### Modified

- `frontend/src/app/features/trader/trader-shell.ts` — add `'Scanner'` nav entry (icon: 'menu'). Mobile-nav `jcs-mobile-nav` already supports horizontal scroll; 9 items render fine.
- `frontend/src/app/features/trader/trader.routes.ts` — add `path: 'scanner'` lazy route.
- `frontend/src/app/features/trader/dashboard.page.ts` — embed `<jcs-watchlist [symbols]="['EURUSD','GBPJPY','BTCUSD']">` in the live-prices section.

## Cross-Module Concerns

### `CurrentPriceNearStopRule` data source change

The constructor of `CurrentPriceNearStopRule` gains `IQuoteProvider`. Existing Wave 3b tests that mocked `EntryPrice` need to be updated to mock `IQuoteProvider.GetQuoteAsync`. The test surface stays small (4 tests).

### No domain events dispatcher

Per Wave 2 finding, Wave 4 does NOT introduce a domain events dispatcher. `QuoteBroadcastService` and `AttachmentLifecycleService` are `BackgroundService`s that pull via `IServiceProvider.GetRequiredService<T>()`. They don't listen to `INotificationHandler<T>` events. Wave 5+ may revisit this if cross-module event coordination is needed.

### MinIO bucket lifecycle

The MinIO bucket policy (90d expiration) is applied via `docker-compose.yml` MinIO init container (already present from Wave 1d). The `AttachmentLifecycleService` is belt-and-suspenders — it cleans up DB rows + MinIO objects even if the bucket policy is misconfigured.

### CORS for SignalR

SignalR WebSocket requires `AllowCredentials()` in CORS. The existing CORS policy is updated to include credentials. Origins stay restricted to the configured allowlist.

### IAttachmentStorage extension

`MinioAttachmentStore` already exists (Wave 1d). Wave 4d adds:

```csharp
public Task<string> GetThumbnailUrlAsync(string objectKey, int width, int height, CancellationToken ct)
{
    // Build presigned GET URL with ?width=&height= transform params.
    var args = new PresignedGetObjectArgs()
        .WithBucket(_bucket)
        .WithObject(objectKey)
        .WithExpiry(3600);  // 1h

    var url = _minio.PresignedGetObjectAsync(args).GetAwaiter().GetResult();
    var uri = new UriBuilder(url);
    var query = uri.Query.TrimStart('?');
    query = string.IsNullOrEmpty(query)
        ? $"width={width}&height={height}"
        : $"{query}&width={width}&height={height}";
    uri.Query = query;
    return Task.FromResult(uri.Uri.ToString());
}
```

## Migration Sequencing

Three separate migration files (one per slice that ships independently):

```
infrastructure/postgres/migrations/
├── 0016_quotes_cache.sql              (Wave 4b)
│   BEGIN;
│   CREATE TABLE IF NOT EXISTS trading.quotes_cache (
│       symbol VARCHAR(20) PRIMARY KEY,
│       bid NUMERIC(18,8) NOT NULL,
│       ask NUMERIC(18,8) NOT NULL,
│       spread NUMERIC(18,8) NOT NULL,
│       volume_24h NUMERIC(24,8) NOT NULL,
│       source SMALLINT NOT NULL DEFAULT 0,
│       cached_at TIMESTAMPTZ NOT NULL DEFAULT now()
│   );
│   ALTER TABLE trading.instruments
│       ADD COLUMN IF NOT EXISTS last_quote_at TIMESTAMPTZ NULL,
│       ADD COLUMN IF NOT EXISTS bid NUMERIC(18,8) NULL,
│       ADD COLUMN IF NOT EXISTS ask NUMERIC(18,8) NULL,
│       ADD COLUMN IF NOT EXISTS spread NUMERIC(18,8) NULL;
│   COMMIT;
│
├── 0017_scanner_filters.sql            (Wave 4a)
│   BEGIN;
│   CREATE TABLE IF NOT EXISTS trading.scanner_filters (
│       id UUID PRIMARY KEY,
│       user_id UUID NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
│       name VARCHAR(64) NOT NULL,
│       min_spread NUMERIC(10,5) NOT NULL DEFAULT 0,
│       max_spread NUMERIC(10,5) NULL,
│       min_volume NUMERIC(24,8) NOT NULL DEFAULT 0,
│       min_risk_reward NUMERIC(6,2) NOT NULL DEFAULT 0,
│       volatility_window SMALLINT NOT NULL DEFAULT 0,
│       active_hours JSONB NOT NULL DEFAULT '[]',
│       is_active BOOLEAN NOT NULL DEFAULT TRUE,
│       created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
│       updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
│   );
│   CREATE INDEX IF NOT EXISTS ux_scanner_filters_user_name
│       ON trading.scanner_filters (user_id, lower(name)) WHERE is_active = true;
│   COMMIT;
│
└── 0018_attachment_lifecycle.sql       (Wave 4d)
    BEGIN;
    ALTER TABLE trading.trade_attachments
        ADD COLUMN IF NOT EXISTS thumbnail_object_key VARCHAR(255) NULL,
        ADD COLUMN IF NOT EXISTS bytes BIGINT NOT NULL DEFAULT 0,
        ADD COLUMN IF NOT EXISTS expires_at TIMESTAMPTZ NULL,
        ADD COLUMN IF NOT EXISTS virus_scanned_at TIMESTAMPTZ NULL,
        ADD COLUMN IF NOT EXISTS scan_result SMALLINT NOT NULL DEFAULT 0;
    CREATE INDEX IF NOT EXISTS ix_trade_attachments_expires_sweep
        ON trading.trade_attachments (expires_at)
        WHERE is_active = true AND expires_at IS NOT NULL;
    COMMIT;
```

All three migrations are idempotent (`IF NOT EXISTS` / `ADD COLUMN IF NOT EXISTS`). They can be re-run safely. Wave 4c (Realtime) has no migration — only code + frontend.

## Sequence Diagram (QuoteBroadcast iteration)

```
Timer fires (5s + jitter)
  ↓
QuoteBroadcastService.ExecuteAsync
  ↓
ActiveSubscriptions.Snapshot() → ["EURUSD", "GBPJPY", "BTCUSD"]
  ↓
CreateAsyncScope
  ↓
IQuoteProvider.GetQuotesAsync(["EURUSD", "GBPJPY", "BTCUSD"]) → 3 Quotes
  ↓
For each Quote:
  ├─ Skip if unchanged from last tick (Bid/Ask/Volume24h match)
  ├─ QuoteCacheRepository.UpsertAsync(quote)  ← write-through cache
  └─ IHubContext<QuoteHub, IQuoteClient>.Clients.Group("symbol-{S}").OnQuoteUpdate(quote)
  ↓
Dispose scope
  ↓
Sleep until next iteration
```

## Key Files to Be Created (slice breakdown)

**Slice 4a (Scanner) ~700 líneas**:
- Backend: `Shared.Kernel/Enums/VolatilityWindow.cs`, `Shared.Kernel/Enums/ActiveHoursWindow.cs`, `Trading.Domain/Scanner/ScannerFilter.cs`, `Trading.Application/Features/Scanner/*` (6 handlers), `ScannerFilterConfiguration`, `ScannerFilterRepository`, `ScannerRunner`, `MapScannerEndpoints`, migration `0017_scanner_filters.sql`.
- Frontend: `scanner-page.ts`, `scanner.service.ts`, `scanner.state.ts`, `__tests__/scanner-page.spec.ts` (4 specs).
- Tests: ~15 unit/integration tests.

**Slice 4b (MarketData) ~600 líneas**:
- Backend: `Shared.Kernel/MarketData/Quote.cs`, `Shared.Kernel/MarketData/IQuoteProvider.cs`, `Shared.Kernel/MarketData/QuoteSource.cs`, `Trading.Infrastructure/MarketData/InMemoryQuoteProvider.cs`, `QuoteCacheRepository.cs`, `Trading.Application/MarketData/GetQuoteHandler.cs`, `GetQuotesBulkHandler.cs`, `RefreshQuoteCacheHandler.cs`, `QuoteEndpoints.cs`, migration `0016_quotes_cache.sql`.
- Frontend: `quotes.service.ts` (HTTP wrapper).
- Tests: ~10 unit/integration tests.

**Slice 4c (Realtime) ~700 líneas**:
- Backend: `Trading.Api/Hubs/QuoteHub.cs`, `Trading.Infrastructure/Realtime/QuoteBroadcastService.cs`, `ActiveSubscriptions.cs`, `Program.cs` SignalR wire, modified `CurrentPriceNearStopRule.cs`.
- Frontend: `@microsoft/signalr` package, `quotes-signalr.service.ts`, `quotes.state.ts`, `jcs-watchlist.component.ts` + SCSS, dashboard integration, scanner-page modifications (optional — show live quotes).
- Tests: ~12 unit/integration tests.

**Slice 4d (Attachments) ~500 líneas**:
- Backend: `Shared.Kernel/Storage/AttachmentQuota.cs`, `Shared.Kernel/Storage/IVirusScanner.cs`, `Trading.Infrastructure/Storage/VirusScannerNoOp.cs`, `MinioAttachmentStore.GetThumbnailUrlAsync` extension, `AttachmentLifecycleService.cs`, `AttachmentQuotaEnforcer.cs`, `AttachmentUsageQuery.cs`, `GetThumbnailHandler.cs`, `MapAttachmentEndpoints.cs` (thumbnail + usage), migration `0018_attachment_lifecycle.sql`.
- Frontend: `attachments.service.ts` (usage endpoint), storage indicator UI on trades-list page.
- Tests: ~10 unit/integration tests.

**Slice 4e (E2E) ~300 líneas**:
- Mobile-nav update (`trader-shell.ts` adds Scanner entry).
- Smoke E2E from Tailscale.
- Tasks close + verify-report.