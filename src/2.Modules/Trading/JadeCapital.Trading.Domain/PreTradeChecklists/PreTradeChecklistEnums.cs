using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Domain.Common;

namespace JadeCapital.Trading.Domain.PreTradeChecklists;

/// <summary>
/// Estado emocional del trader al momento de abrir el trade. Escala 1..5
/// siguiendo la guia conventional de trader psychology: 1 = peor (Fearful),
/// 5 = mejor (Euphoric).
///
/// Se serializa como SMALLINT en la BD. El underlying byte coincide
/// 1-a-1 con el valor SMALLINT persistido — no usamos un offset porque
/// el rango valido (1..5) coincide exactamente con el byte range.
/// </summary>
public enum Emotionality : byte
{
    Fearful    = 1,
    Anxious    = 2,
    Neutral    = 3,
    Confident  = 4,
    Euphoric   = 5,
}

/// <summary>
/// Calidad del setup identificado por el trader al momento de abrir el
/// trade. Escala 1..5: 1 = Poor (peor), 5 = Excellent (mejor).
///
/// Persistido como SMALLINT, mismo underlying byte 1-a-1.
/// </summary>
public enum SetupQuality : byte
{
    Poor           = 1,
    BelowAverage   = 2,
    Average        = 3,
    Good           = 4,
    Excellent      = 5,
}

/// <summary>
/// Factorias tipadas para los enums del checklist. Validan que el valor
/// subyacente este dentro del rango 1..5 (rechaza 0 y 6+). Devuelven
/// <see cref="Result{T}"/> para que la capa de aplicacion pueda mapear
/// el error a 422 sin tener que try/catch.
///
/// Se exponen como metodos estaticos regulares (no extension methods)
/// porque C# no permite llamar `Emotionality.Create(1)` cuando el receiver
/// es un enum — la unica manera ergonomica seria extension methods con
/// `using static`, lo cual agrega un using extra en cada archivo.
/// Metodos estaticos directos son mas explicitos y menos magic.
/// </summary>
public static class PreTradeChecklistEnums
{
    public static Result<Emotionality> CreateEmotionality(byte value)
        => value is < 1 or > 5
            ? Result.Failure<Emotionality>(TradingDomainErrors.PreTradeChecklist.EmotionalityOutOfRange)
            : Result.Success((Emotionality)value);

    public static Result<SetupQuality> CreateSetupQuality(byte value)
        => value is < 1 or > 5
            ? Result.Failure<SetupQuality>(TradingDomainErrors.PreTradeChecklist.SetupQualityOutOfRange)
            : Result.Success((SetupQuality)value);
}

/// <summary>
/// Value Object inmutable con los datos del checklist submitted por el
/// trader. Se construye desde Application (no tiene constructor publico
/// de validacion; los componentes internos ya vienen validados o son
/// primitive types acotados).
///
/// Los rangos invalidos (emotionality fuera de 1..5, confluences fuera
/// de 1..10) los enforce el aggregate PreTradeChecklist.Create, no este
/// record. Esto evita logica duplicada entre VO y aggregate.
/// </summary>
public sealed record PreTradeChecklistSubmission(
    Emotionality Emotionality,
    SetupQuality SetupQuality,
    decimal RiskRewardAtEntry,
    decimal RiskRewardTargetUsed,
    byte ConfluencesCount);
