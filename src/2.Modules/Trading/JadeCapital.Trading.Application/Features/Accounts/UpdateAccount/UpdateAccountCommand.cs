using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Accounts;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Accounts.UpdateAccount;

/// <summary>
/// Actualiza metadata (name, broker, currency, leverage, payout) de una
/// cuenta existente. El handler valida ownership antes de mutar.
/// InitialBalance NO se modifica (es derivado de trades + deposits; en 1.5A
/// lo ignoramos y queda como input de solo lectura).
/// </summary>
public sealed record UpdateAccountCommand(
    Guid AccountId,
    Guid UserId,
    string Name,
    string Broker,
    string Currency,
    decimal Leverage,
    decimal PayoutPercent) : IRequest<Result<AccountDto>>;

public sealed class UpdateAccountValidator : AbstractValidator<UpdateAccountCommand>
{
    public UpdateAccountValidator()
    {
        RuleFor(x => x.AccountId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Account.MaxNameLength);
        RuleFor(x => x.Broker).NotEmpty().MaximumLength(Account.MaxBrokerLength);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.Leverage).GreaterThan(0m);
        RuleFor(x => x.PayoutPercent).InclusiveBetween(0m, 1m);
    }
}
