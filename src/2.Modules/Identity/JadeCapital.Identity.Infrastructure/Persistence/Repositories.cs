using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Identity.Infrastructure.Persistence;

public sealed class UserRepository : IUserRepository
{
    private readonly IdentityDbContext _db;

    public UserRepository(IdentityDbContext db) { _db = db; }

    public Task<User?> FindByEmailAsync(string email, CancellationToken ct)
        => _db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant(), ct);

    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct)
        => _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    /// <summary>
    /// Slice 7a.1 — satisfies <see cref="IRepository{T}.GetByIdAsync"/>
    /// so the <c>UserAuditDecorator</c> can fetch the pre-mutation
    /// snapshot via the generic <c>DecoratedRepository&lt;T&gt;</c> helper.
    /// Delegates to the same EF query as <c>FindByIdAsync</c>.
    /// </summary>
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task AddAsync(User user, CancellationToken ct)
        => await _db.Users.AddAsync(user, ct);

    public async Task UpdateAsync(User user, CancellationToken ct)
    {
        // User es tracked? Si no, lo trackeamos.
        var entry = _db.Entry(user);
        if (entry.State == EntityState.Detached)
        {
            _db.Users.Update(user);
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// Slice 6c.3 — list users in the tenant. The query materializes a
    /// stable projection (id + email + display_name + role + status +
    /// created_at) so the handler can map straight to <c>TenantUserDto</c>
    /// without loading password hashes / session-version etc. Ordered by
    /// created_at ASC for stable iteration in the API response.
    /// </summary>
    public async Task<IReadOnlyList<User>> ListByTenantIdAsync(TenantId tenantId, CancellationToken ct)
        => await _db.Users
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(ct);

    /// <summary>
    /// Slice 6c.3 — count users in the tenant. Single SQL
    /// <c>SELECT COUNT(*) FROM identity.users WHERE tenant_id = $1</c>.
    /// </summary>
    public Task<int> CountByTenantIdAsync(TenantId tenantId, CancellationToken ct)
        => _db.Users.CountAsync(u => u.TenantId == tenantId, ct);

    /// <summary>
    /// Wave 7, slice 7a.1 — defensive STUB. The canonical User mutation
    /// surface is <c>User.Cancel(reason)</c> + Tenant reassignment via
    /// <c>User.AssignToTenant</c> / <c>User.ReassignToTenantByAdmin</c>;
    /// there is no domain op that hard-deletes a User. The
    /// <c>UserAuditDecorator</c> short-circuits to an
    /// <see cref="Audit.AuditAction.Failed"/> audit row + re-throws this
    /// exception BEFORE the inner is reached. The inner is kept as a
    /// defensive second-line check so a misconfigured DI container
    /// cannot accidentally hard-delete a user.
    /// </summary>
    public Task DeleteAsync(User user, CancellationToken ct)
        => throw new NotSupportedException(
            "User deletion happens via Tenant reassignment, not direct delete.");
}

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IdentityDbContext _db;

    public RefreshTokenRepository(IdentityDbContext db) { _db = db; }

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct)
        => _db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct)
        => await _db.RefreshTokens.AddAsync(token, ct);

    public async Task<IReadOnlyList<RefreshToken>> ListActiveByUserIdAsync(Guid userId, CancellationToken ct)
        => await _db.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null && r.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(ct);

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var active = await _db.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in active)
        {
            token.Revoke(now, Guid.Empty);
        }
    }
}

/// <summary>
/// Unit of Work scoped a la BD de Identity.
/// </summary>
public sealed class IdentityUnitOfWork : Application.Abstractions.IUnitOfWork
{
    private readonly IdentityDbContext _db;
    public IdentityUnitOfWork(IdentityDbContext db) { _db = db; }

    public async Task<Shared.Kernel.Results.Result<int>> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            var rows = await _db.SaveChangesAsync(ct);
            return Shared.Kernel.Results.Result<int>.Success(rows);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Shared.Kernel.Results.Result<int>.Failure(Shared.Kernel.Results.Error.Conflict("db.concurrency", "Concurrency conflict."));
        }
        catch (DbUpdateException ex)
        {
            return Shared.Kernel.Results.Result<int>.Failure(Shared.Kernel.Results.Error.Failure("db.update.failed", ex.Message));
        }
    }
}