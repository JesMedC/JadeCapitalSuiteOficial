using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Trading.Contracts.Coaching;

namespace JadeCapital.Trading.Application._Common;

// ============================================================================
//  CoachingMapping — slice 2d.1 (Trader Journal Core).
//
//  Wire projection for <see cref="CoachingPrompt"/>. Stays in
//  Application (not in the Shared.Kernel coaching type) because it
//  references the wire Contracts DTOs — which are a wire-layer concern.
//
//  Severity serializes as a lowercase string ("low" | "medium" | "high")
//  to match the conventions of other BehavioralPeriod / Severity mappings
//  in the project (see BehavioralMapping for the side-by-side rule).
// ============================================================================

public static class CoachingMapping
{
    public static CoachingPromptsDto ToDto(
        IReadOnlyList<CoachingPrompt> prompts,
        CoachingPeriod period)
    {
        var periodString = period switch
        {
            CoachingPeriod.Days7  => "7d",
            CoachingPeriod.Days30 => "30d",
            CoachingPeriod.Days90 => "90d",
            CoachingPeriod.All   => "all",
            _                    => "30d",
        };

        var dtos = prompts.Select(p => new CoachingPromptDto(
            RuleId: p.RuleId,
            Severity: SeverityToWire(p.Severity),
            Title: p.Title,
            Body: p.Body,
            Cta: new CoachingCtaDto(p.Cta.Route, p.Cta.Label),
            OccurredAt: p.OccurredAt)).ToList();

        return new CoachingPromptsDto(periodString, dtos);
    }

    private static string SeverityToWire(Severity severity) => severity switch
    {
        Severity.Low    => "low",
        Severity.Medium => "medium",
        Severity.High   => "high",
        _               => "low",
    };
}
