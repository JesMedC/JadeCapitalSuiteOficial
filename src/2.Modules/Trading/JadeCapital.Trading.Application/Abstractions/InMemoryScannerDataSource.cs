using JadeCapital.Trading.Domain.Instruments;

namespace JadeCapital.Trading.Application.Abstractions;

// Stub impl for Wave 4a. Wave 4b will replace with DB-backed version reading
// the quotes_cache table. For now: returns the seeded active instruments list.
public sealed class InMemoryScannerDataSource : IScannerDataSource
{
    private readonly IInstrumentRepository _instruments;

    public InMemoryScannerDataSource(IInstrumentRepository instruments)
    {
        _instruments = instruments;
    }

    public async Task<IReadOnlyList<Instrument>> GetInstrumentsAsync(CancellationToken ct)
    {
        return await _instruments.ListActiveAsync(ct);
    }
}
