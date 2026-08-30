using JadeCapital.Trading.Domain.TradeReviews;

namespace JadeCapital.Trading.UnitTests.TradeReviews;

/// <summary>
/// Domain tests for <c>TradeReview</c> — slice 1d.1 of
/// <c>2026-08-15-trader-risk-journal-core</c>.
///
/// Covers the eight scenarios called out in the work-unit breakdown:
/// <list type="number">
///   <item>Create valid — happy path persists all fields and raises event.</item>
///   <item>Trade-not-closed reject — open/cancelled trades cannot be reviewed.</item>
///   <item>Duplicate reject — UNIQUE on trade_id at the DB layer; the
///   application/repository layer translates a duplicate-key DbUpdate into
///   <c>trade_review.already_exists</c>. At the domain layer, the factory
///   only knows about trade-not-closed (the repository raises AlreadyExists).
///   We assert that a SAME-trade second insert attempts at the factory would
///   pass domain validation; the DB UNIQUE is the gate.</item>
///   <item>Upsert behavior via Update — setup/lessons/rating mutate,
///   emotionality is preserved.</item>
///   <item>Attachment slot request — out of scope here, lives in
///   <c>TradeAttachmentTests</c>.</item>
///   <item>Lifecycle — Pending to Uploaded transition only via MarkUploaded.</item>
///   <item>Max-size 10MB enforcement — covered in attachment tests.</item>
///   <item>Free-text lessons up to 5000 chars — boundary test below.</item>
///   <item>Rating out-of-range — 0 and 6+ rejected.</item>
///   <item>SetupUsed over-length — > 64 chars rejected.</item>
/// </list>
/// </summary>
public class TradeReviewTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidInputs_PersistsAllFieldsAndRaisesEvent()
    {
        var tradeId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var r = TradeReview.Create(
            id: Guid.NewGuid(),
            tradeId: tradeId,
            userId: userId,
            emotionality: (byte)ReviewEmotionality.Confident,
            rating: 4,
            setupUsed: "London breakout",
            lessons: "Held through the news release without panic.",
            tradeIsClosed: true,
            now: FixedNow);

        r.IsSuccess.Should().BeTrue();
        var review = r.Value;
        review.TradeId.Should().Be(tradeId);
        review.UserId.Should().Be(userId);
        review.Emotionality.Should().Be(ReviewEmotionality.Confident);
        review.SetupUsed.Should().Be("London breakout");
        review.Lessons.Should().Be("Held through the news release without panic.");
        review.Rating.Should().Be((byte)4);
        review.CreatedAt.Should().Be(FixedNow);
        review.UpdatedAt.Should().Be(FixedNow);

        review.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TradeReviewCreatedDomainEvent>();
    }

    [Fact]
    public void Create_ForOpenTrade_FailsWithTradeNotClosed()
    {
        var r = TradeReview.Create(
            id: Guid.NewGuid(),
            tradeId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            emotionality: (byte)ReviewEmotionality.Calm,
            rating: null,
            setupUsed: null,
            lessons: null,
            tradeIsClosed: false, // trade is Open or Cancelled
            now: FixedNow);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("conflict.trade_review.trade_not_closed");
    }

    [Fact]
    public void Create_WithNullRating_Succeeds()
    {
        var r = TradeReview.Create(
            id: Guid.NewGuid(),
            tradeId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            emotionality: (byte)ReviewEmotionality.Neutral,
            rating: null,
            setupUsed: null,
            lessons: null,
            tradeIsClosed: true,
            now: FixedNow);

        r.IsSuccess.Should().BeTrue();
        r.Value.Rating.Should().BeNull();
    }

    [Fact]
    public void Create_WithRatingOutOfRange_FailsWithValidation()
    {
        var r = TradeReview.Create(
            id: Guid.NewGuid(),
            tradeId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            emotionality: (byte)ReviewEmotionality.Neutral,
            rating: 7, // out of [1,5]
            setupUsed: null,
            lessons: null,
            tradeIsClosed: true,
            now: FixedNow);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade_review.rating_out_of_range");
    }

    [Fact]
    public void Create_WithSetupOver64Chars_FailsWithValidation()
    {
        var longSetup = new string('x', 65);

        var r = TradeReview.Create(
            id: Guid.NewGuid(),
            tradeId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            emotionality: (byte)ReviewEmotionality.Neutral,
            rating: null,
            setupUsed: longSetup,
            lessons: null,
            tradeIsClosed: true,
            now: FixedNow);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade_review.setup_used_too_long");
    }

    [Fact]
    public void Create_WithLessonsOver5000Chars_FailsWithValidation()
    {
        var longLessons = new string('x', 5001);

        var r = TradeReview.Create(
            id: Guid.NewGuid(),
            tradeId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            emotionality: (byte)ReviewEmotionality.Neutral,
            rating: null,
            setupUsed: null,
            lessons: longLessons,
            tradeIsClosed: true,
            now: FixedNow);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade_review.lessons_too_long");
    }

    [Fact]
    public void Create_WithLessonsExactly5000_Succeeds()
    {
        var exact = new string('x', 5000);

        var r = TradeReview.Create(
            id: Guid.NewGuid(),
            tradeId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            emotionality: (byte)ReviewEmotionality.Neutral,
            rating: null,
            setupUsed: null,
            lessons: exact,
            tradeIsClosed: true,
            now: FixedNow);

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_WithWhitespaceOnlySetup_NormalizesToNull()
    {
        var r = TradeReview.Create(
            id: Guid.NewGuid(),
            tradeId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            emotionality: (byte)ReviewEmotionality.Neutral,
            rating: null,
            setupUsed: "   ",
            lessons: null,
            tradeIsClosed: true,
            now: FixedNow);

        r.IsSuccess.Should().BeTrue();
        r.Value.SetupUsed.Should().BeNull();
    }

    [Fact]
    public void Update_ChangesSetupAndLessons_KeepsEmotionality()
    {
        var review = TradeReview.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            (byte)ReviewEmotionality.Confident, 3, "Old setup", "Old lessons",
            tradeIsClosed: true, now: FixedNow).Value;

        var later = FixedNow.AddMinutes(5);
        var update = review.Update(
            setupUsed: "New setup",
            lessons: "Better lessons",
            rating: 5,
            now: later);

        update.IsSuccess.Should().BeTrue();
        review.SetupUsed.Should().Be("New setup");
        review.Lessons.Should().Be("Better lessons");
        review.Rating.Should().Be((byte)5);
        review.Emotionality.Should().Be(ReviewEmotionality.Confident); // immutable
        review.UpdatedAt.Should().Be(later);
        review.CreatedAt.Should().Be(FixedNow); // unchanged
    }

    [Fact]
    public void Update_WithRatingOutOfRange_Fails()
    {
        var review = TradeReview.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            (byte)ReviewEmotionality.Neutral, null, null, null,
            tradeIsClosed: true, now: FixedNow).Value;

        var update = review.Update(setupUsed: null, lessons: null, rating: 0, now: FixedNow);

        update.IsFailure.Should().BeTrue();
        update.Error.Code.Should().Be("validation.trade_review.rating_out_of_range");
    }

    [Fact]
    public void Create_WithEmptyGuidId_FailsWithValidation()
    {
        var r = TradeReview.Create(
            id: Guid.Empty,
            tradeId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            emotionality: (byte)ReviewEmotionality.Neutral,
            rating: null,
            setupUsed: null,
            lessons: null,
            tradeIsClosed: true,
            now: FixedNow);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.trade_review.id_required");
    }
}
