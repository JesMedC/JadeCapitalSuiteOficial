using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Validation;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Trades.DeleteTrade;

/// <summary>
/// Borra fisicamente un trade. Solo permite Open o Cancelled — los Closed
/// son registro historico y deben quedar en BD aunque el usuario los
/// "elimine" de su vista (futuro: soft-delete via flag).
/// </summary>
public sealed class DeleteTradeHandler : IRequestHandler<DeleteTradeCommand, Result<Unit>>
{
    private readonly ITradeRepository _trades;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<DeleteTradeHandler> _logger;

    public DeleteTradeHandler(
        ITradeRepository trades,
        IUnitOfWork uow,
        ILogger<DeleteTradeHandler> logger)
    {
        _trades = trades;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(DeleteTradeCommand req, CancellationToken ct)
    {
        var trade = await _trades.FindByIdAsync(req.TradeId, ct);
        if (trade is null || trade.UserId != req.UserId)
            return Result.Failure<Unit>(TradingApplicationErrors.Trades.NotFound);

        if (trade.Status == TradeStatus.Closed)
            return Result.Failure<Unit>(TradingApplicationErrors.Trades.CannotDeleteClosed);

        await _trades.RemoveAsync(trade, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation("Trade {TradeId} deleted by user {UserId}.", trade.Id, trade.UserId);

        return Result.Success(Unit.Value);
    }
}