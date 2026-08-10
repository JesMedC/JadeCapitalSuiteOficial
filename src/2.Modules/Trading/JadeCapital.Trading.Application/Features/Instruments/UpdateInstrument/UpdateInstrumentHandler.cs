using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Validation;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Instruments.UpdateInstrument;

/// <summary>
/// Actualiza metadata del instrumento. NotFound canonico si no existe.
/// Already inactive -&gt; Conflict via aggregate.
/// </summary>
public sealed class UpdateInstrumentHandler : IRequestHandler<UpdateInstrumentCommand, Result<InstrumentDto>>
{
    private readonly IInstrumentRepository _instruments;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<UpdateInstrumentHandler> _logger;

    public UpdateInstrumentHandler(
        IInstrumentRepository instruments,
        IUnitOfWork uow,
        ILogger<UpdateInstrumentHandler> logger)
    {
        _instruments = instruments;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result<InstrumentDto>> Handle(UpdateInstrumentCommand req, CancellationToken ct)
    {
        var instrument = await _instruments.FindByIdAsync(req.InstrumentId, ct);
        if (instrument is null)
            return Result.Failure<InstrumentDto>(TradingApplicationErrors.Instruments.NotFound);

        var updateResult = instrument.UpdateMetadata(
            req.Symbol,
            req.AssetClass,
            req.ContractSize,
            req.DecimalPlaces,
            req.PipValue,
            req.PayoutPercent);

        if (updateResult.IsFailure)
            return Result.Failure<InstrumentDto>(updateResult.Error);

        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation("Instrument {InstrumentId} metadata updated.", instrument.Id);

        return Result.Success(instrument.ToDto());
    }
}
