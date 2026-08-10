using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Enums;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Trades.OpenTrade;

public sealed record OpenTradeCommand(
    Guid UserId,
    Guid AccountId,
    Guid InstrumentId,
    string Symbol,
    AssetClass AssetClass,
    TradeDirection Direction,
    decimal Volume,
    string VolumeCurrency,
    decimal EntryPrice,
    string EntryPriceCurrency,
    string? Strategy,
    string? Notes) : IRequest<Result<TradeDto>>;

public sealed class OpenTradeValidator : AbstractValidator<OpenTradeCommand>
{
    public OpenTradeValidator()
    {
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.AccountId).NotEqual(Guid.Empty);
        RuleFor(x => x.InstrumentId).NotEqual(Guid.Empty);
        RuleFor(x => x.Symbol).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Volume).GreaterThan(0);
        RuleFor(x => x.VolumeCurrency).NotEmpty().Length(3);
        RuleFor(x => x.EntryPrice).GreaterThan(0);
        RuleFor(x => x.EntryPriceCurrency).NotEmpty().Length(3);
        RuleFor(x => x.Strategy).MaximumLength(80);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
