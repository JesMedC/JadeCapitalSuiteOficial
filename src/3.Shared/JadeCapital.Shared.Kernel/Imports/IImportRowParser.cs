namespace JadeCapital.Shared.Kernel.Imports;

/// <summary>
/// Pluggable row parser for an import source (CSV, MT4, MT5). The
/// streaming pipeline resolves one of N registered parsers at runtime —
/// the first one whose <see cref="CanParse"/> returns &gt;= 0.8 wins.
///
/// <para>
/// Cross-module contract: Trading.Application owns the pipeline but the
/// interface lives in Shared.Kernel because the wire shape (an
/// <see cref="ImportRow"/>) is shared. Mirrors the
/// <see cref="JadeCapital.Shared.Kernel.MarketData.IQuoteProvider"/>
/// precedent.
/// </para>
/// </summary>
public interface IImportRowParser
{
    /// <summary>Stable identifier for the parser — used in ImportJob.Format and dedupe routing.</summary>
    ImportFormat Format { get; }

    /// <summary>
    /// Confidence score 0..1 — the dispatcher picks the first parser whose
    /// score is &gt;= 0.8. <paramref name="head"/> is a short seekable
    /// stream (already buffered for sniffing; do NOT advance it past the
    /// first KiB or so). Callers MUST NOT close <paramref name="head"/>.
    /// </summary>
    double CanParse(string fileName, Stream head);

    /// <summary>
    /// Streams rows from the body. <paramref name="ct"/> is observed for
    /// cooperative cancellation between rows. <paramref name="body"/> is
    /// fully read; the parser MUST NOT buffer the entire body in memory.
    /// </summary>
    IAsyncEnumerable<ImportRow> ParseAsync(Stream body, CancellationToken ct = default);
}