namespace JadeCapital.Shared.Kernel.Storage;

/// <summary>
/// Result of a virus scan. Lives in <c>Shared.Kernel.Storage</c> next to
/// <see cref="IVirusScanner"/> because it's the wire shape of the scanner
/// contract — same reason <see cref="MarketData.QuoteSource"/> lives next
/// to <see cref="MarketData.IQuoteProvider"/>.
///
/// The default impl in Wave 4d (<c>VirusScannerNoOp</c>) always returns
/// <see cref="Clean"/>. Wave 6 will swap in a real ClamAV impl that can
/// return <see cref="Infected"/> / <see cref="Error"/> / <see cref="Timeout"/>.
/// </summary>
public enum VirusScanResult : byte
{
    /// <summary>Scanner has not run on this stream yet (legacy state — pre-Wave 4d rows).</summary>
    NotScanned = 0,

    /// <summary>Stream was scanned and is clean.</summary>
    Clean = 1,

    /// <summary>Scanner detected a signature match. Attachment must be rejected.</summary>
    Infected = 2,

    /// <summary>Scanner threw a non-transient error. Confirm endpoint should map to 503.</summary>
    Error = 3,

    /// <summary>Scanner exceeded the configured time budget. Transient — caller may retry.</summary>
    Timeout = 4,
}