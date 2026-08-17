namespace JadeCapital.Shared.Kernel.MarketData;

/// <summary>
/// Source of the quote. Identifies which provider produced the value
/// so downstream consumers (alerts, scanner, FE) can display provenance
/// and tests can assert the wire shape.
/// </summary>
public enum QuoteSource : byte
{
    Stub = 0,
    Mock = 1,
    Live = 2,
    Broker = 3,
}
