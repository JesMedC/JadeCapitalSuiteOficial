using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.ValueObjects;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Instruments.CreateInstrument;

/// <summary>
/// Crea un nuevo instrumento. Instrument es global (no per-user), cualquier
/// usuario autenticado puede crearlo en V1 (futuro: solo admins).
/// Pre-check de duplicado de symbol via <see cref="IInstrumentRepository.FindBySymbolAsync"/>.
///
/// AssetClasses es un [Flags] enum (Forex=1, Crypto=2, Binary=4, Commodity=8, Other=16).
/// ContractSize / DecimalPlaces / PipValue / PayoutPercent son opcionales: si son
/// null, el handler aplica los defaults (1, 6, 0, 0.85).
/// </summary>
public sealed record CreateInstrumentCommand(
    string Symbol,
    AssetClass AssetClasses,
    decimal? ContractSize,
    int? DecimalPlaces,
    decimal? PipValue,
    decimal? PayoutPercent) : IRequest<Result<InstrumentDto>>;

public sealed class CreateInstrumentValidator : AbstractValidator<CreateInstrumentCommand>
{
    public CreateInstrumentValidator()
    {
        RuleFor(x => x.Symbol).NotEmpty().MaximumLength(Symbol.MaxLength);
        RuleFor(x => x.AssetClasses).NotEqual(AssetClass.None);
        RuleFor(x => x.ContractSize)
            .GreaterThan(0m)
            .When(x => x.ContractSize.HasValue);
        RuleFor(x => x.DecimalPlaces)
            .GreaterThanOrEqualTo(0)
            .When(x => x.DecimalPlaces.HasValue);
        RuleFor(x => x.PipValue)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.PipValue.HasValue);
        RuleFor(x => x.PayoutPercent)
            .InclusiveBetween(0m, 1m)
            .When(x => x.PayoutPercent.HasValue);
    }
}
