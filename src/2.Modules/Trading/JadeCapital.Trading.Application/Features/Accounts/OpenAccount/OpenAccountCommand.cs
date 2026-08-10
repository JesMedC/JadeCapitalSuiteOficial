using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Enums;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Accounts.OpenAccount;

/// <summary>
/// Abre una nueva cuenta de trading para el usuario autenticado.
/// Validacion Fluent: shape (largo de strings, rangos). Las invariantes de
/// negocio (currency valida, leverage positivo segun MarketType) las
/// enforce el factory del dominio <see cref="Domain.Accounts.Account.Open"/>.
/// </summary>
public sealed record OpenAccountCommand(
    Guid UserId,
    string Name,
    string Broker,
    MarketType MarketType,
    string Currency,
    decimal InitialBalance,
    decimal? Leverage) : IRequest<Result<AccountDto>>;

public sealed class OpenAccountValidator : AbstractValidator<OpenAccountCommand>
{
    public OpenAccountValidator()
    {
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Domain.Accounts.Account.MaxNameLength);
        RuleFor(x => x.Broker).NotEmpty().MaximumLength(Domain.Accounts.Account.MaxBrokerLength);
        // Currency.Create valida formato + whitelist; el handler la invoca
        // y devuelve el error canonico si falla. Acá solo shape.
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.InitialBalance).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.MarketType).IsInEnum();
        // Cross-field: Leverage requerido solo si MarketType == Forex.
        RuleFor(x => x.Leverage)
            .Must((cmd, lev) => cmd.MarketType == MarketType.Binary || (lev.HasValue && lev.Value > 0m))
            .WithMessage("Leverage is required for Forex accounts.");
    }
}
