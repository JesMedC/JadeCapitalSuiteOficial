using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Accounts.OpenAccount;

/// <summary>
/// Abre una nueva cuenta de trading para el usuario autenticado.
/// Validacion Fluent: shape (largo de strings, rangos). Las invariantes de
/// negocio (currency valida, leverage positivo, payout en [0,1]) las
/// enforce el factory del dominio <see cref="Domain.Accounts.Account.Open"/>.
/// </summary>
public sealed record OpenAccountCommand(
    Guid UserId,
    string Name,
    string Broker,
    string Currency,
    decimal InitialBalance,
    decimal Leverage,
    decimal PayoutPercent) : IRequest<Result<AccountDto>>;

public sealed class OpenAccountValidator : AbstractValidator<OpenAccountCommand>
{
    public OpenAccountValidator()
    {
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Domain.Accounts.Account.MaxNameLength);
        RuleFor(x => x.Broker).NotEmpty().MaximumLength(Domain.Accounts.Account.MaxBrokerLength);
        // Currency.Create valida formato + whitelist; el handler la invoca
        // y devuelve el error canonico si falla. Acu'a solo shape.
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.InitialBalance).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.Leverage).GreaterThan(0m);
        RuleFor(x => x.PayoutPercent).InclusiveBetween(0m, 1m);
    }
}
