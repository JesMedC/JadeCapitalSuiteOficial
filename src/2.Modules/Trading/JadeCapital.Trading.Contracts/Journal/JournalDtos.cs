using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Trading.Contracts.Journal;

/// <summary>
/// DTO del journal entry diario devuelto al frontend. Proyeccion del
/// aggregate <see cref="JadeCapital.Trading.Domain.Journal.JournalEntry"/>
/// a un shape wire-friendly.
///
/// <c>LocalDate</c> se serializa como string ISO 8601 (<c>YYYY-MM-DD</c>)
/// porque System.Text.Json no sabe serializar <see cref="LocalDate"/>
/// directamente. Lo mismo para <c>Tags</c> (<c>string[]</c> en lugar de
/// <c>IReadOnlyList&lt;string&gt;</c>).
///
/// Los timestamps (<c>CreatedAt</c> / <c>UpdatedAt</c>) se exponen como
/// <see cref="DateTimeOffset"/> con offset explicito — el FE los usa para
/// el "ultima modificacion" del card.
/// </summary>
public sealed record JournalEntryDto(
    Guid Id,
    string LocalDate,
    string Timezone,
    byte? MoodPre,
    byte? MoodDuring,
    byte? MoodPost,
    string? PremarketPlan,
    string? PostmarketReflection,
    string[]? Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Request body de <c>POST /api/journal/today</c> (upsert). Todos los
/// campos son opcionales; el aggregate rechaza con
/// <c>validation.journal.nothing_to_save</c> si todos son null/empty.
///
/// Usamos tipos primitivos (<c>byte?</c>) en lugar de
/// <see cref="JadeCapital.Trading.Domain.Journal.Mood"/> para que
/// System.Text.Json deserialice sin necesidad de un JsonConverter
/// custom. El handler hace el cast explicito
/// (<c>Mood.FromTrusted(req.MoodPre.Value)</c>) — la validacion de
/// rango la enforce el VO <see cref="JadeCapital.Trading.Domain.Journal.Mood.Create"/>.
/// </summary>
public sealed record UpsertJournalEntryRequest(
    byte? MoodPre,
    byte? MoodDuring,
    byte? MoodPost,
    string? PremarketPlan,
    string? PostmarketReflection,
    string[]? Tags);
