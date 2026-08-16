using JadeCapital.Shared.Kernel.Alerts;
using AlertAggregate = JadeCapital.Trading.Domain.Alerts.Alert;
using AlertWire = JadeCapital.Shared.Kernel.Alerts.Alert;
using AlertDtoWire = JadeCapital.Trading.Contracts.Alerts.AlertDto;

namespace JadeCapital.Trading.Application.Abstractions;

// ============================================================================
//  IAlertRepository — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Cross-user scope: every List/Get is filtered by userId explicitly.
//  The BackgroundService iterates users via a separate Identity reader
//  (IActiveUserIdsReader in Identity.Contracts) and then calls
//  EvaluateForUserAsync here per user.
//
//  AddAsync is the dedup boundary: the EF implementation translates to
//  INSERT ... ON CONFLICT DO NOTHING on the partial UNIQUE INDEX
//  (user_id, rule_id, UTC-date). The caller does not check for dupes
//  before inserting; the DB does. AddAsync is therefore idempotent
//  at the row level.
//
//  All read methods return the Alert aggregate (NOT a wire shape). The
//  API edge converts via AlertMapping.ToDto. This keeps mutating ops
//  (Acknowledge) consistent: same type in/out, no projection gymnastics.
// ============================================================================

public interface IAlertRepository
{
    /// <summary>
    /// Lists alerts for the user. When <paramref name="activeOnly"/> is
    /// true, filters out acked and expired alerts. Otherwise returns the
    /// full history (audit trail). Sorted by Severity DESC + CreatedAt DESC.
    /// </summary>
    Task<IReadOnlyList<AlertAggregate>> ListByUserAsync(
        Guid userId, bool activeOnly, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Looks up a single alert by id, scoped to the user. Returns null
    /// when the alert does not exist OR belongs to another user
    /// (cross-user isolation: no leak existence).
    /// </summary>
    Task<AlertAggregate?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct);

    /// <summary>
    /// Inserts a new alert. Returns true when the row was actually
    /// persisted; false when the dedup UNIQUE INDEX rejected it
    /// (a row for (user, rule, UTC-date) already exists).
    /// </summary>
    Task<bool> AddAsync(AlertAggregate alert, CancellationToken ct);

    /// <summary>Persists changes (acknowledgement) to an existing alert.</summary>
    Task UpdateAsync(AlertAggregate alert, CancellationToken ct);
}