using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Identity.Infrastructure.Cascade;

/// <summary>
/// Identity-side GDPR Art. 17 cascade deletor (Wave 10, slice 10.5).
///
/// <para>
/// Soft-deletes every row in <c>identity.*</c> that references the
/// target user:
/// </para>
/// <list type="bullet">
///   <item><b>RefreshTokens</b> — revokes (sets RevokedAt) every
///         active token. The "soft-delete" mapping is REVOCATION because
///         RefreshToken has no IsDeleted flag — revoking is the
///         equivalent terminal transition for that aggregate. The 30-day
///         grace period still applies via the physical-delete sweep.</item>
///   <item><b>RiskProfiles</b> — supersedes (sets IsActive=false) every
///         active profile. Same logic as refresh tokens: no IsDeleted
///         flag on RiskProfile, so the canonical termination surface
///         (<see cref="JadeCapital.Identity.Domain.RiskProfile.RiskProfile.MarkSuperseded"/>)
///         is the soft-delete mapping.</item>
///   <item><b>PasswordHistory</b> — physical purge on hard-delete; left
///         alone on soft-delete (password history is non-PII by design:
///         the hashes are PBKDF2 with per-entry salts, no plaintext).</item>
/// </list>
///
/// <para>
/// Hard-delete physically removes <c>refresh_tokens</c>,
/// <c>password_history</c> + (caller responsibility) the <c>users</c>
/// row. The orchestrator runs <c>CascadeHardDeleteAsync</c> on every
/// deletor and then writes the pseudonymized audit row separately via
/// <see cref="IGdprAuditAnonymizer"/>.
/// </para>
/// </summary>
public sealed class IdentityUserCascadeDeletor : IUserCascadeDeletor
{
    private readonly IdentityDbContext _db;
    private readonly IClock _clock;

    public IdentityUserCascadeDeletor(IdentityDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<int> CascadeSoftDeleteAsync(Guid userId, CancellationToken ct)
    {
        var touched = 0;

        var activeTokens = await _db.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null)
            .ToListAsync(ct);

        var now = _clock.UtcNow;
        foreach (var token in activeTokens)
        {
            var result = token.Revoke(now, Guid.Empty);
            if (result.IsSuccess) touched++;
        }

        var activeProfiles = await _db.RiskProfiles
            .Where(p => p.UserId == userId && p.IsActive)
            .ToListAsync(ct);

        foreach (var profile in activeProfiles)
        {
            var result = profile.MarkSuperseded(_clock);
            if (result.IsSuccess) touched++;
        }

        await _db.SaveChangesAsync(ct);
        return touched;
    }

    public async Task<int> CascadeHardDeleteAsync(Guid userId, CancellationToken ct)
    {
        var touched = 0;

        var deletedTokens = await _db.RefreshTokens
            .Where(r => r.UserId == userId)
            .ExecuteDeleteAsync(ct);
        touched += deletedTokens;

        var deletedHistory = await _db.PasswordHistory
            .Where(p => p.UserId == userId)
            .ExecuteDeleteAsync(ct);
        touched += deletedHistory;

        var deletedTempCreds = await _db.TemporaryCredentials
            .Where(t => t.UserId == userId)
            .ExecuteDeleteAsync(ct);
        touched += deletedTempCreds;

        var deletedProfiles = await _db.RiskProfiles
            .Where(p => p.UserId == userId)
            .ExecuteDeleteAsync(ct);
        touched += deletedProfiles;

        return touched;
    }
}