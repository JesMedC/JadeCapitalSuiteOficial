using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Domain.RiskProfile;

/// <summary>
/// Drawdown maximo que el trader esta dispuesto a tolerar en su cuenta.
/// Rango cerrado: <c>[0.00, 50.00]</c> inclusive.
///
/// El limite inferior (0.00%) es matematicamente la opcion "no acepto ninguna
/// perdida" — util para cuentas recien fondeadas donde el trader quiere
/// marcar la pausa manual. El limite superior (50.00%) es el techo aceptado
/// como disciplina seria: por encima de 50% un drawdown destruye
/// psicologicamente al trader y elimina la capacidad de recuperacion
/// (50% drawdown requiere un 100% gain para break-even).
///
/// Invariante de dominio enforce aqui; redundada en el CHECK
/// <c>ck_risk_profiles_max_drawdown_range</c> a nivel DB.
/// </summary>
public sealed class MaxDrawdownPercent : ValueObject
{
    public decimal Value { get; }

    public const decimal MinValue = 0.00m;
    public const decimal MaxValue = 50.00m;

    private MaxDrawdownPercent(decimal value) { Value = value; }

    public static Result<MaxDrawdownPercent> Create(decimal value)
    {
        if (value < MinValue || value > MaxValue)
            return Result.Failure<MaxDrawdownPercent>(Error.Validation(
                "risk_profile.max_drawdown_percent_out_of_range",
                $"Max-drawdown percent must be between {MinValue:0.00} and {MaxValue:0.00}."));

        return Result.Success(new MaxDrawdownPercent(value));
    }

    /// <summary>Acceso directo sin validacion. SOLO para hydration desde DB / seed scripts.</summary>
    public static MaxDrawdownPercent FromTrusted(decimal value) => new(value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => $"{Value:0.00}%";
}
