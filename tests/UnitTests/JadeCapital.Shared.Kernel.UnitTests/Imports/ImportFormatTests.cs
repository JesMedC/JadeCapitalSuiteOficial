using JadeCapital.Shared.Kernel.Imports;
using FluentAssertions;
using Xunit;

namespace JadeCapital.Shared.Kernel.UnitTests.Imports;

/// <summary>
/// Tests for <see cref="ImportFormat"/> enum wire shape. Mirrors the
/// CHECK constraint on trading.import_jobs.format (format IN (0, 1, 2, 255))
/// declared in migration 0019_import_jobs.sql.
/// </summary>
public class ImportFormatTests
{
    [Fact]
    public void Enum_Has_Csv_As_Zero()
    {
        ((byte)ImportFormat.Csv).Should().Be(0);
    }

    [Fact]
    public void Enum_Has_Mt4_And_Mt5_As_Successive()
    {
        ((byte)ImportFormat.Mt4).Should().Be(1);
        ((byte)ImportFormat.Mt5).Should().Be(2);
    }

    [Fact]
    public void Unknown_Has_Sentinel_255()
    {
        ((byte)ImportFormat.Unknown).Should().Be(255);
    }
}