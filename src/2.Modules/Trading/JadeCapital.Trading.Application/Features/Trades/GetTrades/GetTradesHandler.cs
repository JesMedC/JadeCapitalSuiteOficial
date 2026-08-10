using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Trades.GetTrades;

/// <summary>
/// Lista paginada de trades del usuario con filtros opcionales (status, symbol).
/// Re-defiende el cap de PageSize para tolerar handlers invocados sin
/// FluentValidation (e.g. desde otros handlers).
/// </summary>
public sealed class GetTradesHandler : IRequestHandler<GetTradesQuery, Result<PagedTradesDto>>
{
    private const int MaxPageSize = 100;

    private readonly ITradeRepository _trades;

    public GetTradesHandler(ITradeRepository trades)
    {
        _trades = trades;
    }

    public async Task<Result<PagedTradesDto>> Handle(GetTradesQuery req, CancellationToken ct)
    {
        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, MaxPageSize);

        var items = await _trades.ListByUserIdAsync(
            req.UserId, page, pageSize, ct, req.StatusFilter, req.SymbolFilter);

        var total = await _trades.CountByUserIdAsync(
            req.UserId, ct, req.StatusFilter, req.SymbolFilter);

        var dtos = items.Select(t => t.ToDto()).ToList();
        return Result.Success(new PagedTradesDto(dtos, total, page, pageSize));
    }
}