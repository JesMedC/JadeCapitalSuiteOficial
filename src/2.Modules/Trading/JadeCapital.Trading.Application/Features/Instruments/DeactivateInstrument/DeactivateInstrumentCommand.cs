using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Instruments.DeactivateInstrument;

/// <summary>
/// Desactiva un instrumento. Los trades existentes siguen siendo accesibles;
/// solo bloquea su uso para operaciones futuras. Sin ownership (es global).
/// </summary>
public sealed record DeactivateInstrumentCommand(
    Guid InstrumentId) : IRequest<Result<InstrumentDto>>;

public sealed class DeactivateInstrumentValidator : AbstractValidator<DeactivateInstrumentCommand>
{
    public DeactivateInstrumentValidator()
    {
        RuleFor(x => x.InstrumentId).NotEqual(Guid.Empty);
    }
}
