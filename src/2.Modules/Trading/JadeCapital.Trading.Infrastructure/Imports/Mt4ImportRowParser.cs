using System.Globalization;
using System.Runtime.CompilerServices;
using JadeCapital.Shared.Kernel.Imports;

namespace JadeCapital.Trading.Infrastructure.Imports;

/// <summary>
/// MT4/MT5 row parser for the importer pipeline (slice 5a.2, Wave 5).
///
/// <para>
/// A single implementation handles BOTH the MT4 "ticket history" export and
/// the MT5 "deals history" export because they share the same CSV/TSV
/// infrastructure. Format detection happens at <see cref="CanParse"/> time
/// (scores both header signatures), and inside <see cref="ParseAsync"/> the
/// presence of a <c>Position ID</c> column switches the parser into MT5
/// aggregation mode (group deals by Position ID → one row per position).
/// </para>
///
/// <para>
/// MT4 export: one row per trade. Columns: <c>Ticket, Open Time, Type,
/// Volume, Symbol, Open Price, SL, TP, Close Time, Close Price, Commission,
/// Swap, Profit</c>, optional <c>Comment</c>. Tab or comma delimiter.
/// </para>
///
/// <para>
/// MT5 deals export: one row per DEAL (not per position). Columns: <c>Deal,
/// Order, Time, Action, Volume, Symbol, Price, Commission, Swap, Profit,
/// Position ID</c>. The parser groups deals by Position ID and emits one
/// row per position, with <c>EntryPrice</c> from the entry deal and
/// <c>ExitPrice</c> from the exit deal.
/// </para>
///
/// <para>
/// Prompt-injection defense: the optional MT4 <c>Comment</c> column is
/// never propagated into <see cref="ImportRow.Notes"/>. If Notes ever feeds
/// an LLM, the parser keeps a clean separation between structured trade
/// data and freeform user text.
/// </para>
/// </summary>
public sealed class Mt4ImportRowParser : IImportRowParser
{
    /// <summary>MT4 signature tokens — header recognition for the 5a.1 spec scenario.</summary>
    private static readonly string[] RecognizedMt4Tokens =
    {
        "ticket", "open time", "type", "volume", "symbol", "open price",
        "sl", "tp", "close time", "close price", "profit"
    };

    /// <summary>MT5 signature tokens — single parser handles both formats.</summary>
    private static readonly string[] RecognizedMt5Tokens =
    {
        "deal", "order", "time", "action", "volume", "symbol",
        "price", "profit", "position id"
    };

    /// <summary>
    /// MT4 distinguishing markers — columns that are unique to MT4 exports
    /// and absent from generic CSV. If the header is missing both, the
    /// parser MUST NOT claim it (otherwise it would steal generic CSV files).
    /// </summary>
    private static readonly string[] Mt4DistinguishingMarkers = { "sl", "tp" };

    /// <summary>
    /// MT5 distinguishing marker — the <c>Position ID</c> column is unique
    /// to MT5 deals exports. Without it, the parser MUST NOT claim MT5.
    /// </summary>
    private static readonly string[] Mt5DistinguishingMarkers = { "position id" };

    public ImportFormat Format => ImportFormat.Mt4;

