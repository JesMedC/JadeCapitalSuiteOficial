using JadeCapital.Trading.Domain.Instruments;

namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Contrato de persistencia para la entity Instrument.
/// Implementacion EF Core en Infrastructure (TradingDbContext).
/// </summary>
public interface IInstrumentRepository
{
    Task<Instrument?> FindByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Lookup por symbol canonico (normalizado a mayusculas). Util para
    /// detectar duplicados al crear un instrumento nuevo.
    /// </summary>
    Task<Instrument?> FindBySymbolAsync(string symbol, CancellationToken ct);

    /// <summary>
    /// Lista todos los instrumentos activos (IsActive = true). Default en
    /// UI operativa (selector de simbolo para abrir un trade).
    /// </summary>
    Task<IReadOnlyList<Instrument>> ListActiveAsync(CancellationToken ct);

    /// <summary>
    /// Lista todos los instrumentos, activos e inactivos. Para admin /
    /// debugging. Ordenado por Symbol ascendente.
    /// </summary>
    Task<IReadOnlyList<Instrument>> ListAllAsync(CancellationToken ct);

    Task AddAsync(Instrument instrument, CancellationToken ct);

    /// <summary>
    /// Borra fisicamente un instrumento. El handler es responsable de validar
    /// previamente que no tenga trades asociados (la FK en DB es RESTRICT).
    /// </summary>
    Task RemoveAsync(Instrument instrument, CancellationToken ct);
}
