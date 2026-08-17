namespace JadeCapital.Trading.Contracts.Planner;

// ============================================================================
//  Planner wire contracts — slice 3c (Trader Strategies + Alerts + Planner).
//
//  Mirror de las proyecciones desde
//  JadeCapital.Trading.Application.Features.Planner.*.
//
//  Shape del DTO de sesion:
//   - id (Guid) y sessionDate (LocalDate como ISO 8601 "YYYY-MM-DD").
//   - plannedStartTime / plannedEndTime serializan como "HH:mm" o null.
//   - symbol nullable (sesion general).
//   - status: byte 1..4 (Planned=1, Completed=2, Skipped=3, Cancelled=4).
//     System.Text.Json serializa el enum PlannerStatus como byte.
//   - notes nullable.
//
//  WeekComparison: payload agregado para GET /api/planner/week. El handler
//  cuenta planned/completed/skipped y suma los PnL de los trades cerrados en
//  esa semana (cross-user isolation via userId del JWT claim).
// ============================================================================

public sealed record PlannerSessionDto(
    Guid Id,
    Guid UserId,
    string SessionDate,
    string? PlannedStartTime,
    string? PlannedEndTime,
    string? Symbol,
    byte Status,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PlannerSessionComparisonDto(
    Guid SessionId,
    byte Status,
    int ActualTradeCount,
    int ActualClosedTradeCount,
    IReadOnlyList<string> ActualSymbols,
    bool FollowsPlan);

public sealed record PlannerWeekComparisonDto(
    int Planned,
    int Completed,
    int Skipped,
    int Cancelled,
    int ActualTrades,
    decimal TotalPnl);

public sealed record PlannerWeekDto(
    string WeekStartDate,
    IReadOnlyList<PlannerSessionWithComparisonDto> Sessions,
    PlannerWeekComparisonDto Comparison);

public sealed record PlannerSessionWithComparisonDto(
    PlannerSessionDto Session,
    PlannerSessionComparisonDto Comparison);

public sealed record UpsertPlannerSessionRequest(
    string SessionDate,
    string? PlannedStartTime,
    string? PlannedEndTime,
    string? Symbol,
    string? Notes);

public sealed record UpdatePlannerStatusRequest(byte NewStatus);