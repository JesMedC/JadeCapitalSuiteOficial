using JadeCapital.Shared.Kernel.Repository;
using JadeCapital.Trading.Domain.Instruments;

namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Contrato de persistencia para la entity Instrument.
/// Implementacion EF Core en Infrastructure (TradingDbContext).
/// </summary>
/// <remarks>
/// Wave 8 slice 8a.1 — BREAKING rename from <c>RemoveAsync</c> to
/// <c>DeleteAsync</c> to align with the canonical
/// <c>IRepository&lt;T&gt;.DeleteAsync(T, ct)</c> surface from
/// <c>Shared.Kernel/Repository/IRepository.cs</c>. No <c>[Obsolete]</c>,
/// no overload, no deprecation period. The single handler call site
/// (<c>DeleteInstrumentHandler</c>) is updated atomically in the same slice.
///
/// The interface also extends <see cref="IRepository{T}"/> gaining the
/// canonical generic CRUD surface (<c>GetByIdAsync</c> + <c>AddAsync</c> +
/// <c>UpdateAsync</c> + <c>DeleteAsync</c>). The bespoke read methods
/// (<c>FindByIdAsync</c>, <c>FindBySymbolAsync</c>, <c>ListActiveAsync</c>,
/// <c>ListAllAsync</c>) stay on the interface for backwards compatibility
/// with the existing handler call sites.
/// </remarks>
public interface IInstrumentRepository : IRepository<Instrument>
{
    /// <summary>
    /// Single-fetch by primary key. Bespoke signature — kept on the
    /// interface for backwards compatibility with the existing handlers
    /// (the canonical <see cref="IRepository{T}.GetByIdAsync"/> is also
    /// exposed by the base).
    /// </summary>
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
}