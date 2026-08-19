using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Application.Abstractions;

/// <summary>
/// GDPR Art. 17 cascade contract (Wave 10, slice 10.5).
///
/// <para>
/// One <see cref="IUserCascadeDeletor"/> is registered per module that
/// owns user-owned aggregates. The <c>UserCascadeDeleterOrchestrator</c>
/// aggregates them via DI as <c>IEnumerable&lt;IUserCascadeDeletor&gt;</c>
/// and invokes each in turn. The Identity module ships its own deletor
/// (RefreshTokens + RiskProfiles); Trading and Billing ship theirs.
/// </para>
///
/// <para>
/// <b>Two-phase contract</b>: <see cref="CascadeSoftDeleteAsync"/> runs
/// immediately on <c>DELETE /api/users/me</c> and flips every row's
/// <c>is_deleted</c> to true. <see cref="CascadeHardDeleteAsync"/> runs
/// from <c>HardDeleteSweepBackgroundService</c> 30 days later and
/// physically DELETEs the rows (no soft-delete flag remains).
/// </para>
///
/// <para>
/// <b>Why per-module, not a single Identity-owned orchestrator</b>: the
/// orchestrator lives in <c>Identity.Application</c> to keep the public
/// API contract DRY (one DELETE endpoint, one orchestration entrypoint).
/// Each module's <see cref="IUserCascadeDeletor"/> implementation lives
/// in its own Infrastructure assembly because that is where the
/// DbContext + repositories are. The trade-off: Trading.Infrastructure
/// and Billing.Infrastructure reference <c>Identity.Application</c> for
/// the interface; they do NOT reference each other. Cross-module
/// invariants are enforced by the orchestrator's per-deletor error
/// handling (a single failing deletor logs + continues — the user is
/// purged across every other module even if one deletor throws).
/// </para>
/// </summary>
public interface IUserCascadeDeletor
{
    /// <summary>
    /// Soft-deletes every row owned by <paramref name="userId"/> in the
    /// implementing module's schema. Idempotent — calling twice on the
    /// same userId is a no-op for already-soft-deleted rows.
    /// </summary>
    /// <returns>
    /// Number of rows touched. Zero is a valid result (the user may have
    /// owned nothing in this module). Failures are logged and counted;
    /// the method MUST NOT throw on transient DB errors so the
    /// orchestrator can continue with the next deletor.
    /// </returns>
    Task<int> CascadeSoftDeleteAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Physically purges every row owned by <paramref name="userId"/> in
    /// the implementing module's schema. Called from
    /// <c>HardDeleteSweepBackgroundService</c> after the 30-day grace
    /// window elapses. Idempotent.
    /// </summary>
    /// <returns>
    /// Number of rows physically deleted. Zero is valid. MUST NOT throw.
    /// </returns>
    Task<int> CascadeHardDeleteAsync(Guid userId, CancellationToken ct);
}