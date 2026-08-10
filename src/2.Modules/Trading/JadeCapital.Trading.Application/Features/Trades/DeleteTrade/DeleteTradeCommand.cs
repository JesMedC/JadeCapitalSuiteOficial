using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Trades.DeleteTrade;

public sealed record DeleteTradeCommand(
    Guid TradeId,
    Guid UserId) : IRequest<Result<Unit>>;

public sealed class DeleteTradeValidator : AbstractValidator<DeleteTradeCommand>
{
    public DeleteTradeValidator()
    {
        RuleFor(x => x.TradeId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
    }
}