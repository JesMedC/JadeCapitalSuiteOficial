using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Domain.Common;

namespace JadeCapital.Trading.Domain.Journal;

/// <summary>
/// Estado emocional del trader en uno de los tres momentos temporales
/// del journal (pre / during / post market). Escala 1..5 siguiendo la
/// guia conventional de trader psychology: 1 = peor (Fearful),
/// 5 = mejor (Euphoric).
///
/// Se serializa como SMALLINT en la BD. El underlying <c>byte</c>
/// coincide 1-a-1 con el valor persistido — el dominio enforce el rango
/// exacto y la DB es la red de seguridad via CHECK constraint.
///
/// Value Object inmutable. <see cref="Create"/> valida rango;
/// <see cref="FromTrusted"/> es para hidratacion desde DB (donde el
/// valor ya paso por el CHECK de la DB al escribirse).
/// </summary>
public readonly record struct Mood
{
    /// <summary>Valor subyacente (1..5).</summary>
    public byte Value { get; }

    private Mood(byte value)
    {
        Value = value;
    }

    /// <summary>
    /// Construye un Mood validando el rango [1, 5]. Falla con
    /// <c>validation.journal.mood_out_of_range</c> cuando el byte
    /// esta fuera de rango.
    /// </summary>
    public static Result<Mood> Create(byte value)
        => value is < 1 or > 5
            ? Result.Failure<Mood>(TradingDomainErrors.Journal.MoodOutOfRange)
            : Result.Success(new Mood(value));

    /// <summary>
    /// Acceso directo sin validacion. Usar SOLO desde hidratacion de EF
    /// donde el valor ya fue persistido (y por lo tanto validado por
    /// el CHECK constraint de la DB).
    /// </summary>
    public static Mood FromTrusted(byte value) => new(value);

    /// <summary>
    /// Etiqueta humana del valor: 1=Fearful, 2=Anxious, 3=Neutral,
    /// 4=Confident, 5=Euphoric. Usado por el FE para renderizar los
    /// 5 botones del form.
    /// </summary>
    public string Label => Value switch
    {
        1 => "Fearful",
        2 => "Anxious",
        3 => "Neutral",
        4 => "Confident",
        5 => "Euphoric",
        _ => "Unknown",
    };
}
