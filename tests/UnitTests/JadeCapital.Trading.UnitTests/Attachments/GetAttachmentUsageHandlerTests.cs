using JadeCapital.Identity.Contracts.Projections;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Attachments;

namespace JadeCapital.Trading.UnitTests.Attachments;

/// <summary>
/// Unit tests for <c>GetAttachmentUsageHandler</c> — slice 4d (Wave 4).
/// </summary>
public class GetAttachmentUsageHandlerTests
{
    private readonly IAttachmentQuotaReader _quotaReader = Substitute.For<IAttachmentQuotaReader>();
    private readonly ITradeAttachmentUsageRepository _usage = Substitute.For<ITradeAttachmentUsageRepository>();
    private readonly ILogger<GetAttachmentUsageHandler> _logger
        = Substitute.For<ILogger<GetAttachmentUsageHandler>>();

    private GetAttachmentUsageHandler CreateSut() => new(_quotaReader, _usage, _logger);

    [Fact]
    public async Task Handle_EmptyUser_ReturnsZeroPercentFull()
    {
        var userId = Guid.NewGuid();
        _quotaReader.GetQuotaAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAttachmentQuota(userId, QuotaBytes: 52_428_800, UsedBytes: 0));
        _usage.GetUsageAsync(userId, Arg.Any<CancellationToken>())
            .Returns((0L, 0));

        var result = await CreateSut().Handle(new GetAttachmentUsageQuery(userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalBytes.Should().Be(0L);
        result.Value.AttachmentCount.Should().Be(0);
        result.Value.QuotaBytes.Should().Be(52_428_800L);
        result.Value.PercentFull.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_MultiAttachment_ComputesPercentFullAndRoundsAwayFromZero()
    {
        var userId = Guid.NewGuid();
        _quotaReader.GetQuotaAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAttachmentQuota(userId, QuotaBytes: 52_428_800, UsedBytes: 0));
        _usage.GetUsageAsync(userId, Arg.Any<CancellationToken>())
            .Returns((8_500_000L, 12));

        var result = await CreateSut().Handle(new GetAttachmentUsageQuery(userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalBytes.Should().Be(8_500_000L);
        result.Value.AttachmentCount.Should().Be(12);
        // 8_500_000 / 52_428_800 ≈ 0.1621... → 16.21%
        result.Value.PercentFull.Should().Be(16.21m);
    }

    [Fact]
    public async Task Handle_NoQuotaRow_FallsBackToDefaultQuota()
    {
        var userId = Guid.NewGuid();
        _quotaReader.GetQuotaAsync(userId, Arg.Any<CancellationToken>())
            .Returns((UserAttachmentQuota?)null);
        _usage.GetUsageAsync(userId, Arg.Any<CancellationToken>())
            .Returns((1024L, 1));

        var result = await CreateSut().Handle(new GetAttachmentUsageQuery(userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.QuotaBytes.Should().Be(AttachmentQuota.Default.MaxTotalBytes);
        result.Value.QuotaCount.Should().Be(AttachmentQuota.Default.MaxAttachmentCount);
    }
}