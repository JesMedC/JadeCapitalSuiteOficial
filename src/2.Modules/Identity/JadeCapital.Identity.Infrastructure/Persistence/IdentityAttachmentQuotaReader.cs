using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Contracts.Projections;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Identity.Infrastructure.Persistence;

/// <summary>
/// Identity-side implementation of <see cref="IAttachmentQuotaReader"/>.
/// Reads <c>identity.users.attachment_quota_bytes</c> +
/// <c>attachment_used_bytes</c> added by migration 0018.
///
/// Returns <c>null</c> when the user does not exist — Trading's enforcer
/// then falls back to <c>AttachmentQuota.Default</c>. This is intentional:
/// the projection never leaks "does this user exist?" to Trading.
/// </summary>
public sealed class IdentityAttachmentQuotaReader : IAttachmentQuotaReader
{
    private readonly IdentityDbContext _db;

    public IdentityAttachmentQuotaReader(IdentityDbContext db) { _db = db; }

    public async Task<UserAttachmentQuota?> GetQuotaAsync(Guid userId, CancellationToken ct)
    {
        // Project only the two columns we need — avoids loading the
        // full User row (with PasswordHash, SessionVersion, etc.)
        // into the Trading module's scope.
        var row = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.AttachmentQuotaBytes, u.AttachmentUsedBytes })
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;
        return new UserAttachmentQuota(row.Id, row.AttachmentQuotaBytes, row.AttachmentUsedBytes);
    }
}