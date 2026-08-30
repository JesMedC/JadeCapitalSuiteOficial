using JadeCapital.Trading.Domain.Instruments;

namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Read-only data source for the scanner. Wave 4a returns instruments via
/// Instrument only; Wave 4b will extend this interface with GetQuoteAsync
/// once the quotes_cache table is in place.
/// </summary>
public interface IScannerDataSource
{
    Task<IReadOnlyList<Instrument>> GetInstrumentsAsync(CancellationToken ct);
}
