using JadeCapital.Identity.Contracts.Projections;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Identity.Infrastructure.Persistence;

// ============================================================================
//  IdentityActiveUserIdsReader — slice 3b (Trader Strategies + Alerts + Planner).
//
//  EF Core implementation of <see cref="IActiveUserIdsReader"/>. Lives in
//  Identity.Infrastructure (NOT in Trading) so the Trading module keeps
//  zero references to Identity.Domain — it only depends on the narrow
//  Identity.Contracts projection.
//
//  Query: AsNoTracking + Select(Id) on Users WHERE Status = Active. Cheap
//  query (only the Id column) suitable for the 5-minute BackgroundService
//  tick. For very large user bases Wave 4+ can paginate or use a
//  stream-based reader.
// ============================================================================

public sealed class IdentityActiveUserIdsReader : IActiveUserIdsReader
{
    private readonly Persistence.IdentityDbContext _db;

    public IdentityActiveUserIdsReader(Persistence.IdentityDbContext db) { _db = db; }

    public async Task<IReadOnlyList<Guid>> ListActiveUserIdsAsync(CancellationToken ct)
    {
        var ids = await _db.Users
            .AsNoTracking()
            .Where(u => u.Status == Domain.Users.UserStatus.Active)
            .Select(u => u.Id)
            .ToListAsync(ct);
        return ids;
    }
}