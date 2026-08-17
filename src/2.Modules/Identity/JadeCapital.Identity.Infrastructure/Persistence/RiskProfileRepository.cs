using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.RiskProfile;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Identity.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IRiskProfileRepository"/>.
///
/// Slice 1a.1b.
/// Atomicidad del supersede + add: ambos paths pasan por este repo y la
/// UoW de EF emite un solo <c>SaveChangesAsync</c> desde el handler. El
/// UNIQUE INDEX PARTIAL <c>ux_risk_profiles_user_active</c> en la DB
/// rechaza el segundo activo si la carrera pasa al nivel del commit.
/// </summary>
public sealed class RiskProfileRepository : IRiskProfileRepository
{
    private readonly IdentityDbContext _db;

    public RiskProfileRepository(IdentityDbContext db) { _db = db; }

    public async Task AddAsync(RiskProfile profile, CancellationToken ct = default)
        => await _db.RiskProfiles.AddAsync(profile, ct);

    public Task<RiskProfile?> GetActiveAsync(Guid userId, CancellationToken ct = default)
        => _db.RiskProfiles
            .FirstOrDefaultAsync(
                p => p.UserId == userId && p.IsActive,
                ct);

    public Task<RiskProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.RiskProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Result> MarkSupersededAsync(Guid id, IClock clock, CancellationToken ct = default)
    {
        var profile = await _db.RiskProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null)
            return Result.Failure(RiskProfileErrors.NotFound);

        // MarkSuperseded en el aggregate es idempotente. La traduccion del
        // failure del nivel DB (e.g. optimistic concurrency) la atrapa el
        // UoW en SaveChangesAsync y la handler mapea al ProblemFromResult
        // correspondiente.
        return profile.MarkSuperseded(clock);
    }
}
