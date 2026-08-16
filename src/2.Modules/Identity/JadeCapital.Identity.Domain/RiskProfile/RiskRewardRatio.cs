using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Domain.RiskProfile;

/// <summary>
/// Target de ratio riesgo/beneficio. Invariante: <c>&gt;= 1.0</c>.
///
/// Por debajo de 1.0 el sistema indicaria al trader que busca un objetivo
/// peyor que 1:1 (p.ej. 0.8:1), lo cual no es disciplina sino
/// "pago premium por ganar menos de lo que arriesgo". Un trader serio
/// nunca configura esto por debajo de 1.0 como objetivo.
///
/// Invariante de dominio enforce aqui; redundada en el CHECK
/// <c>ck_risk_profiles_risk_reward_target_min</c> a nivel DB.
/// </summary>
public sealed class RiskRewardRatio : ValueObject
{
    public decimal Value { get; }

    public const decimal MinValue = 1.0m;

    private RiskRewardRatio(decimal value) { Value = value; }

    public static Result<RiskRewardRatio> Create(decimal value)
    {
        if (value < MinValue)
            return Result.Failure<RiskRewardRatio>(Error.Validation(
                "risk_profile.risk_reward_target_below_one",
                $"Risk-reward target must be greater than or equal to {MinValue:0.0}."));

        return Result.Success(new RiskRewardRatio(value));
    }

    /// <summary>Acceso directo sin validacion. SOLO para hydration desde DB / seed scripts.</summary>
    public static RiskRewardRatio FromTrusted(decimal value) => new(value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => $"1:{Value:0.00}";
}
