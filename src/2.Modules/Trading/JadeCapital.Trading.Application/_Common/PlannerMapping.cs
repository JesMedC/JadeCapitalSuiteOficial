using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Contracts.Planner;
using JadeCapital.Trading.Domain.Planner;

namespace JadeCapital.Trading.Application._Common;

// ============================================================================
//  PlannerMappingExtensions — slice 3c.
//
//  Extension centralizada para proyectar el aggregate PlannerSession al DTO
//  wire. TimeOnly? se serializa como "HH:mm" (formato 24h, 5 chars) para
//  que el FE pueda parsear con new Date(). Status va como byte (1..4).
// ============================================================================

public static class PlannerMappingExtensions
{
    private const string TimeFormat = "HH:mm";

    public static PlannerSessionDto ToDto(this PlannerSession s)
        => new(
            s.Id,
            s.UserId,
            s.SessionDate.Iso8601,
            s.PlannedStartTime?.ToString(TimeFormat),
            s.PlannedEndTime?.ToString(TimeFormat),
            s.Symbol,
            (byte)s.Status,
            s.Notes,
            s.CreatedAt,
            s.UpdatedAt ?? s.CreatedAt);

    /// <summary>Parser defensivo para "HH:mm" (5 chars) → TimeOnly. Null si el input es null/empty.</summary>
    public static TimeOnly? ParseTimeOnly(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (TimeOnly.TryParseExact(trimmed, TimeFormat, out var t))
            return t;
        // Fallback: aceptar tambien "H:mm" (e.g. "9:00").
        if (TimeOnly.TryParseExact(trimmed, "H:mm", out t))
            return t;
        return TimeOnly.Parse(trimmed);
    }

    /// <summary>Parser defensivo para "YYYY-MM-DD" → LocalDate.</summary>
    public static LocalDate? ParseLocalDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (System.DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd", out var d))
            return LocalDate.From(d);
        return null;
    }
}