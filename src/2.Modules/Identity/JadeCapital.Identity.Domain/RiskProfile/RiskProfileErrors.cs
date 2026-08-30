using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Domain.RiskProfile;

/// <summary>
/// Errores semanticos del aggregate <c>RiskProfile</c>.
/// Convencion: codigo = "validation.risk_profile.{detalle}" o
/// "notfound.risk_profile.{detalle}" para que el ProblemFromResult
/// del endpoint los enrute correctamente (400/404 o 422 segun el prefix).
/// </summary>
public static class RiskProfileErrors
{
    /// <summary>Capital debe ser positivo; el VO Money devuelve su propio error si el amount esta fuera del rango NUMERIC(24,8).</summary>
    public static readonly Error CapitalOutOfRange =
        Error.Validation("risk_profile.capital_amount_invalid", "Capital amount must be greater than zero.");

    /// <summary>El handler GetActive devuelve esto cuando la coleccion devuelve null.</summary>
    public static readonly Error NotFound =
        Error.NotFound("risk_profile.not_found", "No active risk profile was found for this user.");

    /// <summary>El handler CreateOrSupersede devuelve esto cuando el supersede fallo (e.g. race con otro request del mismo usuario).</summary>
    public static readonly Error ConcurrentSupersede =
        Error.Conflict("risk_profile.concurrent_supersede", "Another risk profile update is in progress for this user.");
}
