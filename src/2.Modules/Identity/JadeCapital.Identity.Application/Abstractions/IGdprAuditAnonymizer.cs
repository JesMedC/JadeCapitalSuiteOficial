namespace JadeCapital.Identity.Application.Abstractions;

/// <summary>
/// GDPR Art. 17 audit log anonymization contract (Wave 10, slice 10.5,
/// design.md §3.3).
///
/// <para>
/// When a user is hard-deleted by <see cref="JadeCapital.Identity.Infrastructure.BackgroundServices.HardDeleteSweepBackgroundService"/>,
/// the audit trail MUST be retained for compliance but the personal
/// identifiers MUST be removed. The anonymizer:
/// </para>
/// <list type="bullet">
///   <item>Sets <c>audit.events.user_id = NULL</c> on every row that
///         referenced the deleted user.</item>
///   <item>Replaces <c>entity_id</c> with
///         <c>deleted_user_&lt;sha256(original_guid)&gt;</c> when
///         <c>entity_type = "User"</c>; other entity rows are left
///         alone (they reference non-user aggregates whose FK is being
///         hard-deleted).</item>
///   <item>Replaces <c>changes_json</c> with
///         <c>{ "reason": "gdpr_hard_delete", "original_user_id_hash": "&lt;sha256&gt;" }</c>
///         so the compliance trail records WHEN + WHY the row was
///         anonymized without leaking the original user id.</item>
/// </list>
///
/// <para>
/// The anonymizer runs as a sibling step of
/// <c>IUserCascadeDeletor.CascadeHardDeleteAsync</c>; the
/// <c>UserCascadeDeleterOrchestrator</c> invokes each deletor THEN
/// invokes this anonymizer once per user. The orchestrator logs and
/// continues on failure — the cascade is best-effort across modules,
/// but a single anonymization failure MUST NOT leave the user half-purged.
/// </para>
/// </summary>
public interface IGdprAuditAnonymizer
{
    /// <summary>
    /// Anonymizes every audit.events row for <paramref name="userId"/>.
    /// Returns the number of rows touched. Zero is a valid result
    /// (the user may have had no audit trail). MUST NOT throw on
    /// transient DB errors.
    /// </summary>
    Task<int> AnonymizeUserAsync(Guid userId, CancellationToken ct);
}