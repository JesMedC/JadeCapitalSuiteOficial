// ============================================================================
//  Coaching — slice 2d (Trader Journal Core).
//
//  Shared wire-shape types for the coaching-prompt registry. Lives in
//  JadeCapital.Shared.Kernel because the prompt shape (Title, Body, CTA,
//  Severity) is a stable contract that future modules could reuse.
//
//  What does NOT live here:
//   - ICoachingRule and CoachingContext live in
//     JadeCapital.Trading.Application/Coaching — they reference
//     Trading-side aggregates (Trade, JournalEntry, BehavioralEvent) and
//     pulling those into Shared.Kernel would invert the layer dependency.
//   - The 5 concrete rules live in Trading.Application — they consume
//     the Trading aggregates to drive their evaluation.
//
//  Severity ordinals (Low=1, Medium=2, High=3) intentionally mirror the
//  Domain.Behavioral.Severity enum; the numeric cast works in both
//  directions. We keep them as distinct types so the kernel does not
//  have to take a dependency on the Trading module.
// ============================================================================

namespace JadeCapital.Shared.Kernel.Coaching;

/// <summary>
/// Three-level scale for coaching prompts.
/// </summary>
public enum Severity : byte
{
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>
/// Call-to-action rendered inside the prompt card. <c>route</c> is a
/// frontend path; <c>label</c> is the Spanish CTA text.
/// </summary>
public sealed record Cta(string Route, string Label);

/// <summary>
/// One coaching nudge produced by a rule. The shape mirrors the wire DTO
/// <c>CoachingPromptDto</c>; the kernel type stays neutral so future
/// non-Trading modules could plug their own rules in the registry.
/// </summary>
public sealed record CoachingPrompt(
    string RuleId,
    Severity Severity,
    string Title,
    string Body,
    Cta Cta,
    DateTimeOffset OccurredAt);
