using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Validation;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Instruments.DeleteInstrument;

/// <summary>
/// Borra un instrumento. Pre-check contra trades (FK RESTRICT): si hay
/// trades que apuntan a este instrumento, devolvemos Conflict sin tocar la DB.
/// </summary>
public sealed class DeleteInstrumentHandler : IRequestHandler<DeleteInstrumentCommand, Result<Unit>>
{
    private readonly IInstrumentRepository _instruments;
    private readonly ITradeRepository _trades;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<DeleteInstrumentHandler> _logger;

    public DeleteInstrumentHandler(
        IInstrumentRepository instruments,
        ITradeRepository trades,
        IUnitOfWork uow,
        ILogger<DeleteInstrumentHandler> logger)
    {
        _instruments = instruments;
        _trades = trades;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(DeleteInstrumentCommand req, CancellationToken ct)
    {
        var instrument = await _instruments.FindByIdAsync(req.InstrumentId, ct);
        if (instrument is null)
            return Result.Failure<Unit>(TradingApplicationErrors.Instruments.NotFound);

        // Pre-check: cualquier trade apuntando a este instrumento bloquea el borrado.
        var tradeCount = await _trades.CountByInstrumentIdAsync(req.InstrumentId, ct);

        if (tradeCount > 0)
            return Result.Failure<Unit>(TradingApplicationErrors.Instruments.HasTrades);

        await _instruments.RemoveAsync(instrument, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation("Instrument {InstrumentId} deleted.", instrument.Id);

        return Result.Success(Unit.Value);
    }
}
