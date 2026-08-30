namespace JadeCapital.Identity.Contracts.RiskProfiles;

/// <summary>
/// DTO publico del perfil de riesgo del usuario. Vive en
/// <c>JadeCapital.Identity.Contracts</c> para que el modulo API pueda
/// devolver el shape sin depender de <c>JadeCapital.Identity.Domain</c>
/// (que contiene los VOs y el aggregate). Mismo patron que
/// <c>RegisterUserResult</c> / <c>ChangePasswordResponse</c> en Wave 0.
///
/// Money se aplana a <c>decimal Amount</c> + <c>string CurrencyCode</c>;
/// las fechas son <c>DateTimeOffset</c> UTC. Solo lectura.
/// </summary>
public sealed record RiskProfileDto(
    Guid Id,
    Guid UserId,
    decimal CapitalAmount,
    string CapitalCurrency,
    decimal MaxDrawdownPercent,
    decimal RiskPerTradePercent,
    decimal RiskRewardTarget,
    bool IsActive,
    DateTimeOffset? SupersededAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Body de <c>PUT /api/risk-profile</c>. El UserId NO viene en el body: lo
/// resuelve el endpoint a partir del claim NameIdentifier del JWT
/// autenticado. La validacion de los rangos vive en
/// <c>UpsertRiskProfileValidator</c> (FluentValidation) y re-checkea en el
/// aggregate (defense in depth).
/// </summary>
public sealed record UpsertRiskProfileRequest(
    decimal CapitalAmount,
    string CapitalCurrency,
    decimal MaxDrawdownPercent,
    decimal RiskPerTradePercent,
    decimal RiskRewardTarget);
