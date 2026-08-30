namespace JadeCapital.Trading.Contracts.Alerts;

// ============================================================================
//  Alerts wire contracts — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Mirror of the wire shape produced by AlertRegistry in
//  JadeCapital.Trading.Application/Alerts and persisted as rows in
//  trading.alerts (0015b_alerts.sql).
//
//  System.Text.Json serializes the Severity enum as its underlying byte
//  on the wire (default policy of the API host). The FE maps byte → label
//  via SEVERITY_LABELS for color rendering.
// ============================================================================

public sealed record AlertDto(
    Guid Id,
    string RuleId,
    string Severity,
    string Title,
    string Body,
    AlertCtaDto Cta,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt);

public sealed record AlertCtaDto(string Route, string Label);