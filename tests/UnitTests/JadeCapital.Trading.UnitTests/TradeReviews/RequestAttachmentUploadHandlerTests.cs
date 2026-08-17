using FluentAssertions;
using FluentValidation.TestHelper;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Features.TradeReviews.RequestAttachmentUpload;
using JadeCapital.Trading.Domain.TradeAttachments;
using JadeCapital.Trading.Domain.TradeReviews;
using NSubstitute.ReturnsExtensions;
using MediatR;

namespace JadeCapital.Trading.UnitTests.TradeReviews;

/// <summary>
/// Application-handler tests for <c>RequestAttachmentUploadHandler</c>
/// — slice 1d.1 of <c>2026-08-15-trader-risk-journal-core</c>.
///
/// Four required scenarios:
/// <list type="number">
///   <item>Valid request returns DTO with presigned URL + 15-min TTL.</item>
///   <item>Invalid content-type (e.g. text/html) rejected with
///   validation error.</item>
///   <item>Oversized attachment (&gt; 10 MB) rejected with validation error.</item>
///   <item>Max attachments per review cap (5) kicks in.</item>
/// </list>
/// Cross-user scope and NotFound behavior are also covered.
/// </summary>
public class RequestAttachmentUploadHandlerTests
{
    private readonly ITradeReviewRepository _reviews = Substitute.For<ITradeReviewRepository>();
    private readonly IAttachmentStorage _storage = Substitute.For<IAttachmentStorage>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<RequestAttachmentUploadHandler> _logger
        = Substitute.For<ILogger<RequestAttachmentUploadHandler>>();

    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    public RequestAttachmentUploadHandlerTests()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));
        _storage.GetPresignedPutUrlAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(),
                                          Arg.Any<CancellationToken>())
            .Returns("http://minio:9000/bucket/presigned?signature=xyz");
    }

    private RequestAttachmentUploadHandler CreateSut()
        => new(_reviews, _storage, _uow, _clock, _logger);

    private static TradeReview BuildReview(Guid tradeId, Guid userId)
    {
        return TradeReview.Create(
            id: Guid.NewGuid(),
            tradeId: tradeId,
            userId: userId,
            emotionality: (byte)ReviewEmotionality.Confident,
            rating: 4,
            setupUsed: "London",
            lessons: "Held.",
            tradeIsClosed: true,
            now: FixedNow).Value;
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsPresignedDto()
    {
        var tradeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        var review = TradeReview.Create(
            reviewId, tradeId, userId,
            (byte)ReviewEmotionality.Confident, 4, "London", "Held.",
            tradeIsClosed: true, now: FixedNow).Value;

        _reviews.FindByTradeIdAsync(tradeId, userId, Arg.Any<CancellationToken>())
            .Returns(review);
        _reviews.CountAttachmentsByReviewIdAsync(reviewId, Arg.Any<CancellationToken>())
            .Returns(0);

        var cmd = new RequestAttachmentUploadCommand(
            tradeId, userId, "image/png", 1024L, "screenshot.png");

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.ObjectKey.Should().StartWith($"trading/attachments/{userId}/{tradeId}/{reviewId}/");
        dto.ObjectKey.Should().EndWith("/screenshot.png");
        dto.PutUrl.Should().StartWith("http://minio:9000/");
        dto.ExpiresInSeconds.Should().Be(900); // 15 min TTL
        dto.AttachmentId.Should().NotBe(Guid.Empty);

        await _storage.Received(1).GetPresignedPutUrlAsync(
            Arg.Is<string>(s => s.StartsWith("trading/attachments/")),
            TimeSpan.FromMinutes(15),
            Arg.Any<CancellationToken>());
        await _reviews.Received(1).AddAttachmentAsync(
            Arg.Is<TradeAttachment>(a => a.Status == TradeAttachmentStatus.Pending),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validator_RejectsContentTypeOutsideWhitelist()
    {
        var cmd = new RequestAttachmentUploadCommand(
            Guid.NewGuid(), Guid.NewGuid(), "text/html", 1024L, "doc.html");

        var validator = new RequestAttachmentUploadValidator();
        var result = validator.TestValidate(cmd);

        result.ShouldHaveValidationErrorFor(x => x.ContentType);
    }

    [Fact]
    public void Validator_RejectsSizeAboveTenMegabytes()
    {
        // 11 MB > 10 MB cap.
        var cmd = new RequestAttachmentUploadCommand(
            Guid.NewGuid(), Guid.NewGuid(), "image/png", 11L * 1024L * 1024L, "huge.png");

        var validator = new RequestAttachmentUploadValidator();
        var result = validator.TestValidate(cmd);

        result.ShouldHaveValidationErrorFor(x => x.SizeBytes);
    }

    [Fact]
    public async Task Handle_AlreadyFiveAttachments_FailsWithTooMany()
    {
        var tradeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var review = TradeReview.Create(
            Guid.NewGuid(), tradeId, userId,
            (byte)ReviewEmotionality.Confident, 4, null, null,
            tradeIsClosed: true, now: FixedNow).Value;

        _reviews.FindByTradeIdAsync(tradeId, userId, Arg.Any<CancellationToken>())
            .Returns(review);
        _reviews.CountAttachmentsByReviewIdAsync(review.Id, Arg.Any<CancellationToken>())
            .Returns(5); // already at cap

        var cmd = new RequestAttachmentUploadCommand(
            tradeId, userId, "image/png", 1024L, "sixth.png");

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("too_many_attachments");
        await _storage.DidNotReceive().GetPresignedPutUrlAsync(
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoReviewForTrade_ReturnsNotFound()
    {
        _reviews.FindByTradeIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(),
                                     Arg.Any<CancellationToken>())
            .ReturnsNull();

        var cmd = new RequestAttachmentUploadCommand(
            Guid.NewGuid(), Guid.NewGuid(), "image/png", 1024L, "x.png");

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.trade_review.not_found");
    }

    [Fact]
    public async Task Handle_SanitizesFilename_StripsPathTraversal()
    {
        var tradeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var review = TradeReview.Create(
            Guid.NewGuid(), tradeId, userId,
            (byte)ReviewEmotionality.Confident, 4, null, null,
            tradeIsClosed: true, now: FixedNow).Value;

        _reviews.FindByTradeIdAsync(tradeId, userId, Arg.Any<CancellationToken>())
            .Returns(review);
        _reviews.CountAttachmentsByReviewIdAsync(review.Id, Arg.Any<CancellationToken>())
            .Returns(0);

        var cmd = new RequestAttachmentUploadCommand(
            tradeId, userId, "image/png", 1024L,
            "../../etc/passwd.png");

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Path.GetFileName colapsa "../" al basename; sin directory traversal.
        result.Value.ObjectKey.Should().EndWith("/passwd.png");
        result.Value.ObjectKey.Should().NotContain("..");
    }
}
