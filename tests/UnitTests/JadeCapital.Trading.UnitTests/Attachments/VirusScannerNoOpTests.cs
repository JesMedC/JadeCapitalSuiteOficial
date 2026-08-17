using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Trading.Infrastructure.Storage;

namespace JadeCapital.Trading.UnitTests.Attachments;

/// <summary>
/// Unit tests for the default <c>VirusScannerNoOp</c> — slice 4d (Wave 4).
///
/// The no-op always returns <see cref="VirusScanResult.Clean"/>. Wave 6
/// swaps in a real ClamAV impl via DI; tests don't need to cover ClamAV.
/// </summary>
public class VirusScannerNoOpTests
{
    [Fact]
    public async Task ScanAsync_AnyInput_ReturnsClean()
    {
        var scanner = new VirusScannerNoOp();
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        var result = await scanner.ScanAsync(stream, mimeType: "image/png");

        result.Should().Be(VirusScanResult.Clean);
    }

    [Fact]
    public async Task ScanAsync_EmptyStream_StillReturnsClean()
    {
        var scanner = new VirusScannerNoOp();
        var stream = new MemoryStream(Array.Empty<byte>());

        var result = await scanner.ScanAsync(stream, mimeType: "application/pdf");

        result.Should().Be(VirusScanResult.Clean);
    }

    [Fact]
    public async Task ScanAsync_CancellationRequested_StillReturnsClean()
    {
        // No-op ignores the cancellation token — it's a stub that does no work.
        var scanner = new VirusScannerNoOp();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var stream = new MemoryStream(new byte[] { 0xFF, 0xD8 });

        var result = await scanner.ScanAsync(stream, mimeType: "image/jpeg", ct: cts.Token);

        result.Should().Be(VirusScanResult.Clean);
    }
}