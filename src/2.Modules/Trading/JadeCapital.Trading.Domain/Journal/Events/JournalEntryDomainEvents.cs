using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Trading.Domain.Journal;

/// <summary>
/// Domain event emitido cuando un <see cref="JournalEntry"/> es creado
/// por primera vez (no incluye payload completo para minimizar la
/// superficie — los consumidores pueden leer el entry por id).
///
/// El FE Wave 2b (BehavioralAnalytics) y Wave 2d (CoachingPrompts)
/// pueden consumir este evento para refrescar caches o emitir
/// notificaciones push.
/// </summary>
public sealed record JournalEntryCreatedDomainEvent(
    Guid EntryId,
    Guid UserId,
    LocalDate LocalDate,
    DateTimeOffset OccurredOn) : IDomainEvent;

/// <summary>
/// Domain event emitido cuando un <see cref="JournalEntry"/> existente
/// es actualizado (upsert sobre una fecha ya con entry). Mismo shape
/// que <see cref="JournalEntryCreatedDomainEvent"/> sin el LocalDate
/// (que no cambia en un Update).
/// </summary>
public sealed record JournalEntryUpdatedDomainEvent(
    Guid EntryId,
    Guid UserId,
    DateTimeOffset OccurredOn) : IDomainEvent;
