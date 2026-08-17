using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.Trades;

namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Contrato de persistencia para el aggregate Trade.
/// Implementacion EF Core en Infrastructure (TradingDbContext).
/// </summary>
public interface ITradeRepository
{
    Task<Trade?> FindByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Lista paginada del usuario con filtros opcionales por estado, simbolo
    /// y cuenta. Ordenada por OpenedAt descendente.
    /// </summary>
    Task<IReadOnlyList<Trade>> ListByUserIdAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken ct,
        TradeStatus? statusFilter = null,
        string? symbolFilter = null,
        Guid? accountIdFilter = null);

    Task<int> CountByUserIdAsync(
        Guid userId,
        CancellationToken ct,
        TradeStatus? statusFilter = null,
        string? symbolFilter = null,
        Guid? accountIdFilter = null);

    /// <summary>
    /// Lista TODOS los trades del usuario en el rango [from, to] por OpenedAt,
    /// SIN paginar. Usado por los queries de dashboard / calendario.
    /// </summary>
    Task<IReadOnlyList<Trade>> ListByUserIdAndOpenedAtRangeAsync(
        Guid userId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);

    /// <summary>
    /// Lista TODOS los trades cerrados del usuario sin limite inferior ni
    /// superior. Usado por el behavioral analyzer con <c>period=all</c>.
    /// El analyzer filtra por <c>ClosedAt</c> en memoria — esta query es
    /// la red de seguridad para no paginar accidentalmente el historial
    /// completo del usuario. Wave 3 introducira un cursor si algun user
    /// pasa de ~10k trades.
    /// </summary>
    Task<IReadOnlyList<Trade>> ListClosedByUserIdAsync(
        Guid userId,
        CancellationToken ct);

    /// <summary>
    /// Cuenta cuantos trades (de cualquier usuario) apuntan a un instrumento.
    /// Usado por DeleteInstrument para el pre-check de FK RESTRICT.
    /// </summary>
    Task<int> CountByInstrumentIdAsync(Guid instrumentId, CancellationToken ct);

    Task AddAsync(Trade trade, CancellationToken ct);

    Task UpdateAsync(Trade trade, CancellationToken ct);

    /// <summary>
    /// Borra fisicamente un trade. Solo aplica a trades Open/Cancelled
    /// (los Closed quedan como registro historico — validacion a nivel handler).
    /// </summary>
    Task RemoveAsync(Trade trade, CancellationToken ct);
}