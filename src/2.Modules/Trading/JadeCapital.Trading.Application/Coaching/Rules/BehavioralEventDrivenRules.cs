using JadeCapital.Shared.Kernel.Coaching;
using DomainSeverity = JadeCapital.Trading.Domain.Behavioral.Severity;

namespace JadeCapital.Trading.Application.Coaching.Rules;

// ============================================================================
//  Slice 2d.1 — three behavioral-event-driven rules.
//
//  Each rule is a thin adapter from <see cref="BehavioralEvent"/> to one
//  or more <see cref="CoachingPrompt"/>. The detection logic lives in
//  <see cref="BehavioralAnalyzer"/> (slice 2b); here we ONLY translate
//  each event into a prompt. The mapping is 1:1 per event — the analyzer
//  already enforced the precise trigger conditions, so the rule does
//  not duplicate them.
//
//  All three rules:
//   - Filter BehavioralEvents by RuleId (analyzer-produced RuleIds).
//   - Map Domain.Severity → Shared.Kernel.Severity via numeric cast
//     (both enums share Low=1 / Medium=2 / High=3).
//   - Render hardcoded Spanish copy + CTA. Copy is qualitative — no
//     absolute PnL amount, no percentage (PII guard, see
//     CoachingPiiTests).
// ============================================================================

internal static class SeverityMap
{
    /// <summary>
    /// Domain <c>Domain.Behavioral.Severity</c> and
    /// <see cref="Severity"/> share Low=1 / Medium=2 / High=3 — the
    /// numeric cast keeps the rules DRY without leaking the domain enum
    /// into the Shared.Kernel layer.
    /// </summary>
    public static Severity ToKernel(this DomainSeverity s)
        => (Severity)(byte)s;
}

/// <summary>
/// Wrap a single <c>RevengeTrade</c> event into a High-severity prompt
/// pointing at the journal so the trader can dump the urge.
/// </summary>
public sealed class RevengeTradeRule : ICoachingRule
{
    public string RuleId => "RevengeTrade";
    public int Priority => 100;

    public IReadOnlyList<CoachingPrompt> Evaluate(CoachingContext ctx)
    {
        // Task spec: High severity for every RevengeTrade prompt. The
        // analyzer already filters to qualified events; we don't carry
        // the per-event severity to the prompt — all revenge trades
        // deserve a stop-and-reflect nudge.
        return ctx.BehavioralEvents
            .Where(e => e.RuleId == RuleId)
            .Select(e => new CoachingPrompt(
                RuleId: RuleId,
                Severity: Severity.High,
                Title: "Revenge trading detectado",
                Body: "Detectamos revenge trading — ¿estabas emocionalmente afectado por la pérdida anterior? Te recomendamos un break de 30 min.",
                Cta: new Cta("/app/journal", "Escribir en el diario"),
                OccurredAt: e.OccurredAt))
            .ToList();
    }
}

/// <summary>
/// Wrap a single <c>OvertradingDay</c> event into a Medium-severity
/// prompt pointing at the patterns dashboard.
/// </summary>
public sealed class OvertradingDayRule : ICoachingRule
{
    public string RuleId => "OvertradingDay";
    public int Priority => 200;

    private const string Copy =
        "Operaste N veces hoy — tu promedio es M. ¿Estás sobre-operando?";

    public IReadOnlyList<CoachingPrompt> Evaluate(CoachingContext ctx)
    {
        return ctx.BehavioralEvents
            .Where(e => e.RuleId == RuleId)
            .Select(e => new CoachingPrompt(
                RuleId: RuleId,
                Severity: Severity.Medium,
                Title: "Sobreoperativa",
                Body: Copy,
                Cta: new Cta("/app/patterns", "Ver patrones"),
                OccurredAt: e.OccurredAt))
            .ToList();
    }
}

/// <summary>
/// Wrap a single <c>TiltSequence</c> event into a High-severity prompt
/// pointing at the recent trades so the trader can verify the streak.
/// Fires first because of Priority=50 (lowest = most urgent).
/// </summary>
public sealed class TiltSequenceRule : ICoachingRule
{
    public string RuleId => "TiltSequence";
    public int Priority => 50;

    public IReadOnlyList<CoachingPrompt> Evaluate(CoachingContext ctx)
    {
        return ctx.BehavioralEvents
            .Where(e => e.RuleId == RuleId)
            .Select(e => new CoachingPrompt(
                RuleId: RuleId,
                Severity: Severity.High,
                Title: "Posible tilt detectado",
                Body: "3 pérdidas consecutivas en 90 min — posible tilt. Cierra el terminal y volvé mañana con cabeza fresca.",
                Cta: new Cta("/app/trades", "Revisar operaciones"),
                OccurredAt: e.OccurredAt))
            .ToList();
    }
}
