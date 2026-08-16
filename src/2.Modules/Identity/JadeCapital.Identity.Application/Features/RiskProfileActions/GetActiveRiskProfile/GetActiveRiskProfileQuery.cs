using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Identity.Application.Features.RiskProfileActions.GetActiveRiskProfile;

/// <summary>
/// Query para obtener el perfil de riesgo activo del usuario. Devuelve
/// <c>Result&lt;RiskProfileDto&gt;</c> con un error <c>notfound.</c> si el
/// usuario no tiene perfil activo.
///
/// UserId se pasa en el command (mismo patron que recovery handlers) para
/// mantener la logica de aplicacion libre de acoplamiento a HttpContext.
/// </summary>
public sealed record GetActiveRiskProfileQuery(Guid UserId)
    : IRequest<Result<RiskProfileDto>>;

/// <summary>
/// DTO del perfil de riesgo del usuario para respuestas HTTP y consumidores
/// cross-module. Sigue la convencion de records inmutables (mismo patron
/// que <c>LoginResult</c> / <c>ChangePasswordResult</c> de Wave 0).
///
/// Money se aplana a <c>decimal</c> + <c>string currency</c> para que el
/// frontend no tenga que conocer el VO Money (del lado de Identity.Domain)
/// — solo la API publica (este DTO).
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
