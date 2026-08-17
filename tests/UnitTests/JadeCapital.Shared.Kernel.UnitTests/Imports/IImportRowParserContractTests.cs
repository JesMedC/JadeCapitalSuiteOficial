using JadeCapital.Shared.Kernel.Imports;
using FluentAssertions;
using Xunit;

namespace JadeCapital.Shared.Kernel.UnitTests.Imports;

/// <summary>
/// Tests for the <see cref="IImportRowParser"/> contract — the shape that
/// every parser (CsvImportRowParser in 5a.1, Mt4ImportRowParser in 5a.2)
/// must satisfy. Mirrors the "Parser contract" Requirement in the
/// importers spec.md.
/// </summary>
public class IImportRowParserContractTests
{
    [Fact]
    public void Interface_Exposes_Format_Property()
    {
        // Use a test double to assert the interface shape; reflection-based
        // verification of the interface is more brittle than a compile-time
        // check, but the assertion here documents the contract.
        IImportRowParser parser = new StubParser();
        parser.Format.Should().Be(ImportFormat.Csv);
    }

    [Fact]
    public void CanParse_Returns_Confidence_In_Range()
    {
        IImportRowParser parser = new StubParser();
        var score = parser.CanParse("trades.csv", Stream.Null);
        score.Should().BeInRange(0.0, 1.0);
    }

    [Fact]
    public void ParseAsync_Returns_IAsyncEnumerable_Of_ImportRow()
    {
        IImportRowParser parser = new StubParser();
        var enumerable = parser.ParseAsync(Stream.Null, CancellationToken.None);
        enumerable.Should().NotBeNull();
        enumerable.Should().BeAssignableTo<IAsyncEnumerable<ImportRow>>();
    }

    private sealed class StubParser : IImportRowParser
    {
        public ImportFormat Format => ImportFormat.Csv;

        public double CanParse(string fileName, Stream head) => 0.95;

        public async IAsyncEnumerable<ImportRow> ParseAsync(
            Stream body, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield break;
        }
    }
}