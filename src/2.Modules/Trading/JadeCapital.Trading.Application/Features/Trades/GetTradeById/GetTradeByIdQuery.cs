using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Trades.GetTradeById;

public sealed record GetTradeByIdQuery(
    Guid TradeId,
    Guid UserId) : IRequest<Result<TradeDto>>;