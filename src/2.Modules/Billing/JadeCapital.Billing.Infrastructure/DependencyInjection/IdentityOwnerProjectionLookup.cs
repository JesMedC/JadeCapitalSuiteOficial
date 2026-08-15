using JadeCapital.Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Billing.Infrastructure.DependencyInjection;

/// <summary>
/// Default <see cref="Application.Features.Subscriptions.IOwnerProjectionLookup"/>:
/// reads the owner's email + display name directly from <c>identity.users</c>
/// via the shared <see cref="BillingDbContext"/> connection. Cross-schema
/// reads are allowed in this codebase's Postgres layout (one DB, one
/// service, multiple schemas) so no extra DbContext registration is needed.
///
/// If the row doesn't exist (orphan subscription), returns <c>null</c> so the
/// handler can fall back to an empty projection. We do NOT throw on missing
/// — that would leak "user exists?" through an error code.
/// </summary>
public sealed class IdentityOwnerProjectionLookup
    : Application.Features.Subscriptions.IOwnerProjectionLookup
{
    private readonly BillingDbContext _db;

    public IdentityOwnerProjectionLookup(BillingDbContext db) { _db = db; }

    public async Task<JadeCapital.Identity.Contracts.Projections.IUserOwnerProjection?> FindByUserIdAsync(
        Guid userId, CancellationToken ct = default)
    {
        // SqlQueryRaw returns a queryable; materialise to a small DTO via
        // raw SQL to avoid pulling the full User aggregate through this
        // lookup (which would couple Billing → Identity.Domain).
        var rows = await _db.Database
            .SqlQueryRaw<OwnerRow>(
                "SELECT email, display_name AS DisplayName FROM identity.users WHERE id = {0}",
                userId)
            .ToListAsync(ct);

        var r = rows.FirstOrDefault();
        return r is null ? null : new OwnerProjection(r.Email, r.DisplayName);
    }

    /// <summary>Local DTO matching the raw SELECT column aliases.</summary>
    private sealed record OwnerRow(string Email, string DisplayName);

    /// <summary>IUserOwnerProjection implementation local to the Billing module.</summary>
    private sealed record OwnerProjection(string Email, string DisplayName)
        : JadeCapital.Identity.Contracts.Projections.IUserOwnerProjection;
}
