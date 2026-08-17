using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Attachments;
using JadeCapital.Trading.Domain.TradeAttachments;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Trading.UnitTests.Attachments;

/// <summary>
/// Unit tests for <c>GetAttachmentThumbnailHandler</c> — slice 4d (Wave 4).
///
/// Scenarios:
/// <list type="number">
///   <item>Image attachment → 200 + presigned URL with width/height.</item>
///   <item>Non-image attachment → 415 with thumbnail_not_supported.</item>
///   <item>Cross-user attachment → 404 (review repo returns null).</item>
///   <item>Width/height clamping to [16, 1024].</item>
/// </list>
/// </summary>
public class GetAttachmentThumbnailHandlerTests
{
    private readonly ITradeReviewRepository _reviews = Substitute.For<ITradeReviewRepository>();
    private readonly IAttachmentThumbnailGenerator _generator = Substitute.For<IAttachmentThumbnailGenerator>();

    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 19, 14, 32, 0, TimeSpan.Zero);

    private GetAttachmentThumbnailHandler CreateSut() => new(_reviews, _generator, NullLogger<GetAttachmentThumbnailHandler>.Instance);

    private static TradeAttachment PendingImageAttachment(Guid userId)
    {
        return TradeAttachment.RequestSlot(
            id: Guid.NewGuid(),
            reviewId: Guid.NewGuid(),
            userId: userId,
            objectKey: "trading/attachments/u/t/r/a/img.png",
            contentType: "image/png",
            sizeBytes: 2048L,
            now: FixedNow).Value;
    }

    [Fact]
    public async Task Handle_ImageAttachment_ReturnsThumbnailUrlDto()
    {
        var att = PendingImageAttachment(Guid.NewGuid());
        _reviews.FindAttachmentByIdAsync(att.Id, att.UserId, Arg.Any<CancellationToken>())
            .Returns(att);
        _generator.GetThumbnailUrlAsync(att.ObjectKey, 200, 200, Arg.Any<CancellationToken>())
            .Returns("http://minio:9000/bucket/img.png?width=200&height=200&sig=abc");

        var result = await CreateSut().Handle(
            new GetAttachmentThumbnailQuery(att.Id, att.UserId, 200, 200), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AttachmentId.Should().Be(att.Id);
        result.Value.ContentType.Should().Be("image/png");
        result.Value.Width.Should().Be(200);
        result.Value.Height.Should().Be(200);
        result.Value.PresignedUrl.Should().Contain("width=200");
        result.Value.PresignedUrl.Should().Contain("height=200");
    }

    [Fact]
    public async Task Handle_NonImageAttachment_ReturnsThumbnailNotSupported()
    {
        var userId = Guid.NewGuid();
        var att = TradeAttachment.RequestSlot(
            id: Guid.NewGuid(),
            reviewId: Guid.NewGuid(),
            userId: userId,
            objectKey: "trading/attachments/u/t/r/a/research.pdf",
            contentType: "application/pdf",
            sizeBytes: 4096L,
            now: FixedNow).Value;
        _reviews.FindAttachmentByIdAsync(att.Id, userId, Arg.Any<CancellationToken>())
            .Returns(att);

        var result = await CreateSut().Handle(
            new GetAttachmentThumbnailQuery(att.Id, userId, 200, 200), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("failure.attachment.thumbnail_not_supported");
        await _generator.DidNotReceive().GetThumbnailUrlAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CrossUserAccess_ReturnsNotFound()
    {
        // Repo returns null when userId doesn't match (cross-user scope).
        _reviews.FindAttachmentByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ReturnsNull();

        var result = await CreateSut().Handle(
            new GetAttachmentThumbnailQuery(Guid.NewGuid(), Guid.NewGuid(), 200, 200), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.trade_attachment.not_found");
    }

    [Fact]
    public async Task Handle_WidthAboveMax_GetsClampedTo1024()
    {
        var att = PendingImageAttachment(Guid.NewGuid());
        _reviews.FindAttachmentByIdAsync(att.Id, att.UserId, Arg.Any<CancellationToken>())
            .Returns(att);
        _generator.GetThumbnailUrlAsync(att.ObjectKey, 1024, 1024, Arg.Any<CancellationToken>())
            .Returns("http://minio:9000/bucket/img.png?width=1024&height=1024&sig=abc");

        var result = await CreateSut().Handle(
            new GetAttachmentThumbnailQuery(att.Id, att.UserId, Width: 100_000, Height: 100_000), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Width.Should().Be(1024);
        result.Value.Height.Should().Be(1024);
        await _generator.Received(1).GetThumbnailUrlAsync(
            att.ObjectKey, 1024, 1024, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WidthBelowMin_GetsClampedTo16()
    {
        var att = PendingImageAttachment(Guid.NewGuid());
        _reviews.FindAttachmentByIdAsync(att.Id, att.UserId, Arg.Any<CancellationToken>())
            .Returns(att);
        _generator.GetThumbnailUrlAsync(att.ObjectKey, 16, 16, Arg.Any<CancellationToken>())
            .Returns("http://minio:9000/bucket/img.png?width=16&height=16&sig=abc");

        var result = await CreateSut().Handle(
            new GetAttachmentThumbnailQuery(att.Id, att.UserId, Width: 1, Height: 1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Width.Should().Be(16);
        result.Value.Height.Should().Be(16);
    }

    [Fact]
    public async Task Handle_JpegAttachment_StillAccepted()
    {
        var userId = Guid.NewGuid();
        var att = TradeAttachment.RequestSlot(
            id: Guid.NewGuid(),
            reviewId: Guid.NewGuid(),
            userId: userId,
            objectKey: "trading/attachments/u/t/r/a/img.jpg",
            contentType: "image/jpeg",
            sizeBytes: 1024L,
            now: FixedNow).Value;
        _reviews.FindAttachmentByIdAsync(att.Id, userId, Arg.Any<CancellationToken>())
            .Returns(att);
        _generator.GetThumbnailUrlAsync(att.ObjectKey, 200, 200, Arg.Any<CancellationToken>())
            .Returns("http://minio:9000/bucket/img.jpg?width=200&height=200&sig=abc");

        var result = await CreateSut().Handle(
            new GetAttachmentThumbnailQuery(att.Id, userId, 200, 200), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ContentType.Should().Be("image/jpeg");
    }
}