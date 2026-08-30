using JadeCapital.Shared.Kernel.Coaching;

// ============================================================================
//  Alerts wire shape — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Lives in JadeCapital.Shared.Kernel because the Alert SHAPE (RuleId,
//  Severity, Title, Body, Cta, ExpiresAt) is a stable cross-module contract:
//  the API serializes these records to the wire, the FE renders them, and
//  the background service emits them. Putting the wire shape in the kernel
//  means future non-Trading modules (e.g. an admin alerting tool) can reuse
//  the same DTO without depending on Trading.
//
//  What does NOT live here:
//   - IAlertRule + AlertContext live in JadeCapital.Trading.Application
//     (mirrors the ICoachingRule + CoachingContext pattern from slice 2d).
//     They reference Trading-only aggregates (Trade, JournalEntry,
//     BehavioralAnalyticsResult) which can't be moved into the kernel
//     without inverting the layer dependency.
//   - The 5 concrete rules live in Trading.Application/Rules.
//
//  Severity IS Coaching.Severity (slice 2d) — same ordinal scale
//  (Low=1 / Medium=2 / High=3). We do NOT grow a second three-level
//  severity enum; both the coaching registry and the alert registry share
//  the same scale so the FE / API can render either with the same color
//  map without translation.
// ============================================================================

namespace JadeCapital.Shared.Kernel.Alerts;

/// <summary>
/// One alert emitted by an <c>IAlertRule</c>. The shape mirrors the wire DTO
/// <c>AlertDto</c> in <c>JadeCapital.Trading.Contracts.Alerts</c>; the
/// kernel type stays neutral so future non-Trading modules could plug their
/// own rules in the registry.
/// </summary>
public sealed record Alert(
    string RuleId,
    Severity Severity,
    string Title,
    string Body,
    Cta Cta,
    DateTimeOffset? ExpiresAt);