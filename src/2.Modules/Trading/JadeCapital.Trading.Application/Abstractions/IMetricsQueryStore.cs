using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Read store para el read-model de metricas. Devuelve trades del usuario en
/// la ventana [from, +inf) (o TODOS si from == null), junto con los conteos por
/// estado. El handler pasa esos datos al MetricsCalculator (pure, domain)
/// que computa expectancy/profit-factor/SQN/drawdown/symbolStats.
///
/// La intencion es que el calculo NO toque EF ni DbContext — esto preserva
/// el patron "Application orquesta, Domain calcula, Infrastructure provee
/// datos" y mantiene al calculator 100% testeable.
///
/// Slice 1e (integration tests) ejercita la implementacion EF contra un
/// Postgres real.
/// </summary>
public interface IMetricsQueryStore
{
    /// <summary>
    /// Lista los trades del usuario en la ventana (OpenedAt &gt;= from).
    /// Si from es null, devuelve TODA la historia. Orden ascendente por OpenedAt.
    /// </summary>
    Task<IReadOnlyList<Trade>> ListAsync(
        Guid userId,
        DateTimeOffset? from,
        CancellationToken ct);

    /// <summary>
    /// Conteos desglosados por estado para el mismo filtro. Mas barato que
    /// Listar todos y contar en memoria, y permite al handler reportar el
    /// total real (incluyendo Cancelled, que MetricsCalculator ignora).
    /// </summary>
    Task<(int Open, int Closed, int Total)> CountAsync(
        Guid userId,
        DateTimeOffset? from,
        CancellationToken ct);
}

/// <summary>
/// Sonda de existencia de usuario. Placeholder para slice 1f: la implementacion
/// actual devuelve true siempre (la auth JWT ya garantiza identidad). Cuando
/// slice 1a introduzca IIdentityUserRiskProfileReader, esta sonda se conecta a
/// ese read-side (Identity.Infrastructure.IdentityUserExistenceProbe) sin
/// tocar el handler ni la API.
///
/// Por que existe: el handler debe poder distinguir "user autenticado sin
/// trades" (200 con metricas vacias) de "user no encontrado" (404). Sin la
/// sonda no hay forma de emitir 404 desde /api/trades/metrics.
/// </summary>
public interface IUserExistenceProbe
{
    Task<bool> ExistsAsync(Guid userId, CancellationToken ct);
}
