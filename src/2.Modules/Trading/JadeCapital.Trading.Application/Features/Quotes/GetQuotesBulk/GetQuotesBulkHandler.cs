using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.MarketData;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Quotes.GetQuotesBulk;

// GET /api/quotes?symbols=EURUSD,GBPJPY
public sealed record GetQuotesBulkQuery(IReadOnlyList<string> Symbols) : IRequest<Result<IReadOnlyList<QuoteDto>>>;

public sealed class GetQuotesBulkHandler : IRequestHandler<GetQuotesBulkQuery, Result<IReadOnlyList<QuoteDto>>>
{
    private static readonly TimeSpan FreshnessThreshold = TimeSpan.FromSeconds(30);

    private readonly IQuoteCacheRepository _cache;
    private readonly IQuoteProvider _provider;
    private readonly IClock _clock;

    public GetQuotesBulkHandler(IQuoteCacheRepository cache, IQuoteProvider provider, IClock clock)
    {
        _cache = cache;
        _provider = provider;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<QuoteDto>>> Handle(GetQuotesBulkQuery req, CancellationToken ct)
    {
        var normalized = req.Symbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalized.Count == 0)
        {
            return Result.Success<IReadOnlyList<QuoteDto>>(Array.Empty<QuoteDto>());
        }

        var cached = (await _cache.GetManyAsync(normalized, ct)).ToList();
        var cachedSymbols = cached.Select(q => q.Symbol).ToHashSet(StringComparer.Ordinal);

        var missing = normalized.Where(s => !cachedSymbols.Contains(s)).ToList();
        var stale = cached.Where(q => !IsFresh(q)).Select(q => q.Symbol).ToList();
        var mustFetch = missing.Concat(stale).Distinct(StringComparer.Ordinal).ToList();

        if (mustFetch.Count == 0)
        {
            return Result.Success<IReadOnlyList<QuoteDto>>(cached.Select(q => q.ToDto()).ToList());
        }

        var fresh = await _provider.GetQuotesAsync(mustFetch, ct);
        foreach (var q in fresh)
        {
            await _cache.UpsertAsync(q, ct);
        }

        var combined = cached
            .Where(q => IsFresh(q))
            .Concat(fresh)
            .GroupBy(q => q.Symbol)
            .Select(g => g.First())
            .Select(q => q.ToDto())
            .ToList();

        return Result.Success<IReadOnlyList<QuoteDto>>(combined);
    }

    private bool IsFresh(Quote quote)
    {
        var age = _clock.UtcNow - quote.Timestamp;
        return age >= TimeSpan.Zero && age <= FreshnessThreshold;
    }
}
