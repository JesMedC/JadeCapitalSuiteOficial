using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Enums;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Trades.GetTrades;

public sealed record GetTradesQuery(
    Guid UserId,
    int Page = 1,
    int PageSize = 20,
    TradeStatus? StatusFilter = null,
    string? SymbolFilter = null,
    Guid? AccountIdFilter = null) : IRequest<Result<PagedTradesDto>>;

public sealed class GetTradesValidator : AbstractValidator<GetTradesQuery>
{
    public const int MaxPageSize = 100;

    public GetTradesValidator()
    {
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);

        // AccountIdFilter no vacio cuando se pasa.
        When(x => x.AccountIdFilter is not null, () =>
        {
            RuleFor(x => x.AccountIdFilter!.Value).NotEqual(Guid.Empty);
        });
    }
}