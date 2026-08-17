namespace JadeCapital.Trading.Contracts.Coaching;

// ============================================================================
//  Coaching wire DTOs — slice 2d.1 (Trader Journal Core).
//
//  Mirror of the Shared.Kernel.Coaching types — the wire shape matches
//  the OpenAPI spec in
//  `openspec/changes/2026-08-17-trader-journal-core/specs/coaching-prompts/spec.md`.
//
//  Enums serialize as string names via System.Text.Json's default
//  enum-string policy on the API host.
// ============================================================================

/// <summary>
/// Time window for the coaching query. Maps to <c>?period=</c> in the URL.
/// Mirrors <c>BehavioralPeriod</c> in
/// <c>src/2.Modules/Trading/JadeCapital.Trading.Contracts/Behavioral/BehavioralDtos.cs</c>;
/// we keep a separate copy instead of coupling Contracts.Coaching →
/// Contracts.Behavioral so Wave 3+ can move coaching to a non-trading
/// module without dragging the behavioral analytics shape with it.
/// </summary>
public enum CoachingPeriod
{
    Days7 = 7,
    Days30 = 30,
    Days90 = 90,
    All = 0,
}

/// <summary>
/// Top-level response body for <c>GET /api/coaching/prompts</c>.
/// <c>prompts</c> is sorted by severity DESC then occurredAt DESC
/// (newest high-severity prompt first).
/// </summary>
public sealed record CoachingPromptsDto(
    string Period,
    IReadOnlyList<CoachingPromptDto> Prompts);

/// <summary>
/// One prompt emitted by a <c>ICoachingRule</c> implementation.
/// Mirrors <see cref="JadeCapital.Shared.Kernel.Coaching.CoachingPrompt"/>.
/// </summary>
public sealed record CoachingPromptDto(
    string RuleId,
    string Severity,
    string Title,
    string Body,
    CoachingCtaDto Cta,
    DateTimeOffset OccurredAt);

/// <summary>
/// Call-to-action object nested inside a prompt.
/// </summary>
public sealed record CoachingCtaDto(
    string Route,
    string Label);
