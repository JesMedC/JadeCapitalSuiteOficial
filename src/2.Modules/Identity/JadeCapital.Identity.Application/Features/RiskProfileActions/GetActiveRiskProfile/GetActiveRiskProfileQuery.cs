using JadeCapital.Identity.Contracts.RiskProfiles;
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
///
/// El DTO se reuse desde <c>JadeCapital.Identity.Contracts.RiskProfiles</c>
/// para que la API publique y los consumidores cross-module vean el mismo
/// shape (siguiendo el patron de Wave 0 donde LoginResult vive en
/// Application y el body publico en su archivo al lado).
/// </summary>
public sealed record GetActiveRiskProfileQuery(Guid UserId)
    : IRequest<Result<RiskProfileDto>>;
