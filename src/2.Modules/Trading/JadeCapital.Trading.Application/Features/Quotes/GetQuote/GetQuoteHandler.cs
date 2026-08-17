using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.MarketData;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Quotes.GetQuote;

// GET /api/quotes/{symbol}
public sealed record GetQuoteQuery(string Symbol) : IRequest<Result<QuoteDto>>;

public sealed class GetQuoteHandler : IRequestHandler<GetQuoteQuery, Result<QuoteDto>>
{
    private static readonly TimeSpan FreshnessThreshold = TimeSpan.FromSeconds(30);

    private readonly IQuoteCacheRepository _cache;
    private readonly IQuoteProvider _provider;
    private readonly IClock _clock;

    public GetQuoteHandler(IQuoteCacheRepository cache, IQuoteProvider provider, IClock clock)
    {
        _cache = cache;
        _provider = provider;
        _clock = clock;
    }

    public async Task<Result<QuoteDto>> Handle(GetQuoteQuery req, CancellationToken ct)
    {
        var normalized = NormalizeSymbol(req.Symbol);
        if (normalized is null)
        {
            return Result.Failure<QuoteDto>(QuotesErrors.NotFound);
        }

        var cached = await _cache.GetAsync(normalized, ct);
        if (cached is not null && IsFresh(cached))
        {
            return Result.Success(cached.ToDto());
        }

        var fresh = await _provider.GetQuoteAsync(normalized, ct);
        if (fresh is null)
        {
            return Result.Failure<QuoteDto>(QuotesErrors.NotFound);
        }

        await _cache.UpsertAsync(fresh, ct);
        return Result.Success(fresh.ToDto());
    }

    private bool IsFresh(Quote quote)
    {
        var age = _clock.UtcNow - quote.Timestamp;
        return age >= TimeSpan.Zero && age <= FreshnessThreshold;
    }

    private static string? NormalizeSymbol(string? symbol)
        => string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();
}
