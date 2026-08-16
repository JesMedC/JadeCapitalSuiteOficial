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

    /// <summary>
    /// Lista los checklists del usuario. Usado por el behavioral analyzer
    /// (slice 2b.1) para cruzar emocionalidad con PnL por bucket.
    /// El analyzer filtra por trade_id en memoria despues de cruzar
    /// con la lista de trades cerrados en el window.
    /// </summary>
    Task<IReadOnlyList<PreTradeChecklist>> ListByUserIdAsync(
        Guid userId,
        CancellationToken ct);
}
