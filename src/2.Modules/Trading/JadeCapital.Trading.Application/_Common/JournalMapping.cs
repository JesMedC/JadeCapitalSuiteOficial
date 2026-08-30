using JadeCapital.Trading.Contracts.Journal;
using JadeCapital.Trading.Domain.Journal;

namespace JadeCapital.Trading.Application._Common;

/// <summary>
/// Mapeos entre el aggregate <see cref="JournalEntry"/> y los DTOs wire
/// (<see cref="JournalEntryDto"/>, etc.).
///
/// La conversion no toca navegaciones — el handler pasa el aggregate
/// hidratado y devuelve el DTO. Si en Wave 2b+ queremos proyectar a
/// shapes derivados (e.g. con metricas calculadas), se agrega una
/// overload que reciba parametros extra.
/// </summary>
public static class JournalMappingExtensions
{
    /// <summary>
    /// Convierte un aggregate <see cref="JournalEntry"/> en su DTO.
    /// <c>LocalDate</c> se serializa como ISO 8601 (<c>YYYY-MM-DD</c>);
    /// <c>Tags</c> como <c>string[]</c> (la lista interna es
    /// <c>IReadOnlyList&lt;string&gt;</c>).
    /// </summary>
    public static JournalEntryDto ToDto(this JournalEntry entry)
        => new(
            entry.Id,
            entry.LocalDate.Iso8601,
            entry.Timezone,
            entry.MoodPre?.Value,
            entry.MoodDuring?.Value,
            entry.MoodPost?.Value,
            entry.PremarketPlan,
            entry.PostmarketReflection,
            entry.Tags.Count == 0 ? null : entry.Tags.ToArray(),
            entry.CreatedAt,
            entry.UpdatedAt ?? entry.CreatedAt);
}
