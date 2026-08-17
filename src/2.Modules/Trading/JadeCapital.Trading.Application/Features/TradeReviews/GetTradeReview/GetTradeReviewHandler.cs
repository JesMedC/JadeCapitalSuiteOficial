using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.TradeReviews.GetTradeReview;

/// <summary>
/// Devuelve el review post-trade de un trade del usuario actual, con sus
/// attachments inline. Si el review no existe (aun), retorna 404 con
/// <c>notfound.trade_review.not_found</c>. Cross-user scope enforced via
/// <see cref="ITradeReviewRepository.FindByTradeIdAsync"/>.
/// </summary>
public sealed record GetTradeReviewQuery(Guid TradeId, Guid UserId)
    : IRequest<Result<TradeReviewDto>>;

public sealed class GetTradeReviewHandler
    : IRequestHandler<GetTradeReviewQuery, Result<TradeReviewDto>>
{
    private readonly ITradeReviewRepository _reviews;

    public GetTradeReviewHandler(ITradeReviewRepository reviews)
    {
        _reviews = reviews;
    }

    public async Task<Result<TradeReviewDto>> Handle(
        GetTradeReviewQuery req,
        CancellationToken ct)
    {
        var review = await _reviews.FindByTradeIdAsync(req.TradeId, req.UserId, ct);
        if (review is null)
            return Result.Failure<TradeReviewDto>(TradingDomainErrors.TradeReview.NotFound);

        var attachments = await _reviews.ListAttachmentsByReviewIdAsync(review.Id, req.UserId, ct);
        var attachmentDtos = attachments.Select(a => a.ToDto()).ToArray();

        return Result.Success(review.ToDto(attachmentDtos));
    }
}
