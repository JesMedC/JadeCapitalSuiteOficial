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
/// </summary>
public sealed record CreateInstrumentCommand(
    string Symbol,
    AssetClass AssetClass,
    decimal ContractSize,
    int DecimalPlaces,
    decimal PipValue,
    decimal PayoutPercent) : IRequest<Result<InstrumentDto>>;

public sealed class CreateInstrumentValidator : AbstractValidator<CreateInstrumentCommand>
{
    public CreateInstrumentValidator()
    {
        RuleFor(x => x.Symbol).NotEmpty().MaximumLength(Symbol.MaxLength);
        RuleFor(x => x.ContractSize).GreaterThan(0m);
        RuleFor(x => x.DecimalPlaces).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PipValue).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.PayoutPercent).InclusiveBetween(0m, 1m);
    }
}
