using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Journal;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Persistence;

/// <summary>
/// Implementacion EF Core de <see cref="IJournalEntryRepository"/>.
///
/// Cross-user scope: cada Find/List recibe <c>userId</c> como parametro
/// explicito y la query filtra WHERE user_id = @userId. La aplicacion
/// pasa el userId autenticado (no se infiere de la row — seria inseguro
/// si el caller construye una row con un userId ajeno).
///
/// <c>AddAsync</c> usa <c>DbSet.AddAsync</c> (no tracking hasta el
/// SaveChanges). <c>UpdateAsync</c> verifica el EntityState primero
/// (la entry ya esta tracked si viene del mismo DbContext, e.g. via
/// <c>GetByUserAndDateAsync</c>). El UNIQUE INDEX sobre
/// <c>(user_id, local_date)</c> en la DB es la red de seguridad contra
/// race conditions entre dos POST concurrentes.
/// </summary>
public sealed class JournalEntryRepository : IJournalEntryRepository
{
    private readonly TradingDbContext _db;

    public JournalEntryRepository(TradingDbContext db) { _db = db; }

    public async Task<JournalEntry?> GetByUserAndDateAsync(
        Guid userId, LocalDate localDate, CancellationToken ct)
    {
        var dateOnly = localDate.ToDateOnly();
        return await _db.JournalEntries
            .FirstOrDefaultAsync(j => j.UserId == userId && j.LocalDate == localDate, ct);
    }

    public async Task<JournalEntry> AddAsync(JournalEntry entry, CancellationToken ct)
    {
        await _db.JournalEntries.AddAsync(entry, ct);
        return entry;
    }

    public async Task<JournalEntry> UpdateAsync(JournalEntry entry, CancellationToken ct)
    {
        var entryState = _db.Entry(entry);
        if (entryState.State == EntityState.Detached)
        {
            _db.JournalEntries.Update(entry);
        }
        await Task.CompletedTask;
        return entry;
    }

    public async Task<IReadOnlyList<JournalEntry>> ListByRangeAsync(
        Guid userId, LocalDate from, LocalDate to, CancellationToken ct)
    {
        return await _db.JournalEntries
            .Where(j => j.UserId == userId
                        && j.LocalDate >= from
                        && j.LocalDate <= to)
            .OrderBy(j => j.LocalDate)
            .ToListAsync(ct);
    }

    public async Task<JournalEntry?> FindByIdAsync(
        Guid entryId, Guid userId, CancellationToken ct)
        => await _db.JournalEntries
            .FirstOrDefaultAsync(j => j.Id == entryId && j.UserId == userId, ct);

    public async Task DeleteAsync(Guid entryId, CancellationToken ct)
    {
        var entry = await _db.JournalEntries.FirstOrDefaultAsync(j => j.Id == entryId, ct);
        if (entry is not null)
        {
            _db.JournalEntries.Remove(entry);
        }
    }

    /// <summary>
    /// Decorator-friendly overload (Wave 7, slice 7b.2). Delega a
    /// <see cref="DeleteAsync(Guid, CancellationToken)"/> pasando
    /// <c>entry.Id</c>. Production handlers pueden llamar a esta
    /// overload cuando ya tienen la <see cref="JournalEntry"/> en scope
    /// (e.g. un handler que la cargo via
    /// <see cref="FindByIdAsync(Guid, Guid, CancellationToken)"/>);
    /// evita un acceso extra a la DB para resolver el id.
    /// </summary>
    public async Task DeleteAsync(JournalEntry entry, CancellationToken ct)
        => await DeleteAsync(entry.Id, ct);
}
