using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Trades.UpdateTradeNotes;

public sealed record UpdateTradeNotesCommand(
    Guid TradeId,
    Guid UserId,
    string? Strategy,
    string? Notes) : IRequest<Result<TradeDto>>;

public sealed class UpdateTradeNotesValidator : AbstractValidator<UpdateTradeNotesCommand>
{
    public UpdateTradeNotesValidator()
    {
        RuleFor(x => x.TradeId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.Strategy).MaximumLength(80);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}