using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.ValueObjects;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Instruments.UpdateInstrument;

/// <summary>
/// Actualiza metadata de un instrumento. Sin ownership (es global). El symbol
/// puede cambiar, pero la constraint UNIQUE en la columna Symbol dispararia
/// en DB ante un duplicado post-update: para V1 no prevenimos porque el caso
/// de uso "rename a symbol" es raro y la DB es la red de seguridad.
/// </summary>
public sealed record UpdateInstrumentCommand(
    Guid InstrumentId,
    string Symbol,
    AssetClass AssetClass,
    decimal ContractSize,
    int DecimalPlaces,
    decimal PipValue,
    decimal PayoutPercent) : IRequest<Result<InstrumentDto>>;

public sealed class UpdateInstrumentValidator : AbstractValidator<UpdateInstrumentCommand>
{
    public UpdateInstrumentValidator()
    {
        RuleFor(x => x.InstrumentId).NotEqual(Guid.Empty);
        RuleFor(x => x.Symbol).NotEmpty().MaximumLength(Symbol.MaxLength);
        RuleFor(x => x.ContractSize).GreaterThan(0m);
        RuleFor(x => x.DecimalPlaces).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PipValue).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.PayoutPercent).InclusiveBetween(0m, 1m);
    }
}
