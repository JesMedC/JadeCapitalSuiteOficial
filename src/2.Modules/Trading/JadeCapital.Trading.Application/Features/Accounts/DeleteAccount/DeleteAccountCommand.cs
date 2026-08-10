using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Accounts.DeleteAccount;

/// <summary>
/// Borra fisicamente una cuenta. Solo permite borrar si no tiene trades
/// asociados (la FK en DB es RESTRICT). Pre-check contra
/// <see cref="Application.Abstractions.ITradeRepository.CountByUserIdAsync"/>
/// para devolver Conflict legible en lugar de un DbUpdateException.
/// </summary>
public sealed record DeleteAccountCommand(
    Guid AccountId,
    Guid UserId) : IRequest<Result<Unit>>;

public sealed class DeleteAccountValidator : AbstractValidator<DeleteAccountCommand>
{
    public DeleteAccountValidator()
    {
        RuleFor(x => x.AccountId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
    }
}
