namespace JadeCapital.Shared.Kernel.Storage;

/// <summary>
/// Per-user attachment quota contract. Lives in <c>Shared.Kernel.Storage</c>
/// because both the Application enforcer (pre-upload gate) and the
/// infrastructure HTTP endpoint (read-side report) consume it. The
/// defaults mirror the design.md spec:
/// <list type="bullet">
///   <item><c>MaxTotalBytes = 52_428_800 (50 MiB)</c> — soft cap per user
///   across all uploaded attachments. Wave 4d enforcéa with HTTP 413.</item>
///   <item><c>MaxAttachmentCount = 100</c> — hard cap on number of
///   attachments (any state) per user.</item>
///   <item><c>ExpirationDays = 90</c> — soft-delete sweep horizon. The
///   MinIO bucket lifecycle rule (separate concern) mirrors this.</item>
/// </list>
///
/// The record is intentionally simple: the per-user row in
/// <c>identity.users</c> stores the values; this record is the in-memory
/// snapshot used by the enforcer + report endpoint.
/// </summary>
public sealed record AttachmentQuota(
    long MaxTotalBytes = 52_428_800L,
    int MaxAttachmentCount = 100,
    int ExpirationDays = 90)
{
    /// <summary>Default quota — used when the per-user row has not been
    /// populated (admin override) or the user has no per-user override.</summary>
    public static readonly AttachmentQuota Default = new();
}