    public double CanParse(string fileName, Stream head)
    {
        if (head is null || !head.CanSeek) return 0.0;

        var originalPos = head.Position;
        var sniffLen = (int)Math.Min(head.Length - head.Position, 1024);
        if (sniffLen <= 0)
        {
            head.Position = originalPos;
            return 0.0;
        }

        var buffer = new byte[sniffLen];
        var read = head.Read(buffer, 0, sniffLen);
        head.Position = originalPos;  // restore — caller may re-read
        if (read <= 0) return 0.0;

        // Strip UTF-8 BOM if present.
        var offset = 0;
        if (read >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            offset = 3;

        var headerText = System.Text.Encoding.UTF8.GetString(buffer, offset, read - offset);
        var firstLine = headerText.Split(new[] { '\n', '\r' }, 2)[0].Trim();
        if (string.IsNullOrWhiteSpace(firstLine)) return 0.0;

        var tokenSet = new HashSet<string>(
            TokenizeFirstLine(firstLine),
            StringComparer.OrdinalIgnoreCase);

        return Math.Max(Mt4Score(tokenSet), Mt5Score(tokenSet));
    }

    private static double Mt4Score(HashSet<string> tokenSet)
    {
        // MT4 requires both SL and TP — these columns are absent from generic
        // CSV exports, so their presence is a strong distinguishing marker.
        var hasMarkers = Mt4DistinguishingMarkers.All(m => tokenSet.Contains(m));
        if (!hasMarkers) return 0.0;

        var matched = RecognizedMt4Tokens.Count(t => tokenSet.Contains(t));
        return (double)matched / RecognizedMt4Tokens.Length;
    }

    private static double Mt5Score(HashSet<string> tokenSet)
    {
        // MT5 deals exports always have a Position ID column — without it,
        // the parser MUST NOT claim MT5.
        var hasMarker = Mt5DistinguishingMarkers.All(m => tokenSet.Contains(m));
        if (!hasMarker) return 0.0;

        var matched = RecognizedMt5Tokens.Count(t => tokenSet.Contains(t));
        return (double)matched / RecognizedMt5Tokens.Length;
    }

    public async IAsyncEnumerable<ImportRow> ParseAsync(
        Stream body,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Strip UTF-8 BOM (TextFieldParser does not strip it itself, leading
        // to an "ï»¿Ticket" first column).
        Stream reader = body;
        if (body.CanSeek)
        {
            var firstByte = body.ReadByte();
            if (firstByte == 0xEF)
            {
                var b2 = body.ReadByte();
                var b3 = body.ReadByte();
                if (b2 != 0xBB || b3 != 0xBF)
                    body.Position = 0;
            }
            else
            {
                body.Position = 0;
            }
        }

        // MT4/MT5 exports typically use TAB delimiter; comma is a fallback for
        // hand-rolled files. TextFieldParser accepts both.
        using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(reader)
        {
            TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            Delimiters = new[] { "\t", "," },
            TrimWhiteSpace = false,
        };

        var headers = await ReadNextRecordAsync(parser, ct);
        if (headers is null) yield break;

        var headerMap = BuildHeaderMap(headers);
        var isMt5Mode = headerMap.ContainsKey("position id");

        if (isMt5Mode)
        {
            // Buffer all deals, then aggregate by Position ID. MT5 deals files
            // are typically small (hundreds to thousands of deals per year of
            // trading), so a buffered aggregation is acceptable here.
            var deals = new List<Mt5Deal>();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var fields = await ReadNextRecordAsync(parser, ct);
                if (fields is null) break;
                try
                {
                    var deal = MapMt5Deal(fields, headerMap, lineNumber: (int)parser.LineNumber - 1);
                    if (deal is not null) deals.Add(deal);
                }
                catch
                {
                    // Malformed deal — skip silently, stream continues.
                }
            }

            foreach (var row in AggregateDealsByPosition(deals))
            {
                ct.ThrowIfCancellationRequested();
                yield return row;
            }
        }
        else
        {
            // MT4 mode — one row per data row.
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var fields = await ReadNextRecordAsync(parser, ct);
                if (fields is null) yield break;
                ImportRow? row = null;
                try
                {
                    row = MapMt4Row(fields, headerMap, lineNumber: (int)parser.LineNumber - 1);
                }
                catch
                {
                    // Malformed row — skip silently.
                }
                if (row is not null) yield return row;
            }
        }
    }

    // =======================================================================
    // Tokenizer
    // =======================================================================

    private static IEnumerable<string> TokenizeFirstLine(string firstLine)
    {
        // Tab-delimited (MT4/MT5 standard) or comma-delimited (hand-rolled).
        var delim = firstLine.Contains('\t') ? '\t' : ',';
        return firstLine.Split(delim).Select(t => t.Trim().Trim('"'));
    }

