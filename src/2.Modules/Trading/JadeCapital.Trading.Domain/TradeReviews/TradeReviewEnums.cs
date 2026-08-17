using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Domain.Common;

namespace JadeCapital.Trading.Domain.TradeReviews;

/// <summary>
/// Estado emocional del trader al momento de escribir el review post-trade.
/// SMALLINT 1..5 en la DB; el enum coincide 1-a-1 con el byte persistido.
///
/// El set es DISTINTO del <see cref="JadeCapital.Trading.Domain.PreTradeChecklists.Emotionality"/>
/// porque el pre-trade captura el momento DE ENTRAR al trade (fearful/euphoric)
/// y el post-trade captura el momento DE SALIR (confident/tilted/frustrated).
/// Mismo rango, semantica distinta: NO son intercambiables.
/// </summary>
public enum ReviewEmotionality : byte
{
    Confident  = 1,
    Calm       = 2,
    Anxious    = 3,
    Neutral    = 4,
    Tilted     = 5,
}

/// <summary>
/// Factorias tipadas para los enums del review post-trade. Validan que el
/// underlying byte cae dentro del rango valido (1..5 para emotionality).
/// Devuelven <see cref="Result{T}"/> para que Application mapee el error
/// a HTTP sin try/catch.
/// </summary>
public static class TradeReviewEnums
{
    public static Result<ReviewEmotionality> CreateReviewEmotionality(byte value)
        => value is < 1 or > 5
            ? Result.Failure<ReviewEmotionality>(TradingDomainErrors.TradeReview.EmotionalityOutOfRange)
            : Result.Success((ReviewEmotionality)value);
}

/// <summary>
/// Value Object inmutable con los datos del review post-trade enviado
/// por el trader. Application lo construye desde el request body (con
/// los tipos primitivos del JSON: byte para emotionality, short? para
/// rating, string? para setup/lessons) y se lo pasa al aggregate
/// <see cref="TradeReview"/> para validacion final y persistencia.
///
/// Los rangos (emotionality 1..5, rating 1..5 si presente, setup <= 64
/// chars, lessons <= 5000 chars) los enforce el aggregate en Create/Update.
/// Este VO es solo el carrier de datos sin logica.
/// </summary>
public sealed record TradeReviewSubmission(
    byte Emotionality,
    byte? Rating,
    string? SetupUsed,
    string? Lessons);
