using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Validation;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Instruments.DeactivateInstrument;

public sealed class DeactivateInstrumentHandler : IRequestHandler<DeactivateInstrumentCommand, Result<InstrumentDto>>
{
    private readonly IInstrumentRepository _instruments;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<DeactivateInstrumentHandler> _logger;

    public DeactivateInstrumentHandler(
        IInstrumentRepository instruments,
        IUnitOfWork uow,
        ILogger<DeactivateInstrumentHandler> logger)
    {
        _instruments = instruments;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result<InstrumentDto>> Handle(DeactivateInstrumentCommand req, CancellationToken ct)
    {
        var instrument = await _instruments.FindByIdAsync(req.InstrumentId, ct);
        if (instrument is null)
            return Result.Failure<InstrumentDto>(TradingApplicationErrors.Instruments.NotFound);

        var deactivateResult = instrument.Deactivate();
        if (deactivateResult.IsFailure)
            return Result.Failure<InstrumentDto>(deactivateResult.Error);

        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation("Instrument {InstrumentId} deactivated.", instrument.Id);

        return Result.Success(instrument.ToDto());
    }
}
