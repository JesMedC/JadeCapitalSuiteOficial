using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Domain.Common;

namespace JadeCapital.Trading.Domain.TradeReviews;

/// <summary>
/// Aggregate Root del post-trade review (slice 1d.1).
///
/// Reglas de negocio:
/// <list type="bullet">
///   <item>UN review por trade — enforced por UNIQUE INDEX sobre trade_id
///   en la DB. Intentar crear un segundo review devuelve 409 con
///   <c>trade_review.already_exists</c>.</item>
///   <item>Solo trades cerrados pueden ser reviewed. El handler de aplicacion
///   resuelve el trade (incluyendo su status) y le pasa un flag
///   <c>tradeIsClosed</c> al factory; si el trade sigue Open/Cancelled, el
///   factory rechaza con <c>trade_review.trade_not_closed</c>.</item>
///   <item>El review es mutable: <see cref="Update"/> permite corregir setup,
///   lessons o rating despues de creado. <c>emotionality</c> se congela en
///   Create (el sentimiento al cierre NO cambia retroactivamente).</item>
///   <item>El cross-user scope se enforce desde Application (el handler
///   rechaza con 404 si el trade no pertenece al userId autenticado, antes
///   de llegar al factory).</item>
/// </list>
/// </summary>
public sealed class TradeReview : AggregateRoot<Guid>
{
    /// <summary>Max length del setup tag (libre, <= 64 chars).</summary>
    public const int MaxSetupUsedLength = 64;

    /// <summary>Max length del lessons text (<= 5000 chars).</summary>
    public const int MaxLessonsLength = 5000;

    public Guid TradeId { get; private set; }
    public Guid UserId { get; private set; }
    public ReviewEmotionality Emotionality { get; private set; }
    public string? SetupUsed { get; private set; }
    public string? Lessons { get; private set; }
    public byte? Rating { get; private set; }

    // EF Core.
    private TradeReview() { }

    private TradeReview(
        Guid id,
        Guid tradeId,
        Guid userId,
        ReviewEmotionality emotionality,
        string? setupUsed,
        string? lessons,
        byte? rating,
        DateTimeOffset createdAt) : base(id)
    {
        TradeId = tradeId;
        UserId = userId;
        Emotionality = emotionality;
        SetupUsed = setupUsed;
        Lessons = lessons;
        Rating = rating;
        SetCreatedAt(createdAt);
        UpdatedAt = createdAt;
    }

    /// <summary>
    /// Construye un review para un trade cerrado. Validaciones:
    /// <list type="number">
    ///   <item>id/tradeId/userId no son Guid.Empty (defensa en profundidad;
    ///   la FK a trades/users es la red final).</item>
    ///   <item><paramref name="tradeIsClosed"/> == true (sino 409 con
    ///   <c>trade_review.trade_not_closed</c>). El handler es responsable
    ///   de pasar el flag correcto — el agregado no carga el Trade.</item>
    ///   <item><c>emotionality</c> ∈ [1, 5] (factory-driven; byte directo
    ///   via <see cref="TradeReviewEnums.CreateReviewEmotionality"/>).</item>
    ///   <item><c>rating</c> ∈ [1, 5] o null.</item>
    ///   <item><c>setupUsed</c> <= 64 chars (trimmed).</item>
    ///   <item><c>lessons</c> <= 5000 chars.</item>
    /// </list>
    /// En exito emite un <see cref="TradeReviewCreatedDomainEvent"/>.
    /// </summary>
    public static Result<TradeReview> Create(
        Guid id,
        Guid tradeId,
        Guid userId,
        byte emotionality,
        byte? rating,
        string? setupUsed,
        string? lessons,
        bool tradeIsClosed,
        DateTimeOffset now)
    {
        if (id == Guid.Empty)
            return Result.Failure<TradeReview>(TradingDomainErrors.TradeReview.IdRequired);

        if (tradeId == Guid.Empty)
            return Result.Failure<TradeReview>(TradingDomainErrors.TradeReview.TradeIdRequired);

        if (userId == Guid.Empty)
            return Result.Failure<TradeReview>(TradingDomainErrors.TradeReview.UserIdRequired);

        if (!tradeIsClosed)
            return Result.Failure<TradeReview>(TradingDomainErrors.TradeReview.TradeNotClosed);

        var emotionalityResult = TradeReviewEnums.CreateReviewEmotionality(emotionality);
        if (emotionalityResult.IsFailure)
            return Result.Failure<TradeReview>(emotionalityResult.Error);

        if (rating is not null && (rating < 1 || rating > 5))
            return Result.Failure<TradeReview>(TradingDomainErrors.TradeReview.RatingOutOfRange);

        var normalizedSetup = NormalizeText(setupUsed);
        if (normalizedSetup is { Length: > MaxSetupUsedLength })
            return Result.Failure<TradeReview>(TradingDomainErrors.TradeReview.SetupUsedTooLong);

        var normalizedLessons = NormalizeText(lessons);
        if (normalizedLessons is { Length: > MaxLessonsLength })
            return Result.Failure<TradeReview>(TradingDomainErrors.TradeReview.LessonsTooLong);

        var review = new TradeReview(
            id, tradeId, userId,
            emotionalityResult.Value,
            normalizedSetup,
            normalizedLessons,
            rating,
            now);

        review.RaiseDomainEvent(new TradeReviewCreatedDomainEvent(
            review.Id, review.TradeId, review.UserId, now));

        return Result.Success(review);
    }

    /// <summary>
    /// Edita setup/lessons/rating de un review existente. <c>emotionality</c>
    /// se preserva (el sentimiento al cierre es inmutable). Falla con 422
    /// en los mismos rangos que Create.
    /// </summary>
    public Result Update(
        string? setupUsed,
        string? lessons,
        byte? rating,
        DateTimeOffset now)
    {
        if (rating is not null && (rating < 1 || rating > 5))
            return Result.Failure(TradingDomainErrors.TradeReview.RatingOutOfRange);

        var normalizedSetup = NormalizeText(setupUsed);
        if (normalizedSetup is { Length: > MaxSetupUsedLength })
            return Result.Failure(TradingDomainErrors.TradeReview.SetupUsedTooLong);

        var normalizedLessons = NormalizeText(lessons);
        if (normalizedLessons is { Length: > MaxLessonsLength })
            return Result.Failure(TradingDomainErrors.TradeReview.LessonsTooLong);

        SetupUsed = normalizedSetup;
        Lessons = normalizedLessons;
        Rating = rating;
        UpdatedAt = now;

        return Result.Success();
    }

    private static string? NormalizeText(string? input)
        => string.IsNullOrWhiteSpace(input) ? null : input.Trim();
}

/// <summary>
/// Domain event emitido cuando un review post-trade es creado por primera
/// vez. NO incluye PII ni contenido del review: solo identificadores y el
/// timestamp. Los consumidores downstream (e.g. un sistema de journaling
/// que arme un digest semanal) pueden leer el review por id.
/// </summary>
public sealed record TradeReviewCreatedDomainEvent(
    Guid ReviewId,
    Guid TradeId,
    Guid UserId,
    DateTimeOffset OccurredOn) : IDomainEvent;
