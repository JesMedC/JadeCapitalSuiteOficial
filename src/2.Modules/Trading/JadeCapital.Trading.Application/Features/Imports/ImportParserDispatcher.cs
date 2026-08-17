using JadeCapital.Shared.Kernel.Imports;

namespace JadeCapital.Trading.Application.Features.Imports;

/// <summary>
/// Pure helper that selects the right <see cref="IImportRowParser"/> from
/// the registered collection. Slice 5a.2, Wave 5.
///
/// <para>
/// Iterates <paramref name="parsers"/> in DI registration order and picks
/// the first one whose <see cref="IImportRowParser.CanParse"/> returns a
/// confidence score &gt;= 0.8. Returns <c>null</c> if no parser matches —
/// the caller (the importer endpoint) MUST surface that as
/// <c>422 import.format_unrecognized</c>.
/// </para>
///
/// <para>
/// Registration order matters: the spec convention is MT4/MT5 first, then
/// CSV as fallback. Because <c>CsvImportRowParser.CanParse</c> scores &lt; 0.8
/// on MT4/MT5 headers and vice versa, the order is the tiebreaker for
/// ambiguous headers (e.g. a CSV that happens to contain "ticket" but no
/// <c>SL</c>/<c>TP</c> columns).
/// </para>
/// </summary>
public static class ImportParserDispatcher
{
    /// <summary>Confidence threshold — first parser with score &gt;= this wins.</summary>
    public const double ConfidenceThreshold = 0.8;

    /// <summary>
    /// Selects the first parser whose <see cref="IImportRowParser.CanParse"/>
    /// returns &gt;= <see cref="ConfidenceThreshold"/>. Returns <c>null</c> if
    /// none match.
    /// </summary>
    /// <param name="parsers">Registered parsers in DI order.</param>
    /// <param name="fileName">Original filename — used as a hint.</param>
    /// <param name="head">Seekable head stream for sniffing (caller retains ownership).</param>
    public static IImportRowParser? SelectParser(
        IEnumerable<IImportRowParser> parsers, string fileName, Stream head)
    {
        if (parsers is null) return null;

        foreach (var parser in parsers)
        {
            if (parser is null) continue;
            try
            {
                var score = parser.CanParse(fileName, head);
                if (score >= ConfidenceThreshold)
                    return parser;
            }
            catch
            {
                // Defensive: a buggy parser must NOT break the dispatcher.
                continue;
            }
        }

        return null;
    }
}
