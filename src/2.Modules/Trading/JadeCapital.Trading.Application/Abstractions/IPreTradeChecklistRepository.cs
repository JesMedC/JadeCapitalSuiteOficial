using JadeCapital.Trading.Domain.PreTradeChecklists;

namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Contrato de persistencia para el aggregate PreTradeChecklist.
///
/// Implementacion EF Core en Infrastructure (TradingDbContext). Solo
/// expone AddAsync — el checklist es write-once al abrir el trade; no
/// hay UPDATE ni DELETE (la FK a trading.trades con ON DELETE CASCADE
/// cubre la limpieza si el trade padre se borra).
/// </summary>
public interface IPreTradeChecklistRepository
{
    Task AddAsync(PreTradeChecklist checklist, CancellationToken ct);
}
