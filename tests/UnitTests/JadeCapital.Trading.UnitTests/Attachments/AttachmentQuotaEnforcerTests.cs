using JadeCapital.Identity.Contracts.Projections;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Attachments;

namespace JadeCapital.Trading.UnitTests.Attachments;

/// <summary>
/// Unit tests for <c>AttachmentQuotaEnforcer</c> — slice 4d (Wave 4).
///
/// Covers the spec scenarios from <c>specs/attachments/spec.md</c>:
/// <list type="number">
///   <item>Quota check passes when under the cap.</item>
///   <item>Total-size gate fires (returns 413 path).</item>
///   <item>Attachment-count gate fires.</item>
///   <item>Default quota is used when the user has no row.</item>
/// </list>
/// Plus cross-edge cases (zero usage, exact-match, etc.).
/// </summary>
public class AttachmentQuotaEnforcerTests
{
    private readonly IAttachmentQuotaReader _quotaReader = Substitute.For<IAttachmentQuotaReader>();
    private readonly ITradeAttachmentUsageRepository _usage = Substitute.For<ITradeAttachmentUsageRepository>();

    private AttachmentQuotaEnforcer CreateSut() => new(_quotaReader, _usage);

    private static UserAttachmentQuota Quota(Guid userId, long quotaBytes, long usedBytes)
        => new(userId, quotaBytes, usedBytes);

    [Fact]
    public async Task CheckAsync_UnderLimits_ReturnsAllowed()
    {
        var userId = Guid.NewGuid();
        _quotaReader.GetQuotaAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Quota(userId, quotaBytes: 52_428_800, usedBytes: 10_485_760));
        _usage.GetUsageAsync(userId, Arg.Any<CancellationToken>())
            .Returns((10_485_760L, 5));

        var result = await CreateSut().CheckAsync(userId, incomingBytes: 1_048_576, CancellationToken.None);

        result.Allowed.Should().BeTrue();
        result.QuotaBytes.Should().Be(52_428_800L);
        result.UsedBytes.Should().Be(10_485_760L);
        result.IncomingBytes.Should().Be(1_048_576L);
        result.AttachmentCount.Should().Be(5);
        result.Reason.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_TotalSizeExceedsQuota_ReturnsExceededWithBytesReason()
    {
        var userId = Guid.NewGuid();
        // User has 48 MB used, attempting to upload 5 MB -> 53 MB > 50 MB.
        _quotaReader.GetQuotaAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Quota(userId, quotaBytes: 52_428_800, usedBytes: 50_331_648));
        _usage.GetUsageAsync(userId, Arg.Any<CancellationToken>())
            .Returns((50_331_648L, 1));

        var result = await CreateSut().CheckAsync(userId, incomingBytes: 5_242_880, CancellationToken.None);

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Contain("exceeds quota");
    }

    [Fact]
    public async Task CheckAsync_AttachmentCountExceedsCap_ReturnsExceededWithCountReason()
    {
        var userId = Guid.NewGuid();
        // User has 100 attachments uploaded; one more would exceed.
        _quotaReader.GetQuotaAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Quota(userId, quotaBytes: 52_428_800, usedBytes: 100L));
        _usage.GetUsageAsync(userId, Arg.Any<CancellationToken>())
            .Returns((100L, 100));

        var result = await CreateSut().CheckAsync(userId, incomingBytes: 1024L, CancellationToken.None);

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Contain("count");
    }

    [Fact]
    public async Task CheckAsync_UserHasNoQuotaRow_FallsBackToDefault()
    {
        var userId = Guid.NewGuid();
        _quotaReader.GetQuotaAsync(userId, Arg.Any<CancellationToken>())
            .Returns((UserAttachmentQuota?)null);
        _usage.GetUsageAsync(userId, Arg.Any<CancellationToken>())
            .Returns((0L, 0));

        var result = await CreateSut().CheckAsync(userId, incomingBytes: 1024L, CancellationToken.None);

        result.Allowed.Should().BeTrue();
        result.QuotaBytes.Should().Be(AttachmentQuota.Default.MaxTotalBytes);
    }

    [Fact]
    public async Task CheckAsync_ExactMatchAtQuota_ReturnsAllowed()
    {
        // Edge case: used + incoming == quota exactly. Spec says "would
        // exceed" — boundary case at exactly the cap should pass.
        var userId = Guid.NewGuid();
        _quotaReader.GetQuotaAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Quota(userId, quotaBytes: 1024L, usedBytes: 0L));
        _usage.GetUsageAsync(userId, Arg.Any<CancellationToken>())
            .Returns((0L, 0));

        var result = await CreateSut().CheckAsync(userId, incomingBytes: 1024L, CancellationToken.None);

        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_OneByteOverQuota_ReturnsExceeded()
    {
        var userId = Guid.NewGuid();
        _quotaReader.GetQuotaAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Quota(userId, quotaBytes: 1024L, usedBytes: 0L));
        _usage.GetUsageAsync(userId, Arg.Any<CancellationToken>())
            .Returns((0L, 0));

        var result = await CreateSut().CheckAsync(userId, incomingBytes: 1025L, CancellationToken.None);

        result.Allowed.Should().BeFalse();
    }
}