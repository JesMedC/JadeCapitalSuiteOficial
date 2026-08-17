using JadeCapital.Identity.Domain.RiskProfile;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Identity.Application.Abstractions;

/// <summary>
/// Contrato de persistencia para el aggregate <c>RiskProfile</c>. Vive en
/// Application porque el aggregate de dominio no conoce EF; el binding
/// Implementation lives en <c>JadeCapital.Identity.Infrastructure</c>.
/// Single-active invariant se enforce:
/// <list type="number">
///   <item>A nivel dominio: el aggregate expone <c>MarkSuperseded</c>
///   idempotente y <c>Create</c> solo produce filas activas.</item>
///   <item>A nivel DB: el UNIQUE INDEX PARTIAL
///   <c>ux_risk_profiles_user_active</c> rechaza el segundo activo.</item>
///   <item>A nivel handler: el supersede + add ocurren en una sola UoW.
///   Este repositorio es la superficie que el handler usa para la
///   transaccion atomica.</item>
/// </list>
/// </summary>
public interface IRiskProfileRepository
{
    /// <summary>Anade un perfil al DbContext (no se persiste hasta <c>SaveChangesAsync</c>).</summary>
    Task AddAsync(RiskProfile profile, CancellationToken ct = default);

    /// <summary>Devuelve el perfil activo del usuario o null si no existe.</summary>
    Task<RiskProfile?> GetActiveAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Devuelve cualquier perfil por id (sin importar estado). Usado por proyecciones cross-module.</summary>
    Task<RiskProfile?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Marca el perfil dado como superseded. Devuelve Result.Failure si la
    /// operacion fallo (e.g. carrera con otro request del mismo usuario);
    /// el handler traduce failures a 409 conflict.
    /// </summary>
    Task<Result> MarkSupersededAsync(Guid id, IClock clock, CancellationToken ct = default);
}
