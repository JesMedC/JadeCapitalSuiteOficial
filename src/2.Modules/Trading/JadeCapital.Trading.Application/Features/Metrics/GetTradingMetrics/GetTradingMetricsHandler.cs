using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.Metrics;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Metrics.GetTradingMetrics;

/// <summary>
/// Handler del query de metricas. Orquestacion pura:
///  - Probe de existencia (devuelve NotFound si user ausente).
///  - Store de lectura (trades + conteos por periodo).
///  - Calculator puro (domain) que computa expectancy/profit-factor/SQN/etc.
///  - Proyeccion del resultado a DTO.
///
/// No toca EF ni DbContext — toda la materializacion vive en Infrastructure.
/// </summary>
public sealed class GetTradingMetricsHandler
    : IRequestHandler<GetTradingMetricsQuery, Result<MetricsDto>>
{
    private readonly IMetricsQueryStore _store;
    private readonly IUserExistenceProbe _users;
    private readonly IClock _clock;

    public GetTradingMetricsHandler(
        IMetricsQueryStore store,
        IUserExistenceProbe users,
        IClock clock)
    {
        _store = store;
        _users = users;
        _clock = clock;
    }

    public async Task<Result<MetricsDto>> Handle(GetTradingMetricsQuery req, CancellationToken ct)
    {
        if (!await _users.ExistsAsync(req.UserId, ct))
        {
            return Result.Failure<MetricsDto>(TradingDomainErrors.Metrics.UserNotFound);
        }

        var now = _clock.UtcNow;
        var from = req.Period.FromDate(now);

        var trades = await _store.ListAsync(req.UserId, from, ct);
        var (open, closed, total) = await _store.CountAsync(req.UserId, from, ct);

        var computed = MetricsCalculator.Compute(trades, open, closed, total, from, now);

        var dto = new MetricsDto(
            Period: req.Period.ToKey(),
            TotalTrades: computed.TotalTrades,
            TotalClosedTrades: computed.TotalClosedTrades,
            TotalOpenTrades: computed.TotalOpenTrades,
            WinRate: computed.WinRate,
            Expectancy: computed.Expectancy,
            ProfitFactor: computed.ProfitFactor,
            Payoff: computed.Payoff,
            Sqn: computed.Sqn,
            MaxDrawdown: computed.MaxDrawdown,
            MaxDrawdownAmount: computed.MaxDrawdownAmount,
            MaxDrawdownPercent: computed.MaxDrawdownPercent,
            EquityCurve: computed.EquityCurve
                .Select(p => new EquityPointDto(p.Timestamp, p.Equity, p.Drawdown))
                .ToList(),
            SymbolStats: computed.SymbolStats
                .Select(s => new SymbolStatDto(s.Symbol, s.Trades, s.TotalPnl, s.WinRate))
                .ToList(),
            Currency: computed.Currency);

        return Result.Success(dto);
    }
}
