namespace JadeCapital.Identity.Application.Abstractions;

/// <summary>
/// GDPR Art. 17 cascade orchestrator contract (Wave 11, slice 11.2b).
///
/// <para>
/// Public-facing API of the GDPR cascade that lives in
/// <c>JadeCapital.Identity.Infrastructure.Cascade.UserCascadeDeleterOrchestrator</c>
/// (Wave 10 slice 10.5). The Application layer depends on this interface so
/// the <c>DeleteAccountHandler</c> can dispatch the cascade without taking
/// a direct dependency on Infrastructure.
/// </para>
///
/// <para>
/// The concrete orchestrator resolves every registered
/// <see cref="IUserCascadeDeletor"/> from DI (one per module that owns
/// user-owned aggregates: Identity, Trading, Billing) and invokes them in
/// registration order. Per-deletor failures are logged and the cascade
/// continues — a single failing deletor MUST NOT abort the rest.
/// </para>
/// </summary>
public interface IGdprCascadeOrchestrator
{
    /// <summary>
    /// Soft-deletes every row the user owns across every module. Used by
    /// the <c>DELETE /api/users/me</c> handler immediately after the user
    /// row has been flipped into <c>SoftDeleted</c>.
    /// </summary>
    /// <returns>
    /// Sum of rows touched across all deletors. Zero is valid (the user
    /// may have owned nothing in any module).
    /// </returns>
    Task<int> CascadeSoftDeleteAsync(Guid userId, CancellationToken ct);
}
