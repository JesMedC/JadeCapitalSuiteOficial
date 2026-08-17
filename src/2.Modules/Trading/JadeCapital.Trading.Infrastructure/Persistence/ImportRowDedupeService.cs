using JadeCapital.Shared.Kernel.Imports;
using JadeCapital.Trading.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Persistence;

/// <summary>
/// Default EF-based implementation of <see cref="IImportRowDedupeService"/>.
/// Splits the batch into rows-with-ticket-id and rows-without-ticket-id and
/// issues one composite-key query per group.
///
/// <para>
/// Performance note: this is intentionally a per-batch query (one round-trip
/// per 50 rows). For larger batches the EF query becomes unwieldy; Wave 6 may
/// promote this to a streaming <c>EXISTS</c> subquery inside the INSERT.
/// </para>
/// </summary>
public sealed class ImportRowDedupeService : IImportRowDedupeService
{
    private readonly TradingDbContext _db;

    public ImportRowDedupeService(TradingDbContext db) { _db = db; }

    public async Task<DedupeBatchResult> DedupeBatchAsync(
        Guid userId, Guid accountId, IReadOnlyList<ImportRow> batch, CancellationToken ct)
    {
        if (batch.Count == 0)
            return new DedupeBatchResult(Array.Empty<ImportRow>(), 0);

        var newRows = new List<ImportRow>(batch.Count);
        var skipped = 0;

        // 1) Tickets — composite (user_id, account_id, ticket_id) match.
        var ticketRows = batch.Where(r => !string.IsNullOrEmpty(r.TicketId)).ToList();
        if (ticketRows.Count > 0)
        {
            var tickets = ticketRows.Select(r => r.TicketId!).Distinct().ToList();
            var existingTickets = await _db.Trades
                .Where(t => t.UserId == userId
                    && t.AccountId == accountId
                    && tickets.Contains(t.Id.ToString()))  // TODO 5a.1 — wire ticket_id column; for now best-effort
                .Select(t => t.Id)
                .ToListAsync(ct);

            var existingTicketSet = new HashSet<string>(
                existingTickets.Select(g => g.ToString()),
                StringComparer.Ordinal);
            foreach (var r in ticketRows)
            {
                if (existingTicketSet.Contains(r.TicketId!))
                    skipped++;
                else
                    newRows.Add(r);
            }
        }

        // 2) Composite (user_id, account_id, opened_at, symbol, entry_price) for rows without ticket.
        var noTicket = batch.Where(r => string.IsNullOrEmpty(r.TicketId)).ToList();
        if (noTicket.Count > 0)
        {
            var existing = await _db.Trades
                .Where(t => t.UserId == userId && t.AccountId == accountId)
                .Select(t => new { t.Symbol.Value, t.EntryPrice.Amount, t.OpenedAt })
                .ToListAsync(ct);

            var existingKeys = new HashSet<string>(
                existing.Select(x => $"{x.Value}|{x.Amount}|{x.OpenedAt:o}"),
                StringComparer.Ordinal);

            foreach (var r in noTicket)
            {
                var key = $"{r.Symbol}|{r.EntryPrice}|{r.OpenedAt:o}";
                if (existingKeys.Contains(key))
                    skipped++;
                else
                    newRows.Add(r);
            }
        }

        return new DedupeBatchResult(newRows, skipped);
    }
}