using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Trading.Contracts.Coaching;
using JadeCapital.Trading.Domain.Ai;
using DomainAiCoachingPrompt = JadeCapital.Trading.Domain.Ai.CoachingPrompt;
using SharedCoachingPrompt = JadeCapital.Shared.Kernel.Coaching.CoachingPrompt;

namespace JadeCapital.Trading.Application._Common;

// ============================================================================
//  CoachingMapping — slice 2d.1 (Trader Journal Core) + slice 5b.2 (AI).
//
//  Wire projection for both rule-based and AI coaching prompts. Stays in
//  Application (not in the Shared.Kernel coaching type) because it
//  references the wire Contracts DTOs — which are a wire-layer concern.
//
//  Severity serializes as a lowercase string ("low" | "medium" | "high")
//  to match the conventions of other BehavioralPeriod / Severity mappings
//  in the project (see BehavioralMapping for the side-by-side rule).
//
//  CTA routing table (slice 5b.2):
//   - Low    → /app/journal ("Revisar journal")
//   - Medium → /app/coaching ("Ver más detalles")
//   - High   → /app/trades ("Revisar trades recientes")
// ============================================================================

public static class CoachingMapping
{
    public static CoachingPromptsDto ToDto(
        IReadOnlyList<SharedCoachingPrompt> prompts,
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

    /// <summary>
    /// Projects an AI coaching prompt (Wave 5b.2) into the wire DTO. The
    /// <c>kind</c> discriminator is always "ai" — the AI table never stores
    /// Rule kind rows.
    /// </summary>
    public static AiCoachingPromptDto ToDto(DomainAiCoachingPrompt prompt)
    {
        var severityWire = prompt.Severity switch
        {
            CoachingPromptSeverity.Low => "low",
            CoachingPromptSeverity.Medium => "medium",
            CoachingPromptSeverity.High => "high",
            _ => "low",
        };

        var cta = ResolveAiCta(prompt.Severity);

        return new AiCoachingPromptDto(
            Id: prompt.Id,
            Kind: "ai",
            Severity: severityWire,
            Model: prompt.Model,
            LatencyMs: prompt.LatencyMs,
            Text: prompt.ProviderResponseText,
            ProviderResponse: prompt.ProviderResponseText,  // raw text — FE renders verbatim
            PromptText: prompt.PromptText,
            ContextJson: prompt.ContextJson,
            CreatedAt: prompt.CreatedAt,
            Cta: cta);
    }

    private static CoachingCtaDto ResolveAiCta(CoachingPromptSeverity severity) => severity switch
    {
        CoachingPromptSeverity.Low => new CoachingCtaDto("/app/journal", "Revisar journal"),
        CoachingPromptSeverity.Medium => new CoachingCtaDto("/app/coaching", "Ver más detalles"),
        CoachingPromptSeverity.High => new CoachingCtaDto("/app/trades", "Revisar trades recientes"),
        _ => new CoachingCtaDto("/app/journal", "Revisar journal"),
    };

    private static string SeverityToWire(Severity severity) => severity switch
    {
        Severity.Low    => "low",
        Severity.Medium => "medium",
        Severity.High   => "high",
        _               => "low",
    };
}
