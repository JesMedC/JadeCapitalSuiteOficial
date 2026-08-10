using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Shared.Kernel.Validation;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Instruments;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.Instruments.CreateInstrument;

/// <summary>
/// Crea un instrumento. Pre-check de symbol duplicado: si ya existe uno
/// con ese symbol canonico, devolvemos Conflict legible antes de intentar
/// el INSERT (la constraint UNIQUE en la columna Symbol es la red de
/// seguridad definitiva).
///
/// Defaults para los campos opcionales:
/// - ContractSize    = 1
/// - DecimalPlaces   = 6
/// - PipValue        = 0
/// - PayoutPercent   = 0.85 (85%, tipico para binarias)
/// </summary>
public sealed class CreateInstrumentHandler : IRequestHandler<CreateInstrumentCommand, Result<InstrumentDto>>
{
    private const decimal DefaultContractSize = 1m;
    private const int DefaultDecimalPlaces = 6;
    private const decimal DefaultPipValue = 0m;
    private const decimal DefaultPayoutPercent = 0.85m;

    private readonly IInstrumentRepository _instruments;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<CreateInstrumentHandler> _logger;

    public CreateInstrumentHandler(
        IInstrumentRepository instruments,
        IUnitOfWork uow,
        IClock clock,
        ILogger<CreateInstrumentHandler> logger)
    {
        _instruments = instruments;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<InstrumentDto>> Handle(CreateInstrumentCommand req, CancellationToken ct)
    {
        // Pre-check: si el symbol ya existe, devolvemos Conflict canonico.
        var existing = await _instruments.FindBySymbolAsync(req.Symbol, ct);
        if (existing is not null)
            return Result.Failure<InstrumentDto>(TradingApplicationErrors.Instruments.SymbolAlreadyExists);

        var instrumentId = Guid.NewGuid();

        var createResult = Instrument.Create(
            instrumentId,
            req.Symbol,
            req.AssetClasses,
            req.ContractSize ?? DefaultContractSize,
            req.DecimalPlaces ?? DefaultDecimalPlaces,
            req.PipValue ?? DefaultPipValue,
            req.PayoutPercent ?? DefaultPayoutPercent,
            _clock);

        if (createResult.IsFailure)
            return Result.Failure<InstrumentDto>(createResult.Error);

        var instrument = createResult.Value;

        await _instruments.AddAsync(instrument, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation("Instrument {InstrumentId} ({Symbol}) created.", instrument.Id, instrument.Symbol.Value);

        return Result.Success(instrument.ToDto());
    }
}
