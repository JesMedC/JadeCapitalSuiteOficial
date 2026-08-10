using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Accounts;
using JadeCapital.Trading.Domain.Enums;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Accounts.UpdateAccount;

/// <summary>
/// Actualiza metadata (name, broker, market type, currency, leverage) de una
/// cuenta existente. El handler valida ownership antes de mutar.
/// InitialBalance NO se modifica (es derivado de trades + deposits; en 1.5A
/// lo ignoramos y queda como input de solo lectura).
/// </summary>
public sealed record UpdateAccountCommand(
    Guid AccountId,
    Guid UserId,
    string Name,
    string Broker,
    MarketType MarketType,
    string Currency,
    decimal? Leverage) : IRequest<Result<AccountDto>>;

public sealed class UpdateAccountValidator : AbstractValidator<UpdateAccountCommand>
{
    public UpdateAccountValidator()
    {
        RuleFor(x => x.AccountId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Account.MaxNameLength);
        RuleFor(x => x.Broker).NotEmpty().MaximumLength(Account.MaxBrokerLength);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.MarketType).IsInEnum();
        // Cross-field: Leverage requerido solo si MarketType == Forex.
        RuleFor(x => x.Leverage)
            .Must((cmd, lev) => cmd.MarketType == MarketType.Binary || (lev.HasValue && lev.Value > 0m))
            .WithMessage("Leverage is required for Forex accounts.");
    }
}
