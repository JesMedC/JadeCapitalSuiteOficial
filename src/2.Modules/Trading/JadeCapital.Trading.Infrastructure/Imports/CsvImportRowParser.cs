using System.Globalization;
using System.Runtime.CompilerServices;
using JadeCapital.Shared.Kernel.Imports;

namespace JadeCapital.Trading.Infrastructure.Imports;

/// <summary>
/// CSV row parser for the importer pipeline (slice 5a.1, Wave 5).
///
/// <para>
/// Uses <c>Microsoft.VisualBasic.FileIO.TextFieldParser</c> for robust CSV
/// tokenization — handles UTF-8 BOM, quoted fields with embedded commas,
/// escaped quotes (<c>""</c>), CRLF / LF line endings, and blank lines.
/// The VisualBasic dependency is intentional; Wave 5 accepts the odd-looking
/// reference for the parsing robustness.
/// </para>
///
/// <para>
/// Row mapping: tolerant — missing optional columns (e.g. <c>Close Price</c>
/// for an open trade) produce null <see cref="ImportRow.ExitPrice"/>. Rows
/// that fail domain validation (e.g. unparseable <c>Volume</c>) are skipped
/// silently — the streaming pipeline continues.
/// </para>
/// </summary>
public sealed class CsvImportRowParser : IImportRowParser
{
    /// <summary>Header tokens that signal a recognizable CSV format.</summary>
    private static readonly string[] RecognizedTokens =
    {
        "ticket", "symbol", "open time", "type", "volume", "open price"
    };

    public ImportFormat Format => ImportFormat.Csv;

    public double CanParse(string fileName, Stream head)
    {
        if (head is null) return 0.0;
        if (!head.CanSeek) return 0.0;

        // Sniff up to first KiB (or entire stream if smaller).
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

        // Skip UTF-8 BOM if present.
        var offset = 0;
        if (read >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            offset = 3;

        var headerText = System.Text.Encoding.UTF8.GetString(buffer, offset, read - offset);
        var firstLine = headerText.Split('\n', 2)[0].TrimEnd('\r');

        if (string.IsNullOrWhiteSpace(firstLine)) return 0.0;

        var firstLineTokens = firstLine.Split(',')
            .Select(t => t.Trim().Trim('"').ToLowerInvariant())
            .ToArray();

        // Score: fraction of recognized tokens found in the header.
        var matched = firstLineTokens.Count(t => RecognizedTokens.Contains(t));
        return (double)matched / RecognizedTokens.Length;
    }

    public async IAsyncEnumerable<ImportRow> ParseAsync(
        Stream body,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Wrap the body in a reader that strips UTF-8 BOM (TextFieldParser
        // doesn't strip the BOM itself, leading to an "ï»¿Ticket" first column).
        Stream reader = body;
        if (body.CanSeek)
        {
            var firstByte = body.ReadByte();
            body.Position = Math.Max(0, body.Position - 1);
            if (firstByte != 0xEF)  // not BOM prefix; rewind
            {
                if (body.Position > 0)
                    body.Position = 0;
            }
        }

        // Use TextFieldParser for robust CSV. Encoding defaults to UTF-8.
        using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(reader)
        {
            TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            Delimiters = new[] { "," },
            CommentTokens = new[] { "#" },
            TrimWhiteSpace = false,  // preserve raw tokens; we trim explicitly
        };

        // First row is the header.
        var headers = await ReadNextRecordAsync(parser, ct);
        if (headers is null) yield break;
        var headerMap = BuildHeaderMap(headers);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var fields = await ReadNextRecordAsync(parser, ct);
            if (fields is null) yield break;
            ImportRow? row = null;
            try
            {
                row = MapRow(fields, headerMap, (int)parser.LineNumber - 1);  // line -1: header was line 1
            }
            catch
            {
                // Malformed row — skip silently, stream continues.
                continue;
            }
            if (row is not null) yield return row;
        }
    }

    private static async Task<string[]?> ReadNextRecordAsync(
        Microsoft.VisualBasic.FileIO.TextFieldParser parser, CancellationToken ct)
    {
        // TextFieldParser.ReadFields is sync but light; wrapping in Task.Yield
        // gives cooperative cancellation between rows.
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

    private static ImportRow? MapRow(string[] fields, Dictionary<string, int> headerMap, int lineNumber)
    {
        string? Get(string column)
            => headerMap.TryGetValue(column, out var idx) && idx < fields.Length
                ? fields[idx].Trim().Trim('"')
                : null;

        var ticket = Get("ticket");
        var symbol = Get("symbol") ?? throw new FormatException("symbol required");
        var openTimeStr = Get("open time") ?? throw new FormatException("open time required");
        var type = Get("type") ?? "";
        var volumeStr = Get("volume") ?? throw new FormatException("volume required");
        var openPriceStr = Get("open price") ?? throw new FormatException("open price required");
        var closePriceStr = Get("close price");
        var closeTimeStr = Get("close time");
        var profitStr = Get("profit");

        var direction = type.ToLowerInvariant() switch
        {
            "buy" or "long" => ImportDirection.Long,
            "sell" or "short" => ImportDirection.Short,
            _ => ImportDirection.Long,  // best-effort default for unknown types
        };

        if (!TryParseDecimal(volumeStr, out var volume)) throw new FormatException("invalid volume");
        if (!TryParseDecimal(openPriceStr, out var entryPrice)) throw new FormatException("invalid open price");

        decimal? exitPrice = null;
        DateTimeOffset? closedAt = null;
        if (!string.IsNullOrEmpty(closePriceStr) && TryParseDecimal(closePriceStr, out var ep))
            exitPrice = ep;
        if (!string.IsNullOrEmpty(closeTimeStr) && TryParseDate(closeTimeStr, out var ct2))
            closedAt = ct2;

        var status = exitPrice.HasValue && closedAt.HasValue
            ? ImportRowStatus.Closed
            : ImportRowStatus.Open;

        // For Open trades, PnL is null (no realized gain/loss yet).
        decimal? pnl = null;
        if (status == ImportRowStatus.Closed
            && !string.IsNullOrEmpty(profitStr)
            && TryParseDecimal(profitStr, out var p))
        {
            pnl = p;
        }

        if (!TryParseDate(openTimeStr, out var openedAt)) throw new FormatException("invalid open time");

        return new ImportRow(
            LineNumber: lineNumber,
            TicketId: ticket,
            Symbol: symbol,
            Volume: volume,
            VolumeCurrency: "USD",
            OpenedAt: openedAt,
            ClosedAt: closedAt,
            EntryPrice: entryPrice,
            ExitPrice: exitPrice,
            PnlAmount: pnl,
            PnlCurrency: "USD",
            Direction: direction,
            Status: status,
            Notes: null);
    }

    private static bool TryParseDecimal(string raw, out decimal value)
    {
        // CSV exports use either invariant (1.0850) or culture (1,0850) decimals.
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;

        // Try with the current culture's decimal separator as a fallback.
        return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static bool TryParseDate(string raw, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        // CSV exports commonly use ISO-ish formats: "2026-08-19 14:00", "2026-08-19 14:00:00".
        // Treat naive times as UTC (per spec requirement).
        var formats = new[]
        {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy/MM/dd HH:mm:ss",
            "yyyy/MM/dd HH:mm",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ssZ",
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

        // Last resort: generic parse with AssumeUniversal.
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt2))
        {
            value = new DateTimeOffset(dt2, TimeSpan.Zero);
            return true;
        }

        return false;
    }
}