    private static async Task<string[]?> ReadNextRecordAsync(
        Microsoft.VisualBasic.FileIO.TextFieldParser parser, CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        try
        {
            if (parser.EndOfData) return null;
            return parser.ReadFields();
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, int> BuildHeaderMap(string[] headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++)
        {
            var key = headers[i].Trim().Trim('"').ToLowerInvariant();
            if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key))
                map[key] = i;
        }
        return map;
    }

    private static string? GetField(string[] fields, Dictionary<string, int> headerMap, string column)
    {
        if (!headerMap.TryGetValue(column, out var idx)) return null;
        if (idx >= fields.Length) return null;
        var raw = fields[idx];
        return raw?.Trim().Trim('"');
    }

    // =======================================================================
    // MT4 row mapping
    // =======================================================================

    private static ImportRow? MapMt4Row(string[] fields, Dictionary<string, int> headerMap, int lineNumber)
    {
        var ticket = GetField(fields, headerMap, "ticket");
        var symbol = GetField(fields, headerMap, "symbol") ?? throw new FormatException("symbol required");
        var openTimeStr = GetField(fields, headerMap, "open time") ?? throw new FormatException("open time required");
        var typeStr = GetField(fields, headerMap, "type") ?? "";
        var volumeStr = GetField(fields, headerMap, "volume") ?? throw new FormatException("volume required");
        var openPriceStr = GetField(fields, headerMap, "open price") ?? throw new FormatException("open price required");
        var closePriceStr = GetField(fields, headerMap, "close price");
        var closeTimeStr = GetField(fields, headerMap, "close time");
        var profitStr = GetField(fields, headerMap, "profit");

        var direction = typeStr.ToLowerInvariant() switch
        {
            "buy" or "long" => ImportDirection.Long,
            "sell" or "short" => ImportDirection.Short,
            _ => ImportDirection.Long,  // best-effort default
        };

        if (!TryParseDecimal(volumeStr, out var volume))
            throw new FormatException("invalid volume");
        if (!TryParseDecimal(openPriceStr, out var entryPrice))
            throw new FormatException("invalid open price");

        decimal? exitPrice = null;
        DateTimeOffset? closedAt = null;
        if (!string.IsNullOrEmpty(closePriceStr) && TryParseDecimal(closePriceStr, out var ep))
            exitPrice = ep;
        if (!string.IsNullOrEmpty(closeTimeStr) && TryParseMtDate(closeTimeStr, out var closedAtRaw))
            closedAt = closedAtRaw;

        var status = exitPrice.HasValue && closedAt.HasValue
            ? ImportRowStatus.Closed
            : ImportRowStatus.Open;

        decimal? pnl = null;
        if (status == ImportRowStatus.Closed
            && !string.IsNullOrEmpty(profitStr)
            && TryParseDecimal(profitStr, out var p))
        {
            pnl = p;
        }

        if (!TryParseMtDate(openTimeStr, out var openedAt))
            throw new FormatException("invalid open time");

        return new ImportRow(
            LineNumber: lineNumber,
            TicketId: ticket,
            Symbol: symbol,
            Volume: Math.Abs(volume),  // MT4/MT5 may export negative volumes for closing deals
            VolumeCurrency: "USD",
            OpenedAt: openedAt,
            ClosedAt: closedAt,
            EntryPrice: entryPrice,
            ExitPrice: exitPrice,
            PnlAmount: pnl,
            PnlCurrency: "USD",
            Direction: direction,
            Status: status,
            Notes: null);  // Comment column intentionally dropped — prompt-injection defense
    }

    // =======================================================================
    // MT5 deal mapping + aggregation
    // =======================================================================

    private sealed record Mt5Deal(
        int LineNumber,
        string DealId,
        string PositionId,
        string Action,
        decimal Volume,
        string Symbol,
        decimal Price,
        DateTimeOffset Time,
        decimal Commission,
        decimal Swap,
        decimal Profit);

    private static Mt5Deal? MapMt5Deal(string[] fields, Dictionary<string, int> headerMap, int lineNumber)
    {
        var dealId = GetField(fields, headerMap, "deal") ?? "";
        var positionId = GetField(fields, headerMap, "position id") ?? "";
        var actionStr = GetField(fields, headerMap, "action") ?? "";
        var volumeStr = GetField(fields, headerMap, "volume") ?? throw new FormatException("volume required");
        var symbol = GetField(fields, headerMap, "symbol") ?? throw new FormatException("symbol required");
        var priceStr = GetField(fields, headerMap, "price") ?? throw new FormatException("price required");
        var timeStr = GetField(fields, headerMap, "time") ?? throw new FormatException("time required");
        var commissionStr = GetField(fields, headerMap, "commission") ?? "0";
        var swapStr = GetField(fields, headerMap, "swap") ?? "0";
        var profitStr = GetField(fields, headerMap, "profit") ?? "0";

        if (string.IsNullOrEmpty(positionId)) return null;  // can't aggregate without position id

        if (!TryParseDecimal(volumeStr, out var volume)) throw new FormatException("invalid volume");
        if (!TryParseDecimal(priceStr, out var price)) throw new FormatException("invalid price");
        if (!TryParseMtDate(timeStr, out var time)) throw new FormatException("invalid time");
        TryParseDecimal(commissionStr, out var commission);
        TryParseDecimal(swapStr, out var swap);
        TryParseDecimal(profitStr, out var profit);

        return new Mt5Deal(
            LineNumber: lineNumber,
            DealId: dealId,
            PositionId: positionId,
            Action: actionStr,
            Volume: volume,
            Symbol: symbol,
            Price: price,
            Time: time,
            Commission: commission,
            Swap: swap,
            Profit: profit);
    }

    private static IEnumerable<ImportRow> AggregateDealsByPosition(IReadOnlyList<Mt5Deal> deals)
    {
        // Group by Position ID — preserves input order of the first deal per group.
        var grouped = new Dictionary<string, List<Mt5Deal>>(StringComparer.Ordinal);
        foreach (var d in deals)
        {
            if (!grouped.TryGetValue(d.PositionId, out var list))
            {
                list = new List<Mt5Deal>();
                grouped[d.PositionId] = list;
            }
            list.Add(d);
        }

        foreach (var (positionId, list) in grouped)
        {
            if (list.Count == 0) continue;
            // The first deal chronologically is the entry; the last is the exit.
            // Standard MT5 deals exports list deals in chronological order.
            var first = list[0];
            var last = list.Count == 1 ? list[0] : list[^1];

            var direction = first.Action.ToLowerInvariant() switch
            {
                "buy" => ImportDirection.Long,
                "sell" => ImportDirection.Short,
                _ => ImportDirection.Long,
            };

            var volume = Math.Abs(first.Volume);  // entry volume (positive)
            var exitPrice = list.Count > 1 ? (decimal?)last.Price : null;
            var closedAt = list.Count > 1 ? (DateTimeOffset?)last.Time : null;
            var status = list.Count > 1 ? ImportRowStatus.Closed : ImportRowStatus.Open;

            // PnL = sum of Profit across all deals (entry deals have Profit=0; exit
            // deals carry the realized gain). Commission + Swap are excluded to match
            // the MT4 "Profit" column semantics — those columns are already netted
            // into the deal profit on MT5 export.
            decimal? pnl = null;
            if (status == ImportRowStatus.Closed)
            {
                var total = 0m;
                foreach (var d in list) total += d.Profit;
                pnl = total;
            }

            yield return new ImportRow(
                LineNumber: first.LineNumber,
                TicketId: positionId,
                Symbol: first.Symbol,
                Volume: volume,
                VolumeCurrency: "USD",
                OpenedAt: first.Time,
                ClosedAt: closedAt,
                EntryPrice: first.Price,
                ExitPrice: exitPrice,
                PnlAmount: pnl,
                PnlCurrency: "USD",
                Direction: direction,
                Status: status,
                Notes: null);
        }
    }

    // =======================================================================
    // Parsing helpers (decimal + date)
    // =======================================================================

    private static bool TryParseDecimal(string raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;
        return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static bool TryParseMtDate(string raw, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        // MT4/MT5 use yyyy.MM.dd (with dots) as the standard separator.
        // Also accept ISO-ish formats for hand-rolled exports. Naive times are
        // treated as UTC per the spec requirement.
        var formats = new[]
        {
            "yyyy.MM.dd HH:mm:ss",
            "yyyy.MM.dd HH:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy/MM/dd HH:mm:ss",
            "yyyy/MM/dd HH:mm",
            "yyyy.MM.ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss",
        };

        foreach (var fmt in formats)
        {
            if (DateTime.TryParseExact(raw, fmt, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                value = new DateTimeOffset(dt, TimeSpan.Zero);
                return true;
            }
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt2))
        {
            value = new DateTimeOffset(dt2, TimeSpan.Zero);
            return true;
        }

        return false;
    }
}
