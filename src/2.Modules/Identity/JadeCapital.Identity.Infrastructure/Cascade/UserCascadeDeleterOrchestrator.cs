using JadeCapital.Identity.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Identity.Infrastructure.Cascade;

/// <summary>
/// GDPR Art. 17 cascade orchestrator (Wave 10, slice 10.5).
///
/// <para>
/// Resolves every registered <see cref="IUserCascadeDeletor"/> from DI
/// (one per module that owns user-owned aggregates: Identity, Trading,
/// Billing) and invokes them in sequence. A single deletor failure is
/// logged and the orchestrator continues — the GDPR sweep is
/// best-effort across modules, but a failure on ONE module MUST NOT
/// abort the rest of the cascade.
/// </para>
///
/// <para>
/// On <see cref="CascadeSoftDeleteAsync"/>: invokes every deletor's
/// soft-delete path. The user row's <c>is_deleted</c> + status flag
/// flips are the caller's responsibility (the application handler does
/// that before calling the orchestrator).
/// </para>
///
/// <para>
/// On <see cref="CascadeHardDeleteAsync"/>: invokes every deletor's
/// hard-delete path, then invokes <see cref="IGdprAuditAnonymizer"/>
/// to anonymize the audit trail, then physically deletes the user row
/// from <c>identity.users</c>. The order matters: we MUST delete the
/// FK-referencing rows BEFORE the <c>users</c> row, otherwise the FK
/// constraint would reject the user-row DELETE.
/// </para>
///
/// <para>
/// <b>Why an orchestrator class and not a method on the handler</b>:
/// keeps the handler thin (MediatR-deliver, log, return Result) and
/// lets the sweep BackgroundService reuse the same code path. Single
/// source of truth for the GDPR cascade.
/// </para>
/// </summary>
public sealed class UserCascadeDeleterOrchestrator : IGdprCascadeOrchestrator
{
    private readonly IReadOnlyList<IUserCascadeDeletor> _deletors;
    private readonly IGdprAuditAnonymizer _auditAnonymizer;
    private readonly ILogger<UserCascadeDeleterOrchestrator> _logger;

    public UserCascadeDeleterOrchestrator(
        IEnumerable<IUserCascadeDeletor> deletors,
        IGdprAuditAnonymizer auditAnonymizer,
        ILogger<UserCascadeDeleterOrchestrator> logger)
    {
        ArgumentNullException.ThrowIfNull(deletors);
        ArgumentNullException.ThrowIfNull(auditAnonymizer);
        ArgumentNullException.ThrowIfNull(logger);

        _deletors = deletors.ToList();
        _auditAnonymizer = auditAnonymizer;
        _logger = logger;
    }

    /// <summary>
    /// Soft-deletes every row the user owns across every module.
    /// Returns the sum of rows touched across all deletors. Per-deletor
    /// exceptions are logged + swallowed.
    /// </summary>
    public async Task<int> CascadeSoftDeleteAsync(Guid userId, CancellationToken ct)
    {
        var totalTouched = 0;

        foreach (var deletor in _deletors)
        {
            try
            {
                var touched = await deletor.CascadeSoftDeleteAsync(userId, ct);
                totalTouched += touched;
                _logger.LogInformation(
                    "GdprSoftDelete: deletor {Type} touched {Touched} row(s) for user {UserId}.",
                    deletor.GetType().Name, touched, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "GdprSoftDelete: deletor {Type} failed for user {UserId}; continuing.",
                    deletor.GetType().Name, userId);
            }
        }

        return totalTouched;
    }

    /// <summary>
    /// Hard-deletes every row the user owns across every module,
    /// anonymizes the audit trail, and physically removes the
    /// <c>identity.users</c> row. Returns the sum of rows touched
    /// across all deletors + the audit row count. Per-deletor
    /// exceptions are logged + swallowed; the audit anonymizer is
    /// invoked EVEN IF every deletor fails (the compliance trail MUST
    /// be cleared).
    /// </summary>
    /// <param name="physicalUserRowDeleteAsync">
    /// Delegate that physically deletes the user row from
    /// <c>identity.users</c>. Passed in by the caller (the Identity
    /// module owns the DbContext that contains the row) so the
    /// orchestrator does NOT need a direct DbContext dependency.
    /// </param>
    public async Task<int> CascadeHardDeleteAsync(
        Guid userId,
        Func<CancellationToken, Task<int>> physicalUserRowDeleteAsync,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(physicalUserRowDeleteAsync);

        var totalTouched = 0;

        foreach (var deletor in _deletors)
        {
            try
            {
                var touched = await deletor.CascadeHardDeleteAsync(userId, ct);
                totalTouched += touched;
                _logger.LogInformation(
                    "GdprHardDelete: deletor {Type} deleted {Touched} row(s) for user {UserId}.",
                    deletor.GetType().Name, touched, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "GdprHardDelete: deletor {Type} failed for user {UserId}; continuing.",
                    deletor.GetType().Name, userId);
            }
        }

        // Anonymize the audit trail AFTER all deletors return — we want
        // the per-user audit events to remain attributable to the user
        // during the cascade so the compliance trail captures each
        // deletion as it happens. The anonymizer runs once per user.
        try
        {
            var auditRowsAnonymized = await _auditAnonymizer.AnonymizeUserAsync(userId, ct);
            totalTouched += auditRowsAnonymized;
            _logger.LogInformation(
                "AuditAnonymizationRun: anonymized {Touched} row(s) for user {UserId}.",
                auditRowsAnonymized, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AuditAnonymizationRun: failed for user {UserId}; user row may be hard-deleted but trail retains identifiers.",
                userId);
        }

        // Physical user-row DELETE last — after every FK-referencing row
        // is gone, the FK constraint will accept the user DELETE.
        try
        {
            var userRowsDeleted = await physicalUserRowDeleteAsync(ct);
            totalTouched += userRowsDeleted;
            _logger.LogInformation(
                "GdprHardDelete: identity.users row deleted for user {UserId} ({Rows} row).",
                userId, userRowsDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GdprHardDelete: identity.users row DELETE failed for user {UserId}; manual ops intervention required.",
                userId);
        }

        return totalTouched;
    }
}