using JadeCapital.Trading.Domain.TradeAttachments;
using JadeCapital.Trading.Domain.TradeReviews;

namespace JadeCapital.Trading.UnitTests.TradeReviews;

/// <summary>
/// Domain tests for <c>TradeAttachment</c> — slice 1d.1 of
/// <c>2026-08-15-trader-risk-journal-core</c>.
///
/// Covers the lifecycle and boundary tests for the attachment aggregate:
/// <list type="number">
///   <item>RequestSlot happy path — creates Pending row with all fields.</item>
///   <item>MarkUploaded from Pending — status flips, UploadedAt populated,
///   sha256 stored canonicalized lowercase.</item>
///   <item>MarkUploaded twice — second call rejected with AlreadyUploaded.</item>
///   <item>Size 0 / 10 MB+1 rejected — boundary check.</item>
///   <item>Size exactly 10 MB accepted — boundary inclusive.</item>
///   <item>Malformed sha256 rejected on MarkUploaded.</item>
///   <item>MarkFailed idempotency — second call rejected.</item>
/// </list>
/// </summary>
public class TradeAttachmentTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    private static string SampleKey(Guid userId, Guid tradeId, Guid reviewId, Guid attachmentId)
        => $"trading/attachments/{userId}/{tradeId}/{reviewId}/{attachmentId}/screenshot.png";

    [Fact]
    public void RequestSlot_WithValidInputs_PersistsPendingRow()
    {
        var userId = Guid.NewGuid();
        var tradeId = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();

        var r = TradeAttachment.RequestSlot(
            id: attachmentId,
            reviewId: reviewId,
            userId: userId,
            objectKey: SampleKey(userId, tradeId, reviewId, attachmentId),
            contentType: "image/png",
            sizeBytes: 1024L,
            now: FixedNow);

        r.IsSuccess.Should().BeTrue();
        var attachment = r.Value;
        attachment.Status.Should().Be(TradeAttachmentStatus.Pending);
        attachment.UploadedAt.Should().BeNull();
        attachment.Sha256.Should().BeNull();
        attachment.ObjectKey.Should().StartWith($"trading/attachments/{userId}/{tradeId}/{reviewId}/");
        attachment.ContentType.Should().Be("image/png");
        attachment.SizeBytes.Should().Be(1024L);
        attachment.CreatedAt.Should().Be(FixedNow);
    }

    [Fact]
    public void RequestSlot_WithSizeZero_FailsWithValidation()
    {
        var r = TradeAttachment.RequestSlot(
            id: Guid.NewGuid(),
            reviewId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            objectKey: "trading/attachments/foo/bar",
            contentType: "image/png",
            sizeBytes: 0L,
            now: FixedNow);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade_attachment.size_out_of_range");
    }

    [Fact]
    public void RequestSlot_WithSizeExactly10485760_Succeeds()
    {
        var r = TradeAttachment.RequestSlot(
            id: Guid.NewGuid(),
            reviewId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            objectKey: "trading/attachments/foo/bar",
            contentType: "application/pdf",
            sizeBytes: TradeAttachment.MaxSizeBytes,
            now: FixedNow);

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void RequestSlot_WithSizeOver10MB_FailsWithValidation()
    {
        var r = TradeAttachment.RequestSlot(
            id: Guid.NewGuid(),
            reviewId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            objectKey: "trading/attachments/foo/bar",
            contentType: "image/png",
            sizeBytes: TradeAttachment.MaxSizeBytes + 1,
            now: FixedNow);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade_attachment.size_out_of_range");
    }

    [Fact]
    public void RequestSlot_WithEmptyContentType_Fails()
    {
        var r = TradeAttachment.RequestSlot(
            id: Guid.NewGuid(),
            reviewId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            objectKey: "trading/attachments/foo/bar",
            contentType: "",
            sizeBytes: 1024L,
            now: FixedNow);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade_attachment.content_type_required");
    }

    [Fact]
    public void MarkUploaded_FromPending_FlipsStatusAndStoresShaLowercased()
    {
        var attachment = TradeAttachment.RequestSlot(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "trading/attachments/foo/bar", "image/png", 1024L, FixedNow).Value;

        var later = FixedNow.AddSeconds(30);
        var sha = new string('A', 64); // uppercase

        var r = attachment.MarkUploaded(sha, tradeId: Guid.NewGuid(), later);

        r.IsSuccess.Should().BeTrue();
        attachment.Status.Should().Be(TradeAttachmentStatus.Uploaded);
        attachment.UploadedAt.Should().Be(later);
        attachment.Sha256.Should().Be(new string('a', 64)); // canonicalized lowercase
    }

    [Fact]
    public void MarkUploaded_Twice_FailsWithAlreadyUploaded()
    {
        var attachment = TradeAttachment.RequestSlot(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "trading/attachments/foo/bar", "image/png", 1024L, FixedNow).Value;

        attachment.MarkUploaded(null, tradeId: Guid.NewGuid(), FixedNow.AddSeconds(1));
        var second = attachment.MarkUploaded(null, tradeId: Guid.NewGuid(), FixedNow.AddSeconds(2));

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("conflict.trade_attachment.already_uploaded");
    }

    [Fact]
    public void MarkUploaded_WithMalformedSha_FailsWithValidation()
    {
        var attachment = TradeAttachment.RequestSlot(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "trading/attachments/foo/bar", "image/png", 1024L, FixedNow).Value;

        var r = attachment.MarkUploaded("not-hex", tradeId: Guid.NewGuid(), FixedNow.AddSeconds(1));

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade_attachment.sha256_shape_invalid");
    }

    [Fact]
    public void MarkFailed_FromPending_FlipsStatusToFailed()
    {
        var attachment = TradeAttachment.RequestSlot(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "trading/attachments/foo/bar", "image/png", 1024L, FixedNow).Value;

        var r = attachment.MarkFailed(FixedNow.AddSeconds(5));

        r.IsSuccess.Should().BeTrue();
        attachment.Status.Should().Be(TradeAttachmentStatus.Failed);
    }

    [Fact]
    public void MarkFailed_OnUploaded_FailsWithAlreadyUploaded()
    {
        var attachment = TradeAttachment.RequestSlot(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "trading/attachments/foo/bar", "image/png", 1024L, FixedNow).Value;

        attachment.MarkUploaded(null, tradeId: Guid.NewGuid(), FixedNow.AddSeconds(1));
        var r = attachment.MarkFailed(FixedNow.AddSeconds(2));

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("conflict.trade_attachment.already_uploaded");
    }

    [Fact]
    public void MarkUploaded_WithNullSha_StoresNull()
    {
        var attachment = TradeAttachment.RequestSlot(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "trading/attachments/foo/bar", "image/png", 1024L, FixedNow).Value;

        attachment.MarkUploaded(null, tradeId: Guid.NewGuid(), FixedNow.AddSeconds(1));

        attachment.Sha256.Should().BeNull();
        attachment.Status.Should().Be(TradeAttachmentStatus.Uploaded);
    }
}
