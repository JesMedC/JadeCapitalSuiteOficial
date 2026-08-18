using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.Journal;

namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Contrato de persistencia para el aggregate <see cref="JournalEntry"/>.
///
/// Implementacion EF Core en Infrastructure (TradingDbContext). Cross-user
/// scope: cada Find/List recibe <c>userId</c> como parametro explicito y
/// filtra WHERE user_id = @userId. Es la aplicacion quien pasa el userId
/// autenticado (no se infiere de la row — seria inseguro si el caller
/// construye una row con un userId ajeno).
///
/// Delete es hard delete en Wave 2 (la DB es la fuente de verdad). Soft
/// delete se difiere a Fase 6 (ver tasks.md "Open / deferred to later waves").
/// </summary>
public interface IJournalEntryRepository
{
    /// <summary>Devuelve el entry del usuario para una fecha local, o null si no existe.</summary>
    Task<JournalEntry?> GetByUserAndDateAsync(Guid userId, LocalDate localDate, CancellationToken ct);

    /// <summary>Staggea un nuevo entry para SaveChanges (mismo patron que ChecklistRepository).</summary>
    Task<JournalEntry> AddAsync(JournalEntry entry, CancellationToken ct);

    /// <summary>Marca un entry existente como Modified para SaveChanges.</summary>
    Task<JournalEntry> UpdateAsync(JournalEntry entry, CancellationToken ct);

    /// <summary>Lista los entries del usuario en un rango de fechas locales (inclusivo).</summary>
    Task<IReadOnlyList<JournalEntry>> ListByRangeAsync(
        Guid userId, LocalDate from, LocalDate to, CancellationToken ct);

    /// <summary>Busca un entry por id, filtrado por userId para cross-user scope.</summary>
    Task<JournalEntry?> FindByIdAsync(Guid entryId, Guid userId, CancellationToken ct);

    /// <summary>Borra el entry por id (no filtra por userId — el handler ya valido).</summary>
    Task DeleteAsync(Guid entryId, CancellationToken ct);

    /// <summary>
    /// Decorator-friendly overload. Borra el entry a partir de la entidad
    /// completa (Wave 7, slice 7b.2). Internamente delega a
    /// <see cref="DeleteAsync(Guid, CancellationToken)"/> pasando
    /// <c>entry.Id</c>. Production handlers pueden llamar a cualquiera
    /// de las dos firmas:
    /// <list type="bullet">
    ///   <item>Si solo tienen el <c>Guid</c> (e.g. <c>DeleteJournalEntryHandler</c>):
    ///         <see cref="DeleteAsync(Guid, CancellationToken)"/>.</item>
    ///   <item>Si ya tienen la <see cref="JournalEntry"/> en scope (e.g. un
    ///         handler que la cargo via <see cref="FindByIdAsync"/>): esta
    ///         overload evita un acceso extra a la DB para resolver el id.</item>
    /// </list>
    /// El decorator <c>JournalEntryAuditDecorator</c> envuelve esta
    /// overload (no la Guid-only) para emitir <c>AuditAction.Deleted</c>
    /// con el id + content fields del entry.
    /// </summary>
    Task DeleteAsync(JournalEntry entry, CancellationToken ct);
}
