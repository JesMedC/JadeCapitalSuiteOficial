using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.TradeReviews;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.TradeReviews.CreateOrUpdate;

/// <summary>
/// Crea o actualiza el post-trade review de un trade cerrado. Upsert por
/// <c>trade_id</c>: si ya existe un review, lo actualiza in-place
/// (setup/lessons/rating). El emotionality del review pre-existente se
/// preserva (es inmutable — ver <c>TradeReview.Update</c> en el domain).
///
/// Si el trade es Open o Cancelled, falla con 409
/// <c>conflict.trade_review.trade_not_closed</c>. Si el trade no existe
/// o pertenece a otro usuario, falla con 404 (cross-user scope).
///
/// Se mapea a <see cref="Result{TradeReviewDto}"/>: el FE recibe el
/// review con attachments inline.
/// </summary>
public sealed record CreateOrUpdateTradeReviewCommand(
    Guid TradeId,
    Guid UserId,
    byte Emotionality,
    byte? Rating,
    string? SetupUsed,
    string? Lessons) : IRequest<Result<TradeReviewDto>>;

public sealed class CreateOrUpdateTradeReviewValidator : AbstractValidator<CreateOrUpdateTradeReviewCommand>
{
    public CreateOrUpdateTradeReviewValidator()
    {
        RuleFor(x => x.TradeId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.Emotionality).InclusiveBetween((byte)1, (byte)5);
        RuleFor(x => x.Rating)
            .Must(r => r is null || (r >= 1 && r <= 5))
            .WithMessage("Rating must be between 1 and 5 when present.");
        RuleFor(x => x.SetupUsed).MaximumLength(TradeReview.MaxSetupUsedLength);
        RuleFor(x => x.Lessons).MaximumLength(TradeReview.MaxLessonsLength);
    }
}

public sealed class CreateOrUpdateTradeReviewHandler
    : IRequestHandler<CreateOrUpdateTradeReviewCommand, Result<TradeReviewDto>>
{
    private readonly ITradeReviewRepository _reviews;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateOrUpdateTradeReviewHandler(
        ITradeReviewRepository reviews,
        IUnitOfWork uow,
        IClock clock)
    {
        _reviews = reviews;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<TradeReviewDto>> Handle(
        CreateOrUpdateTradeReviewCommand req,
        CancellationToken ct)
    {
        // Cross-user scope + Closed gate: el handler no confia en que el FE
        // envie un tradeId valido del usuario actual. ITradeReviewRepository
        // .FindByTradeIdAsync ya filtra por userId, pero el close-gate es
        // una validacion separada del estado del trade.
        var tradeIsClosed = await _reviews.TradeIsClosedAsync(req.TradeId, req.UserId, ct);
        if (!tradeIsClosed)
        {
            // El trade no existe o no es del user: ambos casos colapsan
            // a 404 (cross-user scope: no revelamos la diferencia entre
            // "no existe" y "no es tuyo").
            return Result.Failure<TradeReviewDto>(
                JadeCapital.Trading.Domain.Common.TradingDomainErrors.TradeReview.NotFound);
        }

        var now = _clock.UtcNow;

        // Upsert: si ya existe review para este trade, actualizamos;
        // sino, creamos uno nuevo.
        var existing = await _reviews.FindByTradeIdAsync(req.TradeId, req.UserId, ct);

        TradeReview review;
        if (existing is null)
        {
            var createResult = TradeReview.Create(
                id: Guid.NewGuid(),
                tradeId: req.TradeId,
                userId: req.UserId,
                emotionality: req.Emotionality,
                rating: req.Rating,
                setupUsed: req.SetupUsed,
                lessons: req.Lessons,
                tradeIsClosed: true,
                now: now);

            if (createResult.IsFailure)
                return Result.Failure<TradeReviewDto>(createResult.Error);

            review = createResult.Value;
            await _reviews.AddAsync(review, ct);
        }
        else
        {
            var updateResult = existing.Update(
                setupUsed: req.SetupUsed,
                lessons: req.Lessons,
                rating: req.Rating,
                now: now);
            if (updateResult.IsFailure)
                return Result.Failure<TradeReviewDto>(updateResult.Error);

            review = existing;
            await _reviews.UpdateAsync(review, ct);
        }

        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure<TradeReviewDto>(saved.Error);

        // Sin attachments todavia en el upsert path tipico, pero devolvemos
        // la lista vacia para mantener la forma consistente.
        return Result.Success(review.ToDto(Array.Empty<TradeAttachmentDto>()));
    }
}
