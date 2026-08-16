using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.PreTradeChecklists;

namespace JadeCapital.Trading.Infrastructure.Persistence;

/// <summary>
/// Implementacion EF Core de <see cref="IPreTradeChecklistRepository"/>.
///
/// Solo expone <c>AddAsync</c>: el checklist es write-once al abrir el
/// trade (no hay UPDATE ni DELETE en el flujo del producto). Si el trade
/// padre se borra, la FK con ON DELETE CASCADE limpia el checklist
/// automaticamente — no necesitamos un Remove explicito.
///
/// <c>AddAsync</c> usa <c>DbSet.AddAsync</c> (no tracking hasta el
/// SaveChanges). El UNIQUE INDEX sobre <c>trade_id</c> en la DB es la
/// red de seguridad contra doble-persistencia concurrente.
/// </summary>
public sealed class ChecklistRepository : IPreTradeChecklistRepository
{
    private readonly TradingDbContext _db;

    public ChecklistRepository(TradingDbContext db) { _db = db; }

    public async Task AddAsync(PreTradeChecklist checklist, CancellationToken ct)
        => await _db.PreTradeChecklists.AddAsync(checklist, ct);
}
