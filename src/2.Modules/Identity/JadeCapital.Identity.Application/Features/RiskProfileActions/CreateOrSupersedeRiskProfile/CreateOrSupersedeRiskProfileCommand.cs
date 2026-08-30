using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Identity.Application.Features.RiskProfileActions.CreateOrSupersedeRiskProfile;

/// <summary>
/// Crea un nuevo perfil de riesgo para el usuario, supersediendo el activo
/// previo (si existe) en una sola transaccion.
///
/// Validacion al ingreso: el handler enforces los rangos del spec primero;
/// si algo falla, devuelve 422 antes de tocar el repo. La capa de persistencia
/// enforce el mismo invariante via CHECK + UNIQUE INDEX PARTIAL.
///
/// El UserId se pasa dentro del command (mismo patron que recovery handlers):
/// la capa API resuelve el claim NameIdentifier y lo setea hacia abajo,
/// manteniendo la logica de aplicacion libre de acoplamiento a ASP.NET.
/// </summary>
public sealed record CreateOrSupersedeRiskProfileCommand(
    Guid UserId,
    decimal CapitalAmount,
    string CapitalCurrency,
    decimal MaxDrawdownPercent,
    decimal RiskPerTradePercent,
    decimal RiskRewardTarget)
    : IRequest<Result<Guid>>;
