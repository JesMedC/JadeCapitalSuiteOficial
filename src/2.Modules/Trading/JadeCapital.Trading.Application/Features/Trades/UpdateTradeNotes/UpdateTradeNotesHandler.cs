using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Validation;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Trades;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Trades.UpdateTradeNotes;

/// <summary>
/// Actualiza la metadata opcional (strategy/notes) de un trade.
/// Falla si el trade esta Cancelled (estado terminal inmutable) — regla
/// enforced en el dominio via Trade.UpdateMetadata.
/// </summary>
public sealed class UpdateTradeNotesHandler : IRequestHandler<UpdateTradeNotesCommand, Result<TradeDto>>
{
    private readonly ITradeRepository _trades;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<UpdateTradeNotesHandler> _logger;

    public UpdateTradeNotesHandler(
        ITradeRepository trades,
        IUnitOfWork uow,
        ILogger<UpdateTradeNotesHandler> logger)
    {
        _trades = trades;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result<TradeDto>> Handle(UpdateTradeNotesCommand req, CancellationToken ct)
    {
        var trade = await _trades.FindByIdAsync(req.TradeId, ct);
        if (trade is null || trade.UserId != req.UserId)
            return Result.Failure<TradeDto>(TradingApplicationErrors.Trades.NotFound);

        var updateResult = trade.UpdateMetadata(req.Strategy, req.Notes);
        if (updateResult.IsFailure)
            return Result.Failure<TradeDto>(updateResult.Error);

        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation("Trade {TradeId} metadata updated.", trade.Id);

        return Result.Success(trade.ToDto());
    }
}