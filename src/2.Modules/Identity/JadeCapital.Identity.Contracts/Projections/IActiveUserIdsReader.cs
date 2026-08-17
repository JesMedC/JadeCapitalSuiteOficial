namespace JadeCapital.Identity.Contracts.Projections;

// ============================================================================
//  IActiveUserIdsReader — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Read-only projection that returns the Ids of every Active user. Lives
//  in Identity.Contracts because it's a stable cross-module contract: the
//  Trading module's alert BackgroundService iterates these Ids to evaluate
//  alerts per user. The implementation lives in
//  JadeCapital.Identity.Infrastructure.Persistence.IdentityActiveUserIdsReader
//  to keep the Trading → Identity.Domain dependency pointing nowhere
//  (Clean Architecture).
//
//  Use cases:
//   - AlertEvaluationBackgroundService (Wave 3): iterates every 5 min.
//   - Wave 4+ periodic maintenance jobs (e.g. stats recompute).
// ============================================================================

public interface IActiveUserIdsReader
{
    /// <summary>
    /// Returns the Ids of every user with <c>UserStatus.Active</c>. Used
    /// by background jobs that need to walk every active trader.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListActiveUserIdsAsync(CancellationToken ct);
}