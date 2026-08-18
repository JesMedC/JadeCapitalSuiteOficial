using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Identity.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ITenantRepository"/> (Wave 6,
/// slice 6c.1; extended in slice 6d.2 with <see cref="DeleteAsync"/>).
///
/// <para>
/// Each method maps 1:1 to a single EF Core query. The repository is
/// deliberately narrow — cross-tenant filtering lives in slice 6c.2's
/// decorator, not here, so this class stays trivial to unit-test.
/// </para>
/// <para>
/// Slice 6d.2 wraps this class with <c>TenantAuditDecorator</c> via Scrutor
/// to log an <c>audit.events</c> row on every successful Add/Update/Delete.
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

    /// <summary>
    /// Slice 6d.2: hard-delete from the change tracker. The tenant
    /// aggregate doesn't implement <see cref="JadeCapital.Shared.Kernel.SoftDelete.ISoftDelete"/>
    /// in this slice — future work may add it. Soft-delete would mark
    /// <c>IsDeleted=true</c> + <c>DeletedAtUtc=...</c> instead of calling
    /// <c>Remove</c>; the decorator doesn't care which path is taken.
    /// </summary>
    public Task DeleteAsync(Tenant tenant, CancellationToken ct)
    {
        _db.Tenants.Remove(tenant);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Tenant>> ListByOwnerAsync(Guid ownerUserId, CancellationToken ct)
        => await _db.Tenants
            .Where(t => t.OwnerUserId == ownerUserId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
}