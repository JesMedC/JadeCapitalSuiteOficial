using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Trades.CloseTrade;

public sealed record CloseTradeCommand(
    Guid TradeId,
    Guid UserId,
    decimal ExitPrice,
    string ExitPriceCurrency) : IRequest<Result<TradeDto>>;

public sealed class CloseTradeValidator : AbstractValidator<CloseTradeCommand>
{
    public CloseTradeValidator()
    {
        RuleFor(x => x.TradeId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.ExitPrice).GreaterThan(0);
        RuleFor(x => x.ExitPriceCurrency).NotEmpty().Length(3);
    }
}