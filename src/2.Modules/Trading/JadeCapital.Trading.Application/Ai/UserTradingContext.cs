namespace JadeCapital.Trading.Application.Ai;

// ============================================================================
//  UserTradingContext — slice 5b.2 (Wave 5, AI Coaching).
//
//  Pure-data DTO that captures a user's trading context for the last
//  <c>WindowDays</c> days. Composed by <c>UserTradingContextProvider</c>
//  (EF query) and fed to <c>CoachingPromptTemplate.Render</c>.
//
//  <para>
//  PII-safe: no email / displayName / absolute P&L amounts. The provider
//  implementation MUST filter those fields out before serializing to the
//  prompt (see CoachingPromptTemplate). The aggregate stores the JSONB
//  verbatim — the FE renders it for transparency/debugging.
//  </para>
// ============================================================================

public sealed record UserTradingContext(
    Guid UserId,
    int WindowDays,
    int ClosedTradeCount,
    int Winners,
    int Losers,
    decimal WinRate,
    decimal AverageRiskReward,
    IReadOnlyList<string> InstrumentsTraded,
    IReadOnlyList<string> Violations)
{
    /// <summary>True when the user has zero trades in the window.</summary>
    public bool HasNoTrades => ClosedTradeCount == 0;
}