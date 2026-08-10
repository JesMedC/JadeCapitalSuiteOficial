using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Accounts.ReactivateAccount;

/// <summary>
/// Reactiva una cuenta previamente desactivada. Falla con Conflict si ya
/// estaba activa (regla enforced por el aggregate).
/// </summary>
public sealed record ReactivateAccountCommand(
    Guid AccountId,
    Guid UserId) : IRequest<Result<AccountDto>>;

public sealed class ReactivateAccountValidator : AbstractValidator<ReactivateAccountCommand>
{
    public ReactivateAccountValidator()
    {
        RuleFor(x => x.AccountId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
    }
}
