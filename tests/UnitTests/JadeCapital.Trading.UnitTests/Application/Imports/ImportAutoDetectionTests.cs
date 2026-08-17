using System.Text;
using JadeCapital.Shared.Kernel.Imports;
using JadeCapital.Trading.Application.Features.Imports;
using JadeCapital.Trading.Infrastructure.Imports;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace JadeCapital.Trading.UnitTests.Application.Imports;

/// <summary>
/// Tests for the parser auto-detection logic introduced in slice 5a.2. The
/// dispatcher (pure static helper <c>ImportParserDispatcher.SelectParser</c>)
/// iterates the registered parsers and picks the first one whose
/// <see cref="IImportRowParser.CanParse"/> returns &gt;= 0.8.
///
/// <para>
/// Mirrors the auto-detection scenarios from importers/spec.md:
/// unknown format → no parser wins, ambiguous → first wins by registration order.
/// </para>
/// </summary>
public class ImportAutoDetectionTests
{
    private static MemoryStream Stream(string csv) => new(Encoding.UTF8.GetBytes(csv));

    [Fact]
    public void SelectParser_MT4_File_Returns_Mt4_Parser()
    {
        var mt4Header = "Ticket\tOpen Time\tType\tVolume\tSymbol\tOpen Price\tSL\tTP\tClose Time\tClose Price\tCommission\tSwap\tProfit";
        var csv = new Mt4ImportRowParser();
        var parsers = new IImportRowParser[] { csv };

        using var s = Stream(mt4Header + "\n");
        var selected = ImportParserDispatcher.SelectParser(parsers, "mt4-trades.csv", s);

        selected.Should().BeSameAs(csv);
    }

    [Fact]
    public void SelectParser_MT5_File_Returns_Mt4_Parser_Since_Single_Impl_Handles_Both()
    {
        var mt5Header = "Deal\tOrder\tTime\tAction\tVolume\tSymbol\tPrice\tCommission\tSwap\tProfit\tPosition ID";
        var mt4 = new Mt4ImportRowParser();
        var parsers = new IImportRowParser[] { mt4 };

        using var s = Stream(mt5Header + "\n");
        var selected = ImportParserDispatcher.SelectParser(parsers, "mt5-deals.csv", s);

        selected.Should().BeSameAs(mt4);
    }

    [Fact]
    public void SelectParser_CSV_File_Returns_Csv_Parser()
    {
        var csvHeader = "Ticket,Symbol,Open Time,Type,Volume,Open Price,Close Price,Close Time,Commission,Swap,Profit";
        var csv = new CsvImportRowParser();
        var parsers = new IImportRowParser[] { csv };

        using var s = Stream(csvHeader + "\n");
        var selected = ImportParserDispatcher.SelectParser(parsers, "trades.csv", s);

        selected.Should().BeSameAs(csv);
    }

    [Fact]
    public void SelectParser_Unknown_Format_Returns_Null()
    {
        // JSON input — neither MT4 nor CSV header signature.
        var jsonHeader = "{ \"trades\": [] }";
        var mt4 = new Mt4ImportRowParser();
        var csv = new CsvImportRowParser();
        var parsers = new IImportRowParser[] { mt4, csv };

        using var s = Stream(jsonHeader);
        var selected = ImportParserDispatcher.SelectParser(parsers, "trades.json", s);

        selected.Should().BeNull();
    }

    [Fact]
    public void SelectParser_Ambiguous_First_Registered_Wins()
    {
        // Both parsers would score high on this header (it contains both
        // "ticket" and tab separators). The first registered MUST win.
        // This protects the DI registration order convention:
        // MT4 first, CSV fallback (per spec §"5a.2 Phase 2.2").
        var ambiguousHeader = "Ticket\tSymbol\tOpen Time\tType\tVolume\tOpen Price\tClose Price\tClose Time\tCommission\tSwap\tProfit";
        var mt4 = Substitute.For<IImportRowParser>();
        mt4.CanParse(Arg.Any<string>(), Arg.Any<Stream>()).Returns(0.9);
        var csv = Substitute.For<IImportRowParser>();
        csv.CanParse(Arg.Any<string>(), Arg.Any<Stream>()).Returns(0.9);
        var parsers = new IImportRowParser[] { mt4, csv };

        using var s = Stream(ambiguousHeader + "\n");
        var selected = ImportParserDispatcher.SelectParser(parsers, "ambiguous.csv", s);

        selected.Should().BeSameAs(mt4);
        // Verify the second parser was never queried (first short-circuits).
        csv.DidNotReceive().CanParse(Arg.Any<string>(), Arg.Any<Stream>());
    }

    [Fact]
    public void SelectParser_Score_Below_Threshold_Is_Skipped()
    {
        var csvHeader = "Ticket,Symbol,Open Time,Type,Volume,Open Price,Close Price,Close Time,Commission,Swap,Profit";
        var weak = Substitute.For<IImportRowParser>();
        weak.CanParse(Arg.Any<string>(), Arg.Any<Stream>()).Returns(0.5);  // below 0.8
        var csv = new CsvImportRowParser();
        var parsers = new IImportRowParser[] { weak, csv };

        using var s = Stream(csvHeader + "\n");
        var selected = ImportParserDispatcher.SelectParser(parsers, "trades.csv", s);

        selected.Should().BeSameAs(csv);
    }
}
