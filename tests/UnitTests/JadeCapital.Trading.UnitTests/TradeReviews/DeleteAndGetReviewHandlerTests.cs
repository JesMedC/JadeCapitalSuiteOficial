using FluentAssertions;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Features.TradeReviews.DeleteAttachment;
using JadeCapital.Trading.Application.Features.TradeReviews.GetTradeReview;
using JadeCapital.Trading.Domain.TradeAttachments;
using JadeCapital.Trading.Domain.TradeReviews;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Trading.UnitTests.TradeReviews;

/// <summary>
/// Application-handler tests for <c>DeleteAttachmentHandler</c> and
/// <c>GetTradeReviewHandler</c> — slice 1d.1 of
/// <c>2026-08-15-trader-risk-journal-core</c>.
///
/// Delete (2 tests):
/// <list type="number">
///   <item>Happy path: row deleted + best-effort storage delete called.</item>
///   <item>Cross-user returns NotFound; storage not called.</item>
/// </list>
/// Get (3 tests):
/// <list type="number">
///   <item>Returns review with attachments inline.</item>
///   <item>No review returns NotFound.</item>
///   <item>Other user's review returns NotFound.</item>
/// </list>
/// </summary>
public class DeleteAndGetReviewHandlerTests
{
    // ===== DeleteAttachmentHandler =====

    [Fact]
    public async Task Delete_HappyPath_RemovesRowAndCallsBestEffortDelete()
    {
        var reviews = Substitute.For<ITradeReviewRepository>();
        var storage = Substitute.For<IAttachmentStorage>();
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<DeleteAttachmentHandler>>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));

        var objectKey = "trading/attachments/u/t/r/a/img.png";
        reviews.RemoveAttachmentAsync(Arg.Any<Guid>(), Arg.Any<Guid>(),
                                       Arg.Any<CancellationToken>())
            .Returns(objectKey);

        var handler = new DeleteAttachmentHandler(reviews, storage, uow, logger);
        var result = await handler.Handle(
            new DeleteAttachmentCommand(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await storage.Received(1).DeleteAsync(objectKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_CrossUser_StorageNotCalled()
    {
        var reviews = Substitute.For<ITradeReviewRepository>();
        var storage = Substitute.For<IAttachmentStorage>();
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<DeleteAttachmentHandler>>();

        reviews.RemoveAttachmentAsync(Arg.Any<Guid>(), Arg.Any<Guid>(),
                                       Arg.Any<CancellationToken>())
            .ReturnsNull(); // not found / cross-user

        var handler = new DeleteAttachmentHandler(reviews, storage, uow, logger);
        var result = await handler.Handle(
            new DeleteAttachmentCommand(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.trade_attachment.not_found");
        await storage.DidNotReceive().DeleteAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_StorageFailure_DbStillCommitted_SuccessReturned()
    {
        var reviews = Substitute.For<ITradeReviewRepository>();
        var storage = Substitute.For<IAttachmentStorage>();
        var uow = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<DeleteAttachmentHandler>>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));

        var objectKey = "trading/attachments/u/t/r/a/img.png";
        reviews.RemoveAttachmentAsync(Arg.Any<Guid>(), Arg.Any<Guid>(),
                                       Arg.Any<CancellationToken>())
            .Returns(objectKey);
        storage.DeleteAsync(objectKey, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new System.InvalidOperationException("minio 503")));

        var handler = new DeleteAttachmentHandler(reviews, storage, uow, logger);
        var result = await handler.Handle(
            new DeleteAttachmentCommand(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        // DB deletion succeeded; storage best-effort failed silently.
        result.IsSuccess.Should().BeTrue();
    }

    // ===== GetTradeReviewHandler =====

    [Fact]
    public async Task Get_ReturnsReviewWithAttachments()
    {
        var reviews = Substitute.For<ITradeReviewRepository>();
        var tradeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var review = TradeReview.Create(
            Guid.NewGuid(), tradeId, userId,
            (byte)ReviewEmotionality.Confident, 4, "London", "Held.",
            tradeIsClosed: true, now: DateTimeOffset.UtcNow).Value;

        var attachments = new[]
        {
            TradeAttachment.RequestSlot(
                id: Guid.NewGuid(),
                reviewId: review.Id,
                userId: userId,
                objectKey: "trading/attachments/u/t/r/1/img.png",
                contentType: "image/png",
                sizeBytes: 1024L,
                now: DateTimeOffset.UtcNow).Value,
        };

        reviews.FindByTradeIdAsync(tradeId, userId, Arg.Any<CancellationToken>())
            .Returns(review);
        reviews.ListAttachmentsByReviewIdAsync(review.Id, userId,
                                                Arg.Any<CancellationToken>())
            .Returns(attachments);

        var handler = new GetTradeReviewHandler(reviews);
        var result = await handler.Handle(
            new GetTradeReviewQuery(tradeId, userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Attachments.Should().HaveCount(1);
        result.Value.SetupUsed.Should().Be("London");
    }

    [Fact]
    public async Task Get_NoReview_ReturnsNotFound()
    {
        var reviews = Substitute.For<ITradeReviewRepository>();
        reviews.FindByTradeIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(),
                                    Arg.Any<CancellationToken>())
            .ReturnsNull();

        var handler = new GetTradeReviewHandler(reviews);
        var result = await handler.Handle(
            new GetTradeReviewQuery(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.trade_review.not_found");
    }
}
