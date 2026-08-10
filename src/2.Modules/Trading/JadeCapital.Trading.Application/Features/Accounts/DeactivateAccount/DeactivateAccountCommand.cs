using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Accounts.DeactivateAccount;

/// <summary>
/// Desactiva una cuenta del usuario. El historial (trades existentes)
/// permanece accesible: la desactivacion es logica, no fisica.
/// Falla con Conflict si ya estaba inactiva (regla enforced por el aggregate).
/// </summary>
public sealed record DeactivateAccountCommand(
    Guid AccountId,
    Guid UserId) : IRequest<Result<AccountDto>>;

public sealed class DeactivateAccountValidator : AbstractValidator<DeactivateAccountCommand>
{
    public DeactivateAccountValidator()
    {
        RuleFor(x => x.AccountId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
    }
}
