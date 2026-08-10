using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Instruments.DeleteInstrument;

/// <summary>
/// Borra fisicamente un instrumento. Solo si no tiene trades asociados
/// (la FK en DB es RESTRICT). Pre-check via
/// <see cref="Application.Abstractions.ITradeRepository.ListByUserIdAndOpenedAtRangeAsync"/>
/// para devolver Conflict legible.
/// </summary>
public sealed record DeleteInstrumentCommand(
    Guid InstrumentId) : IRequest<Result<Unit>>;

public sealed class DeleteInstrumentValidator : AbstractValidator<DeleteInstrumentCommand>
{
    public DeleteInstrumentValidator()
    {
        RuleFor(x => x.InstrumentId).NotEqual(Guid.Empty);
    }
}
