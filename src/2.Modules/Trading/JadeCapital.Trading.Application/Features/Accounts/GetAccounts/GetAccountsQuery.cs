using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Accounts.GetAccounts;

/// <summary>
/// Lista todas las cuentas del usuario autenticado. Ordenadas por CreatedAt
/// descendente (cuenta mas reciente primero).
/// </summary>
public sealed record GetAccountsQuery(
    Guid UserId) : IRequest<Result<IReadOnlyList<AccountDto>>>;

public sealed class GetAccountsValidator : AbstractValidator<GetAccountsQuery>
{
    public GetAccountsValidator()
    {
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
    }
}
