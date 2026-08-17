using FluentAssertions;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Features.TradeReviews.ConfirmAttachmentUpload;
using JadeCapital.Trading.Domain.TradeAttachments;
using JadeCapital.Trading.Domain.TradeReviews;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Trading.UnitTests.TradeReviews;

/// <summary>
/// Application-handler tests for <c>ConfirmAttachmentUploadedHandler</c>
/// — slice 1d.1 of <c>2026-08-15-trader-risk-journal-core</c>.
///
/// Three required scenarios:
/// <list type="number">
///   <item>Valid pending attachment + storage confirms size +
///   no sha — happy path, status flips, event raised.</item>
///   <item>Storage says object missing / size mismatch —
///   attachment is marked Failed and 409 returned.</item>
///   <item>Already-uploaded attachment is idempotent —
///   storage not consulted again, same DTO returned.</item>
/// </list>
/// Plus cross-user scope (404) and sha validation.
/// </summary>
public class ConfirmAttachmentUploadedHandlerTests
{
    private readonly ITradeReviewRepository _reviews = Substitute.For<ITradeReviewRepository>();
    private readonly IAttachmentStorage _storage = Substitute.For<IAttachmentStorage>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<ConfirmAttachmentUploadedHandler> _logger
        = Substitute.For<ILogger<ConfirmAttachmentUploadedHandler>>();

    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    public ConfirmAttachmentUploadedHandlerTests()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));
    }

    private ConfirmAttachmentUploadedHandler CreateSut()
        => new(_reviews, _storage, _uow, _clock, _logger);

    private static TradeAttachment PendingAttachment(Guid attachmentId)
    {
        return TradeAttachment.RequestSlot(
            id: attachmentId,
            reviewId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            objectKey: "trading/attachments/u/t/r/a/img.png",
            contentType: "image/png",
            sizeBytes: 2048L,
            now: FixedNow).Value;
    }

    [Fact]
    public async Task Handle_PendingAndStorageConfirms_FlipsToUploaded()
    {
        var att = PendingAttachment(Guid.NewGuid());
        _reviews.FindAttachmentByIdAsync(att.Id, att.UserId, Arg.Any<CancellationToken>())
            .Returns(att);
        _reviews.GetTradeIdByAttachmentIdAsync(att.Id, Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());
        _storage.VerifyObjectExistsAsync(att.ObjectKey, att.SizeBytes,
                                          Arg.Any<CancellationToken>())
            .Returns(true);

        var cmd = new ConfirmAttachmentUploadedCommand(att.Id, att.UserId, Sha256: null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        att.Status.Should().Be(TradeAttachmentStatus.Uploaded);
        att.UploadedAt.Should().Be(FixedNow);
        att.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TradeAttachmentUploadedDomainEvent>();
    }

    [Fact]
    public async Task Handle_StorageMismatch_MarksFailedAndReturnsConflict()
    {
        var att = PendingAttachment(Guid.NewGuid());
        _reviews.FindAttachmentByIdAsync(att.Id, att.UserId, Arg.Any<CancellationToken>())
            .Returns(att);
        _storage.VerifyObjectExistsAsync(att.ObjectKey, att.SizeBytes,
                                          Arg.Any<CancellationToken>())
            .Returns(false); // size mismatch or object missing

        var cmd = new ConfirmAttachmentUploadedCommand(att.Id, att.UserId, Sha256: null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.trade_attachment.upload_failed");
        att.Status.Should().Be(TradeAttachmentStatus.Failed);
    }

    [Fact]
    public async Task Handle_AlreadyUploaded_IsIdempotentAndReturnsDto()
    {
        var att = PendingAttachment(Guid.NewGuid());
        att.MarkUploaded(null, tradeId: Guid.NewGuid(), now: FixedNow);

        _reviews.FindAttachmentByIdAsync(att.Id, att.UserId, Arg.Any<CancellationToken>())
            .Returns(att);

        var cmd = new ConfirmAttachmentUploadedCommand(att.Id, att.UserId, Sha256: null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Storage NO se consulta si ya esta uploaded.
        await _storage.DidNotReceive().VerifyObjectExistsAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CrossUser_ReturnsNotFound()
    {
        _reviews.FindAttachmentByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(),
                                          Arg.Any<CancellationToken>())
            .ReturnsNull();

        var cmd = new ConfirmAttachmentUploadedCommand(Guid.NewGuid(), Guid.NewGuid(),
                                                       Sha256: null);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.trade_attachment.not_found");
    }

    [Fact]
    public async Task Handle_ShaProvided_UppercaseCanonicalizedToLowercase()
    {
        var att = PendingAttachment(Guid.NewGuid());
        _reviews.FindAttachmentByIdAsync(att.Id, att.UserId, Arg.Any<CancellationToken>())
            .Returns(att);
        _reviews.GetTradeIdByAttachmentIdAsync(att.Id, Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());
        _storage.VerifyObjectExistsAsync(att.ObjectKey, att.SizeBytes,
                                          Arg.Any<CancellationToken>())
            .Returns(true);

        var shaUppercase = new string('A', 64);
        var cmd = new ConfirmAttachmentUploadedCommand(att.Id, att.UserId, shaUppercase);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        att.Sha256.Should().Be(new string('a', 64));
    }
}
