using JadeCapital.Shared.Kernel.Alerts;
using AlertAggregate = JadeCapital.Trading.Domain.Alerts.Alert;
using AlertWire = JadeCapital.Shared.Kernel.Alerts.Alert;
using AlertDtoWire = JadeCapital.Trading.Contracts.Alerts.AlertDto;

namespace JadeCapital.Trading.Application._Common;

// ============================================================================
//  AlertMapping — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Centralized projection from the Alert aggregate (or wire shape) to
//  the wire DTO (AlertDto in Trading.Contracts.Alerts). Lives in
//  Application because it bridges the domain aggregate with the wire
//  shape.
//
//  The Aggregate.ToDto overload is the one used by handlers (List, GetById,
//  Acknowledge). The wire-shape overload is kept for symmetry with
//  StrategyMapping (unused today; reserved for the
//  AlertEvaluationService's "preview without persisting" path that
//  Wave 4+ might add).
// ============================================================================

public static class AlertMapping
{
    public static AlertDtoWire ToDto(this AlertAggregate aggregate)
        => new(
            Id: aggregate.Id,
            RuleId: aggregate.RuleId,
            Severity: aggregate.Severity.ToString(),
            Title: aggregate.Title,
            Body: aggregate.Body,
            Cta: new JadeCapital.Trading.Contracts.Alerts.AlertCtaDto(
                aggregate.CtaRoute, aggregate.CtaLabel),
            AcknowledgedAt: aggregate.AcknowledgedAt,
            ExpiresAt: aggregate.ExpiresAt,
            CreatedAt: aggregate.CreatedAt);

    public static AlertDtoWire ToDto(this AlertWire wire)
        => new(
            Id: Guid.Empty,
            RuleId: wire.RuleId,
            Severity: wire.Severity.ToString(),
            Title: wire.Title,
            Body: wire.Body,
            Cta: new JadeCapital.Trading.Contracts.Alerts.AlertCtaDto(
                wire.Cta.Route, wire.Cta.Label),
            AcknowledgedAt: null,
            ExpiresAt: wire.ExpiresAt,
            CreatedAt: DateTimeOffset.MinValue);
}