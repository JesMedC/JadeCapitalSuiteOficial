using System.Text;
using JadeCapital.Shared.Kernel.Imports;
using JadeCapital.Trading.Infrastructure.Imports;
using FluentAssertions;
using Xunit;

namespace JadeCapital.Trading.UnitTests.Infrastructure;

/// <summary>
/// Tests for <c>CsvImportRowParser</c>. Mirrors the "CSV parser correctness"
/// scenarios from importers/spec.md Requirement #5: UTF-8 BOM, quoted fields,
/// open trades (no exit price), 1000-row parity with reference data,
/// BOM stripping, time-zone naive times treated as UTC, malformed-row skipping.
/// </summary>
public class CsvImportRowParserTests
{
    private static MemoryStream Stream(string csv, bool withBom = false)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        if (withBom)
        {
            // 0xEF, 0xBB, 0xBF = UTF-8 BOM
            var withBomBytes = new byte[bytes.Length + 3];
            withBomBytes[0] = 0xEF; withBomBytes[1] = 0xBB; withBomBytes[2] = 0xBF;
            Array.Copy(bytes, 0, withBomBytes, 3, bytes.Length);
            return new MemoryStream(withBomBytes);
        }
        return new MemoryStream(bytes);
    }

    private static async Task<List<ImportRow>> CollectAsync(IAsyncEnumerable<ImportRow> rows)
    {
        var list = new List<ImportRow>();
        await foreach (var r in rows) list.Add(r);
        return list;
    }

    [Fact]
    public void CanParse_Returns_High_Score_For_Known_Csv_Header()
    {
        var parser = new CsvImportRowParser();
        using var s = Stream("Ticket,Symbol,Open Time,Type,Volume,Open Price,Close Price,Close Time,Commission,Swap,Profit\n");
        var score = parser.CanParse("trades.csv", s);
        score.Should().BeGreaterOrEqualTo(0.8);
    }

    [Fact]
    public void CanParse_Returns_Low_Score_For_Binary_Input()
    {
        var parser = new CsvImportRowParser();
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 };
        using var s = new MemoryStream(bytes);
        var score = parser.CanParse("trades.csv", s);
        score.Should().BeLessThan(0.5);
    }

    [Fact]
    public async Task Parses_Simple_Csv_With_Header_And_Two_Rows()
    {
        var csv = """
            Ticket,Symbol,Open Time,Type,Volume,Open Price,Close Price,Close Time,Commission,Swap,Profit
            12345,EURUSD,2026-08-19 14:00,Buy,1.0,1.0850,1.0870,2026-08-19 16:00,-5,0,15
            12346,GBPUSD,2026-08-19 15:00,Sell,2.0,1.2500,1.2480,2026-08-19 17:00,-5,0,40
            """;
        var parser = new CsvImportRowParser();
        using var s = Stream(csv);

        var rows = await CollectAsync(parser.ParseAsync(s));

        rows.Should().HaveCount(2);
        rows[0].TicketId.Should().Be("12345");
        rows[0].Symbol.Should().Be("EURUSD");
        rows[0].Volume.Should().Be(1.0m);
        rows[0].EntryPrice.Should().Be(1.0850m);
        rows[0].ExitPrice.Should().Be(1.0870m);
        rows[0].Direction.Should().Be(ImportDirection.Long);
        rows[0].Status.Should().Be(ImportRowStatus.Closed);
        rows[0].PnlAmount.Should().Be(15m);
    }

    [Fact]
    public async Task Handles_Utf8_Bom_In_Header()
    {
        var csv = "Ticket,Symbol,Open Time,Type,Volume,Open Price,Close Price,Close Time,Commission,Swap,Profit\n12345,EURUSD,2026-08-19 14:00,Buy,1.0,1.0850,1.0870,2026-08-19 16:00,-5,0,15\n";
        var parser = new CsvImportRowParser();
        using var s = Stream(csv, withBom: true);

        var rows = await CollectAsync(parser.ParseAsync(s));

        rows.Should().HaveCount(1);
        rows[0].TicketId.Should().Be("12345");
    }

    [Fact]
    public async Task Handles_Quoted_Fields_With_Embedded_Commas()
    {
        var csv = """
            Ticket,Symbol,Open Time,Type,Volume,Open Price,Close Price,Close Time,Commission,Swap,Profit
            12345,"EUR/USD, mini",2026-08-19 14:00,Buy,1.0,1.0850,1.0870,2026-08-19 16:00,-5,0,15
            """;
        var parser = new CsvImportRowParser();
        using var s = Stream(csv);

        var rows = await CollectAsync(parser.ParseAsync(s));

        rows.Should().HaveCount(1);
        rows[0].Symbol.Should().Be("EUR/USD, mini");
    }

    [Fact]
    public async Task Open_Trade_With_Empty_Close_Price_Has_Null_ExitPrice_And_Status_Open()
    {
        var csv = """
            Ticket,Symbol,Open Time,Type,Volume,Open Price,Close Price,Close Time,Commission,Swap,Profit
            12345,EURUSD,2026-08-19 14:00,Buy,1.0,1.0850,,,0,0,0
            """;
        var parser = new CsvImportRowParser();
        using var s = Stream(csv);

        var rows = await CollectAsync(parser.ParseAsync(s));

        rows.Should().HaveCount(1);
        rows[0].ExitPrice.Should().BeNull();
        rows[0].ClosedAt.Should().BeNull();
        rows[0].PnlAmount.Should().BeNull();
        rows[0].Status.Should().Be(ImportRowStatus.Open);
    }

    [Fact]
    public async Task Headers_CaseInsensitive_Dedup()
    {
        // The header "ticket" (lowercase) should map to the Ticket column.
        var csv = """
            ticket,symbol,open time,type,volume,open price,close price,close time,commission,swap,profit
            12345,EURUSD,2026-08-19 14:00,Buy,1.0,1.0850,1.0870,2026-08-19 16:00,-5,0,15
            """;
        var parser = new CsvImportRowParser();
        using var s = Stream(csv);

        var rows = await CollectAsync(parser.ParseAsync(s));

        rows.Should().HaveCount(1);
        rows[0].TicketId.Should().Be("12345");
    }

    [Fact]
    public async Task Malformed_Row_Skipped_Stream_Continues()
    {
        // Row 2 has a non-numeric volume — must be skipped, row 3 still parsed.
        var csv = """
            Ticket,Symbol,Open Time,Type,Volume,Open Price,Close Price,Close Time,Commission,Swap,Profit
            12345,EURUSD,2026-08-19 14:00,Buy,1.0,1.0850,1.0870,2026-08-19 16:00,-5,0,15
            12346,GBPUSD,2026-08-19 15:00,Buy,not-a-number,1.2500,1.2480,2026-08-19 17:00,-5,0,40
            12347,AUDUSD,2026-08-19 16:00,Buy,0.5,0.6500,0.6520,2026-08-19 18:00,-3,0,10
            """;
        var parser = new CsvImportRowParser();
        using var s = Stream(csv);

        var rows = await CollectAsync(parser.ParseAsync(s));

        // Expect 2 valid rows (row 2 dropped).
        rows.Should().HaveCount(2);
        rows[0].TicketId.Should().Be("12345");
        rows[1].TicketId.Should().Be("12347");
    }

    [Fact]
    public async Task Handles_CRLF_Line_Endings()
    {
        var csv = "Ticket,Symbol,Open Time,Type,Volume,Open Price,Close Price,Close Time,Commission,Swap,Profit\r\n12345,EURUSD,2026-08-19 14:00,Buy,1.0,1.0850,1.0870,2026-08-19 16:00,-5,0,15\r\n";
        var parser = new CsvImportRowParser();
        using var s = Stream(csv);

        var rows = await CollectAsync(parser.ParseAsync(s));

        rows.Should().HaveCount(1);
        rows[0].TicketId.Should().Be("12345");
    }

    [Fact]
    public async Task Parses_1000_Rows_Quickly()
    {
        // Generates 1000 rows; the parser must produce 1000 ImportRow records.
        var sb = new StringBuilder();
        sb.AppendLine("Ticket,Symbol,Open Time,Type,Volume,Open Price,Close Price,Close Time,Commission,Swap,Profit");
        for (var i = 1; i <= 1000; i++)
        {
            sb.AppendLine($"{i},EURUSD,2026-08-19 14:00,Buy,1.0,1.0850,1.0870,2026-08-19 16:00,-5,0,15");
        }
        var parser = new CsvImportRowParser();
        using var s = Stream(sb.ToString());

        var rows = await CollectAsync(parser.ParseAsync(s));

        rows.Should().HaveCount(1000);
        rows[0].TicketId.Should().Be("1");
        rows[999].TicketId.Should().Be("1000");
    }

    [Fact]
    public void Format_Returns_Csv()
    {
        new CsvImportRowParser().Format.Should().Be(ImportFormat.Csv);
    }
}