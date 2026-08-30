using JadeCapital.Identity.Contracts.Projections;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Identity.Infrastructure.Projections;

/// <summary>
/// EF Core implementation of <see cref="IIdentityUserRiskProfileReader"/>.
///
/// Slice 1a.1b. Vive en Infrastructure porque el assembly no expone el
/// aggregate <c>RiskProfile</c> al consumidor externo: solo las 4
/// properties del snapshot. Trading no necesita la forma completa;
/// satisfacer esa superficie aqui mantiene la direccion de dependencias
/// limpia (Trading =&gt; Identity.Contracts, NO Trading =&gt; Identity.Domain).
///
/// Query: AsNoTracking + ProjectTo-style; hits el indice NO-UNIQUE
/// <c>ix_risk_profiles_user</c> para encontrar cualquier perfil del
/// usuario, y filtra a IsActive=TRUE en LINQ. Para queries a gran escala,
/// podriamos promover a un GET-only DbContext sin eventos ni proxies.
/// </summary>
public sealed class IdentityUserRiskProfileReader : IIdentityUserRiskProfileReader
{
    private readonly Persistence.IdentityDbContext _db;

    public IdentityUserRiskProfileReader(Persistence.IdentityDbContext db) { _db = db; }

    public async Task<UserRiskProfileSnapshot?> GetActiveAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await _db.RiskProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.IsActive)
            .Select(p => new UserRiskProfileSnapshot(
                p.Capital.Amount,
                p.Capital.CurrencyCode,
                p.RiskPerTradePercent.Value,
                p.RiskRewardTarget.Value))
            .FirstOrDefaultAsync(ct);

        return profile;
    }
}
