using System.Collections.Concurrent;
using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Shared.Kernel.Realtime;
using JadeCapital.Trading.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Infrastructure.Realtime;

// ============================================================================
//  QuoteBroadcastService — slice 4c (Realtime) BackgroundService.
//
//  Polls IQuoteProvider every 5s (with jitter) and pushes ticks to the
//  SignalR group for each subscribed symbol. Change detection: skip a
//  quote whose Bid/Ask/Volume24h match the previous tick (prevents noise
//  when the stub provider returns identical ticks within a 5s window).
//
//  Failure isolation:
//   - Provider exceptions are caught and logged; the host NEVER crashes.
//   - HubContext exceptions per symbol are caught individually so one
//     dead group doesn't poison the others.
//
//  Connection cleanup: the Hub removes the connection from the registry
//  on disconnect; the broadcast loop reads the registry snapshot fresh
//  every tick, so cleanup is implicit.
// ============================================================================

public sealed class QuoteBroadcastService : BackgroundService
{
    public static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxJitter = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan StartupJitterMax = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<QuoteHub, IQuoteClient> _hubContext;
    private readonly IQuoteSubscriptionRegistry _registry;
    private readonly ILogger<QuoteBroadcastService> _logger;
    private readonly TimeSpan _period;
    private readonly Random _jitter = new();
    private readonly ConcurrentDictionary<string, Quote> _lastQuotes = new(StringComparer.OrdinalIgnoreCase);

    public QuoteBroadcastService(
        IServiceScopeFactory scopeFactory,
        IHubContext<QuoteHub, IQuoteClient> hubContext,
        IQuoteSubscriptionRegistry registry,
        ILogger<QuoteBroadcastService> logger,
        TimeSpan? period = null)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _registry = registry;
        _logger = logger;
        _period = period ?? DefaultPeriod;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial jitter (0..30s) to avoid thundering herd at startup.
        var startupDelay = TimeSpan.FromMilliseconds(_jitter.Next(0, (int)StartupJitterMax.TotalMilliseconds));
        try
        {
            await Task.Delay(startupDelay, stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BroadcastTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Belt-and-suspenders: tick-level failures must NEVER crash the host.
                _logger.LogError(ex, "QuoteBroadcastService tick failed");
            }

            try
            {
                var delayMs = (int)_period.TotalMilliseconds + _jitter.Next(-(int)MaxJitter.TotalMilliseconds, (int)MaxJitter.TotalMilliseconds);
                await Task.Delay(delayMs, stoppingToken);
            }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// Run a single broadcast iteration. Public to enable unit-test driving
    /// without depending on the BackgroundService loop timing.
    /// </summary>
    public async Task BroadcastTickAsync(CancellationToken ct)
    {
        var symbols = _registry.GetSubscribedSymbols();
        if (symbols.Count == 0) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IQuoteProvider>();
        var cache = scope.ServiceProvider.GetService<IQuoteCacheRepository>();

        IReadOnlyList<Quote> quotes;
        try
        {
            quotes = await provider.GetQuotesAsync(symbols, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QuoteBroadcastService: provider failed; skipping tick");
            return;
        }

        foreach (var quote in quotes)
        {
            if (_lastQuotes.TryGetValue(quote.Symbol, out var prev) && QuotesEqual(prev, quote))
                continue;

            _lastQuotes[quote.Symbol] = quote;

            if (cache is not null)
            {
                try { await cache.UpsertAsync(quote, ct); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "QuoteBroadcastService: cache upsert failed for {Symbol}", quote.Symbol);
                }
            }

            try
            {
                var update = QuoteUpdate.FromQuote(quote);
                await _hubContext.Clients.Group($"symbol-{quote.Symbol}").OnQuoteUpdate(update);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "QuoteBroadcastService: hub push failed for {Symbol}", quote.Symbol);
            }
        }
    }

    private static bool QuotesEqual(Quote a, Quote b) =>
        a.Bid == b.Bid && a.Ask == b.Ask && a.Volume24h == b.Volume24h;
}