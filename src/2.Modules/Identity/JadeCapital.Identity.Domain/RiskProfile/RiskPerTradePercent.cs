using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Domain.RiskProfile;

/// <summary>
/// Porcentaje de capital que el trader esta dispuesto a arriesgar en una sola
/// operacion. Rango cerrado: <c>[0.01, 5.00]</c> inclusive.
///
/// El limite inferior (0.01%) representa el piso practico: por debajo de eso
/// las diferencias son ruido de redondeo contra el NUMERIC(5,2) que
/// persistimos. El limite superior (5.00%) representa el techo aceptado
/// como disciplina seria: por encima de 5% el perfil deja de ser gestion
/// de riesgo y pasa a gambling.
///
/// Invariante de dominio enforce aqui; redundada en el CHECK
/// <c>ck_risk_profiles_risk_per_trade_range</c> a nivel DB.
/// </summary>
public sealed class RiskPerTradePercent : ValueObject
{
    public decimal Value { get; }

    public const decimal MinValue = 0.01m;
    public const decimal MaxValue = 5.00m;

    private RiskPerTradePercent(decimal value) { Value = value; }

    public static Result<RiskPerTradePercent> Create(decimal value)
    {
        if (value < MinValue || value > MaxValue)
            return Result.Failure<RiskPerTradePercent>(Error.Validation(
                "risk_profile.risk_per_trade_percent_out_of_range",
                $"Risk-per-trade percent must be between {MinValue:0.00} and {MaxValue:0.00}."));

        return Result.Success(new RiskPerTradePercent(value));
    }

    /// <summary>Acceso directo sin validacion. SOLO para hydration desde DB / seed scripts.</summary>
    public static RiskPerTradePercent FromTrusted(decimal value) => new(value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => $"{Value:0.00}%";
}
