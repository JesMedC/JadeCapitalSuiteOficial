using JadeCapital.Identity.Contracts.Projections;

namespace JadeCapital.Identity.Contracts.Projections;

/// <summary>
/// Read-only projection of a user's attachment quota state. Slice 4d (Wave 4).
///
/// Lives in <c>Identity.Contracts</c> because the Trading module's
/// <c>AttachmentQuotaEnforcer</c> + <c>AttachmentUsageHandler</c> need to
/// ask "what's this user's storage budget?" without depending on
/// <c>JadeCapital.Identity.Domain</c> (Clean Architecture — Trading only
/// references Contracts). The implementation lives in
/// <c>JadeCapital.Identity.Infrastructure.Persistence</c> and reads the
/// <c>identity.users.attachment_quota_bytes</c> + <c>attachment_used_bytes</c>
/// columns added in migration 0018.
///
/// Design notes:
/// <list type="bullet">
///   <item>The projection is a snapshot — it does NOT include lock state.
///   Two consecutive calls can return different <see cref="UsedBytes"/>
///   values if another request raced ahead.</item>
///   <item><see cref="UsedBytes"/> is the cached aggregate maintained by
///   the ConfirmAttachmentUploadedHandler. If it drifts from the
///   ground-truth sum of trade_attachments (DB corruption / unsynced
///   writes), the enforcer accepts the cached value and the drift is
///   recovered on the next sweep.</item>
/// </list>
/// </summary>
public interface IAttachmentQuotaReader
{
    /// <summary>
    /// Returns the user's quota + cached usage in bytes. Returns <c>null</c>
    /// if the user does not exist (cross-module scope preserved at the
    /// projection boundary — Trading does not learn whether a userId is
    /// valid vs invalid).
    /// </summary>
    Task<UserAttachmentQuota?> GetQuotaAsync(Guid userId, CancellationToken ct);
}

/// <summary>
/// Wire shape returned by <see cref="IAttachmentQuotaReader"/>. Immutable
/// snapshot of the user's quota row.
/// </summary>
public sealed record UserAttachmentQuota(
    Guid UserId,
    long QuotaBytes,
    long UsedBytes)
{
    /// <summary>Bytes remaining before the user hits the cap.
    /// Never negative — if <c>UsedBytes</c> exceeds <c>QuotaBytes</c> (drift),
    /// returns 0.</summary>
    public long RemainingBytes => Math.Max(0L, QuotaBytes - UsedBytes);
}