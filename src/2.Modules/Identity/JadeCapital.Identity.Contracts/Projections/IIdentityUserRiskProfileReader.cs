namespace JadeCapital.Identity.Contracts.Projections;

/// <summary>
/// Narrow, read-only projection of the user's active risk profile.
/// Designed for the Trading module to consume without depending on
/// <c>JadeCapital.Identity.Domain</c>.
///
/// Invariants:
/// <list type="bullet">
///   <item>Exposes ONLY the four fields the Trading calculator needs:
///   <see cref="UserRiskProfileSnapshot.CapitalAmount"/>,
///   <see cref="UserRiskProfileSnapshot.CapitalCurrency"/>,
///   <see cref="UserRiskProfileSnapshot.RiskPerTradePercent"/>, and
///   <see cref="UserRiskProfileSnapshot.RiskRewardTarget"/>. Notably,
///   <c>MaxDrawdownPercent</c> is NOT exposed (the calculator does not
///   need it; the pre-trade checklist does, but reads it via a different
///   seam).</item>
///   <item><c>Identity.Contracts</c> is shared across modules. The
///   interface deliberately does NOT depend on the Identity.Domain
///   <c>RiskProfile</c> aggregate: the implementation lives in
///   Identity.Infrastructure and projects to the four properties only.
///   Trading can NOT accidentally pull in <c>RiskProfile.Domain</c>.</item>
///   <item>Read-only: no mutation surface. Trading is allowed to observe;
///   persistence flows go through the Identity-owned endpoints.</item>
/// </list>
/// </summary>
public interface IIdentityUserRiskProfileReader
{
    /// <summary>
    /// Devuelve el perfil de riesgo activo del usuario, o <c>null</c> si no
    /// existe (usuario nunca hizo <c>PUT /api/risk-profile</c>). El caller
    /// (Trading) traduce <c>null</c> a 404 o lo trata como "no profile —
    /// use defaults", segun el contexto.
    /// </summary>
    Task<UserRiskProfileSnapshot?> GetActiveAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Snapshot narrow del perfil de riesgo que Trading consume. Cuatruple
/// explicito: el resto del perfil (drawdown, superseded state, timestamps)
/// NO esta disponible para este consumer; lo obtiene solo el endpoint
/// <c>GET /api/risk-profile</c> via <c>RiskProfileDto</c>.
/// </summary>
public sealed record UserRiskProfileSnapshot(
    decimal CapitalAmount,
    string CapitalCurrency,
    decimal RiskPerTradePercent,
    decimal RiskRewardTarget);
