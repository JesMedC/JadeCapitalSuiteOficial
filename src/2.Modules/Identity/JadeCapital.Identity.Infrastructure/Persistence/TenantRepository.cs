using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Identity.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ITenantRepository"/> (Wave 6,
/// slice 6c.1).
///
/// <para>
/// Each method maps 1:1 to a single EF Core query. The repository is
/// deliberately narrow — cross-tenant filtering lives in slice 6c.2's
/// decorator, not here, so this class stays trivial to unit-test.
/// </para>
/// </summary>
public sealed class TenantRepository : ITenantRepository
{
    private readonly IdentityDbContext _db;

    public TenantRepository(IdentityDbContext db) { _db = db; }

    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<Tenant?> FindBySlugAsync(string slug, CancellationToken ct)
        => _db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);

    public async Task AddAsync(Tenant tenant, CancellationToken ct)
        => await _db.Tenants.AddAsync(tenant, ct);

    public async Task UpdateAsync(Tenant tenant, CancellationToken ct)
    {
        var entry = _db.Entry(tenant);
        if (entry.State == EntityState.Detached)
        {
            _db.Tenants.Update(tenant);
        }
        await Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Tenant>> ListByOwnerAsync(Guid ownerUserId, CancellationToken ct)
        => await _db.Tenants
            .Where(t => t.OwnerUserId == ownerUserId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
}
