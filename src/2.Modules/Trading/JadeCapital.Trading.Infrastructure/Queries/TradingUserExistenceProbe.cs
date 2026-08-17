using JadeCapital.Trading.Application.Abstractions;

namespace JadeCapital.Trading.Infrastructure.Queries;

/// <summary>
/// Placeholder implementation de IUserExistenceProbe. Devuelve true siempre
/// porque el JWT bearer middleware ya garantiza que el user existe cuando
/// el handler corre (token valido -> user existe en identity.users).
///
/// Slice 1f no introduce una dependencia cruzada a Identity.Infrastructure;
/// cuando slice 1a introduzca IIdentityUserRiskProfileReader, esta clase se
/// reemplaza por un adapter real (o se elimina si el pipeline de auth ya
/// emite 404 desde un middleware anterior).
///
/// La interfaz se mantiene en Application para que el handler testeable
/// pueda mockearla con NSubstitute sin tocar la red ni Identity.
/// </summary>
public sealed class TradingUserExistenceProbe : IUserExistenceProbe
{
    public Task<bool> ExistsAsync(Guid userId, CancellationToken ct)
        => Task.FromResult(userId != Guid.Empty);
}
