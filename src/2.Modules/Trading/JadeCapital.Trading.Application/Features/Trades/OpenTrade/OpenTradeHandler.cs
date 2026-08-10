using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Shared.Kernel.Validation;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Trades;
using JadeCapital.Trading.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Trades.OpenTrade;

/// <summary>
/// Abre un trade:
/// 1) Valida formato de Symbol/Currency (FluentValidator ya paso).
/// 2) Construye VOs (Symbol, Money) que revalidan formato y normalizan.
/// 3) Llama a Trade.Open (factory del dominio) que valida invariantes,
///    incluyendo AccountId/InstrumentId no vacios.
/// 4) Persiste via UnitOfWork.
/// </summary>
public sealed class OpenTradeHandler : IRequestHandler<OpenTradeCommand, Result<TradeDto>>
{
    private readonly ITradeRepository _trades;
    private readonly IAccountRepository _accounts;
    private readonly IInstrumentRepository _instruments;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<OpenTradeHandler> _logger;

    public OpenTradeHandler(
        ITradeRepository trades,
        IAccountRepository accounts,
        IInstrumentRepository instruments,
        IUnitOfWork uow,
        IClock clock,
        ILogger<OpenTradeHandler> logger)
    {
        _trades = trades;
        _accounts = accounts;
        _instruments = instruments;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<TradeDto>> Handle(OpenTradeCommand req, CancellationToken ct)
    {
        var symbolResult = Symbol.Create(req.Symbol);
        if (symbolResult.IsFailure)
            return Result.Failure<TradeDto>(symbolResult.Error);

        var volumeCurrencyResult = Currency.Create(req.VolumeCurrency);
        if (volumeCurrencyResult.IsFailure)
            return Result.Failure<TradeDto>(volumeCurrencyResult.Error);

        var entryPriceCurrencyResult = Currency.Create(req.EntryPriceCurrency);
        if (entryPriceCurrencyResult.IsFailure)
            return Result.Failure<TradeDto>(entryPriceCurrencyResult.Error);

        var volumeResult = Money.Create(req.Volume, volumeCurrencyResult.Value);
        if (volumeResult.IsFailure)
            return Result.Failure<TradeDto>(volumeResult.Error);

        var entryPriceResult = Money.Create(req.EntryPrice, entryPriceCurrencyResult.Value);
        if (entryPriceResult.IsFailure)
            return Result.Failure<TradeDto>(entryPriceResult.Error);

        var tradeId = Guid.NewGuid();
        var now = _clock.UtcNow;

        var openResult = Trade.Open(
            tradeId,
            req.AccountId,
            req.InstrumentId,
            req.UserId,
            symbolResult.Value,
            req.AssetClass,
            req.Direction,
            volumeResult.Value,
            entryPriceResult.Value,
            req.VolumeCurrency,
            req.Strategy,
            req.Notes,
            now);

        if (openResult.IsFailure)
            return Result.Failure<TradeDto>(openResult.Error);

        var trade = openResult.Value;

        await _trades.AddAsync(trade, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation("Trade {TradeId} opened for user {UserId}.", trade.Id, trade.UserId);

        // Hidratar accountName + instrument para el DTO (evita N+1 en el FE).
        var account = await _accounts.FindByIdAsync(trade.AccountId, ct);
        var instrument = await _instruments.FindByIdAsync(trade.InstrumentId, ct);

        return Result.Success(trade.ToDto(
            accountName: account?.Name,
            instrument: instrument is null ? null : new InstrumentSummaryDto(
                instrument.Symbol.Value,
                instrument.AssetClass,
                instrument.ContractSize,
                instrument.DecimalPlaces,
                instrument.PipValue,
                instrument.PayoutPercent)));
    }
}
