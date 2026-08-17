using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.Common;

namespace JadeCapital.Trading.Domain.Planner;

// ============================================================================
//  PlannerSession aggregate — slice 3c (Trader Strategies + Alerts + Planner).
//
//  Representa una sesion planeada por el trader: cuando piensa operar, con
//  que instrumento (opcional), y notas libres. El `comparison` (closedTrades
//  on date + followsPlan) NO vive en este aggregate: se computa on-read en el
//  GetPlannerSessionsByWeekHandler via JOIN con trading.trades (spec.md
//  "Compare planned vs actual" requirement).
//
//  Reglas de negocio:
//   - Date ∈ [1900-01-01, 2100-12-31] — enforced por la DB CHECK constraint
//     via LocalDate converter; el aggregate no re-valida (defense in depth).
//   - planned_end_time > planned_start_time si ambos presentes.
//   - notes <= MaxNotesLength (500) chars; null permitido.
//   - symbol null = sesion general (cualquier instrumento). Validacion contra
//     trading.instruments vive en el handler (la DB tiene FK soft via la
//     migration, no enforced en el dominio).
//   - Status default Planned. Transiciones validas: Planned -> Completed |
//     Skipped | Cancelled. Completed/Skipped/Cancelled son terminales
//     idempotentes (segunda llamada preserva el status, NO bumpa UpdatedAt
//     porque no hay cambio real — ver implementacion de MarkXxx).
//   - Update preserva CreatedAt; bumpea UpdatedAt via clock.
//
//  Invariantes:
//   - Id != Guid.Empty (assigned at Create).
//   - UserId != Guid.Empty.
//   - SessionDate != default LocalDate.
//   - Status ∈ {Planned, Completed, Skipped, Cancelled} (byte 1..4).
// ============================================================================

public sealed class PlannerSession : AggregateRoot<Guid>
{
    /// <summary>Max length of notes (matches VARCHAR(500) in the migration).</summary>
    public const int MaxNotesLength = 500;

    public Guid UserId { get; private set; }
    public LocalDate SessionDate { get; private set; }
    public TimeOnly? PlannedStartTime { get; private set; }
    public TimeOnly? PlannedEndTime { get; private set; }
    public string? Symbol { get; private set; }
    public PlannerStatus Status { get; private set; }
    public string? Notes { get; private set; }

    // EF Core.
    private PlannerSession() { }

    private PlannerSession(
        Guid id,
        Guid userId,
        LocalDate sessionDate,
        TimeOnly? plannedStartTime,
        TimeOnly? plannedEndTime,
        string? symbol,
        string? notes,
        IClock clock) : base(id)
    {
        UserId = userId;
        SessionDate = sessionDate;
        PlannedStartTime = plannedStartTime;
        PlannedEndTime = plannedEndTime;
        Symbol = symbol;
        Status = PlannerStatus.Planned;
        Notes = notes;

        var now = clock.UtcNow;
        SetCreatedAt(now);
        UpdatedAt = now;
    }

    /// <summary>
    /// Factory: crea una nueva sesion planeada para el user. Default Status = Planned.
    /// Validaciones (defense in depth con la DB):
    ///   - userId != Guid.Empty.
    ///   - end > start si ambos presentes (sino end_before_start).
    ///   - notes <= MaxNotesLength chars (sino notes_too_long).
    ///   - symbol null o no vacio (validacion contra trading.instruments es en handler).
    /// </summary>
    public static Result<PlannerSession> Create(
        Guid userId,
        LocalDate sessionDate,
        TimeOnly? plannedStartTime,
        TimeOnly? plannedEndTime,
        string? symbol,
        string? notes,
        IClock clock)
    {
        if (userId == Guid.Empty)
            return Result.Failure<PlannerSession>(TradingDomainErrors.Planner.UserIdRequired);

        if (!IsTimeRangeValid(plannedStartTime, plannedEndTime))
            return Result.Failure<PlannerSession>(TradingDomainErrors.Planner.EndBeforeStart);

        if (notes is not null && notes.Length > MaxNotesLength)
            return Result.Failure<PlannerSession>(TradingDomainErrors.Planner.NotesTooLong);

        var normalizedSymbol = NormalizeSymbol(symbol);
        var normalizedNotes = NormalizeText(notes);

        var id = Guid.NewGuid();
        var session = new PlannerSession(
            id, userId, sessionDate,
            plannedStartTime, plannedEndTime,
            normalizedSymbol, normalizedNotes,
            clock);

        session.RaiseDomainEvent(new PlannerSessionCreatedDomainEvent(
            session.Id, session.UserId, clock.UtcNow));

        return Result.Success(session);
    }

    /// <summary>
    /// Actualiza planned_start_time, planned_end_time, symbol y notes.
    /// Preserva CreatedAt y Status; bumpea UpdatedAt via clock.
    /// Mismas validaciones que Create (end > start, notes <= max).
    /// Date NO se actualiza (move semantics: cambiar date = crear nueva sesion).
    /// </summary>
    public Result Update(
        TimeOnly? plannedStartTime,
        TimeOnly? plannedEndTime,
        string? symbol,
        string? notes,
        IClock clock)
    {
        if (!IsTimeRangeValid(plannedStartTime, plannedEndTime))
            return Result.Failure(TradingDomainErrors.Planner.EndBeforeStart);

        if (notes is not null && notes.Length > MaxNotesLength)
            return Result.Failure(TradingDomainErrors.Planner.NotesTooLong);

        PlannedStartTime = plannedStartTime;
        PlannedEndTime = plannedEndTime;
        Symbol = NormalizeSymbol(symbol);
        Notes = NormalizeText(notes);
        UpdatedAt = clock.UtcNow;
        return Result.Success();
    }

    /// <summary>Marca la sesion como Completed. Idempotente (segunda llamada no-op).</summary>
    public Result MarkCompleted(IClock clock)
    {
        if (Status == PlannerStatus.Completed)
            return Result.Success();

        Status = PlannerStatus.Completed;
        UpdatedAt = clock.UtcNow;
        return Result.Success();
    }

    /// <summary>Marca la sesion como Skipped. Idempotente (audit trail preserved).</summary>
    public Result MarkSkipped(IClock clock)
    {
        if (Status == PlannerStatus.Skipped)
            return Result.Success();

        Status = PlannerStatus.Skipped;
        UpdatedAt = clock.UtcNow;
        return Result.Success();
    }

    /// <summary>Marca la sesion como Cancelled. Idempotente.</summary>
    public Result MarkCancelled(IClock clock)
    {
        if (Status == PlannerStatus.Cancelled)
            return Result.Success();

        Status = PlannerStatus.Cancelled;
        UpdatedAt = clock.UtcNow;
        return Result.Success();
    }

    // ===== Helpers =====

    private static bool IsTimeRangeValid(TimeOnly? start, TimeOnly? end)
    {
        if (!start.HasValue || !end.HasValue)
            return true;
        return end.Value > start.Value;
    }

    private static string? NormalizeSymbol(string? symbol)
    {
        if (symbol is null) return null;
        var trimmed = symbol.Trim();
        return trimmed.Length == 0 ? null : trimmed.ToUpperInvariant();
    }

    private static string? NormalizeText(string? input)
        => string.IsNullOrWhiteSpace(input) ? null : input.Trim();
}