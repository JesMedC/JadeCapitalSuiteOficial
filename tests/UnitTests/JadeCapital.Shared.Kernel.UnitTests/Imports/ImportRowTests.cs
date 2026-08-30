using JadeCapital.Shared.Kernel.Imports;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace JadeCapital.Shared.Kernel.UnitTests.Imports;

/// <summary>
/// Tests for <see cref="ImportRow"/> record. Mirrors the wire shape that
/// <see cref="IImportRowParser.ParseAsync"/> emits and the streaming pipeline
/// consumes. Decimal-only money + ISO-4217-like currency codes (mirrors the
/// trade domain convention).
/// </summary>
public class ImportRowTests
{
    private static ImportRow Sample() => new(
        LineNumber: 2,
        TicketId: "12345",
        Symbol: "EURUSD",
        Volume: 1.0m,
        VolumeCurrency: "USD",
        OpenedAt: new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero),
        ClosedAt: new DateTimeOffset(2026, 8, 19, 16, 0, 0, TimeSpan.Zero),
        EntryPrice: 1.0850m,
        ExitPrice: 1.0870m,
        PnlAmount: 20m,
        PnlCurrency: "USD",
        Direction: ImportDirection.Long,
        Status: ImportRowStatus.Closed,
        Notes: null);

    [Fact]
    public void Direction_Enum_Has_Long_And_Short()
    {
        ((byte)ImportDirection.Long).Should().Be(1);
        ((byte)ImportDirection.Short).Should().Be(2);
    }

    [Fact]
    public void RowStatus_Enum_Has_Open_And_Closed()
    {
        ((byte)ImportRowStatus.Open).Should().Be(1);
        ((byte)ImportRowStatus.Closed).Should().Be(2);
    }

    [Fact]
    public void Defaults_Nullable_Money_Fields_To_Null()
    {
        var row = new ImportRow(
            LineNumber: 5,
            TicketId: null,
            Symbol: "EURUSD",
            Volume: 1.0m,
            VolumeCurrency: "USD",
            OpenedAt: new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero),
            ClosedAt: null,
            EntryPrice: 1.0850m,
            ExitPrice: null,
            PnlAmount: null,
            PnlCurrency: "USD",
            Direction: ImportDirection.Long,
            Status: ImportRowStatus.Open,
            Notes: null);

        row.ClosedAt.Should().BeNull();
        row.ExitPrice.Should().BeNull();
        row.PnlAmount.Should().BeNull();
        row.Status.Should().Be(ImportRowStatus.Open);
    }

    [Fact]
    public void LineNumber_Defaults_Start_At_2_After_Header()
    {
        // Convention: line 1 is the CSV header, data rows start at line 2.
        Sample().LineNumber.Should().Be(2);
    }

    [Fact]
    public void Json_Contract_Stable_With_CamelCase()
    {
#pragma warning disable CA1869
        var row = Sample();
        var json = JsonSerializer.Serialize(row, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
#pragma warning restore CA1869

        json.Should().Contain("\"lineNumber\":2");
        json.Should().Contain("\"ticketId\":\"12345\"");
        json.Should().Contain("\"symbol\":\"EURUSD\"");
        json.Should().Contain("\"volume\":1.0");
        json.Should().Contain("\"direction\":1");
        json.Should().Contain("\"status\":2");
    }
}