namespace JadeCapital.Trading.Application.Ai;

// ============================================================================
//  IUserTradingContextProvider — slice 5b.2 (Wave 5, AI Coaching).
//
//  Abstraction that builds the user trading context fed to
//  <c>CoachingPromptTemplate</c>. Lives in Application (not Domain) because
//  the concrete impl needs EF + repository handles. The default impl
//  (<c>EfUserTradingContextProvider</c>) queries
//  <c>trading.trades</c> + uses Wave 3b rule violations. Tests substitute
//  the FakeUserTradingContextProvider so the handler unit-test is fully
//  deterministic (no DB, no Ollama).
//
//  Used by:
//   - <c>GenerateCoachingPromptHandler</c> (single-user composition).
//   - <c>CoachingPromptService</c> BG service (lists active users via
//     <see cref="GetActiveUserIdsWithMinTradesAsync"/>).
// ============================================================================

public interface IUserTradingContextProvider
{
    /// <summary>
    /// Loads the user's trading context for the last <paramref name="windowDays"/>
    /// days. Returns an empty context (not null) when the user has no trades —
    /// callers must short-circuit the AI call themselves via
    /// <see cref="UserTradingContext.HasNoTrades"/>.
    /// </summary>
    Task<UserTradingContext> GetUserContextAsync(
        Guid userId,
        int windowDays,
        CancellationToken ct);

    /// <summary>
    /// Returns the ids of every active user who closed at least
    /// <paramref name="minClosedTrades"/> trades in the last
    /// <paramref name="windowDays"/> days. Used by the BG service to pick
    /// which users get a daily AI prompt. Capped to 100 users per tick
    /// (excess users wait for the next day's tick).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetActiveUserIdsWithMinTradesAsync(
        int minClosedTrades,
        int windowDays,
        CancellationToken ct);
}