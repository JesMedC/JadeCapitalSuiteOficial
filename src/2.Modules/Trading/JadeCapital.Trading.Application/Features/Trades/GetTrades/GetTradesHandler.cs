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
    private readonly IAccountRepository _accounts;
    private readonly IInstrumentRepository _instruments;

    public GetTradesHandler(
        ITradeRepository trades,
        IAccountRepository accounts,
        IInstrumentRepository instruments)
    {
        _trades = trades;
        _accounts = accounts;
        _instruments = instruments;
    }

    public async Task<Result<PagedTradesDto>> Handle(GetTradesQuery req, CancellationToken ct)
    {
        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, MaxPageSize);

        var items = await _trades.ListByUserIdAsync(
            req.UserId, page, pageSize, ct, req.StatusFilter, req.SymbolFilter, req.AccountIdFilter);

        var total = await _trades.CountByUserIdAsync(
            req.UserId, ct, req.StatusFilter, req.SymbolFilter, req.AccountIdFilter);

        // Hidratar AccountName e Instrument (batch lookup para no hacer N+1)
        var accountIds = items.Select(t => t.AccountId).Distinct().ToList();
        var instrumentIds = items.Select(t => t.InstrumentId).Distinct().ToList();

        var accountsById = (await Task.WhenAll(
            accountIds.Select(async id => (id, await _accounts.FindByIdAsync(id, ct)))))
            .Where(x => x.Item2 is not null)
            .ToDictionary(x => x.id, x => x.Item2!);

        var instrumentsById = (await Task.WhenAll(
            instrumentIds.Select(async id => (id, await _instruments.FindByIdAsync(id, ct)))))
            .Where(x => x.Item2 is not null)
            .ToDictionary(x => x.id, x => x.Item2!);

        var dtos = items.Select(t =>
        {
            var account = accountsById.GetValueOrDefault(t.AccountId);
            var instrument = instrumentsById.GetValueOrDefault(t.InstrumentId);
            return t.ToDto(
                accountName: account?.Name,
                instrument: instrument is null ? null : new InstrumentSummaryDto(
                    instrument.Symbol.Value,
                    instrument.AssetClasses,
                    instrument.ContractSize,
                    instrument.DecimalPlaces,
                    instrument.PipValue,
                    instrument.PayoutPercent));
        }).ToList();

        return Result.Success(new PagedTradesDto(dtos, total, page, pageSize));
    }
}