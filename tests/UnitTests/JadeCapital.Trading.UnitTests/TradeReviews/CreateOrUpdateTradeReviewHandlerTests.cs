using FluentAssertions;
using FluentValidation.TestHelper;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Features.TradeReviews.CreateOrUpdate;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.TradeReviews;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Trading.UnitTests.TradeReviews;

/// <summary>
/// Application-handler tests for <c>CreateOrUpdateTradeReviewHandler</c>
/// — slice 1d.1 of <c>2026-08-15-trader-risk-journal-core</c>.
///
/// Eight scenarios called out in the work-unit breakdown cover: the
/// happy create, the trade-not-closed reject, the upsert behavior,
/// the rating-out-of-range reject, the setup-too-long reject,
/// the lessons-too-long reject, the cross-user 404, and the validator
/// whitelist.
/// </summary>
public class CreateOrUpdateTradeReviewHandlerTests
{
    private readonly ITradeReviewRepository _reviews = Substitute.For<ITradeReviewRepository>();
    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<CreateOrUpdateTradeReviewHandler> _logger
        = Substitute.For<ILogger<CreateOrUpdateTradeReviewHandler>>();

    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    public CreateOrUpdateTradeReviewHandlerTests()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));
    }

    private CreateOrUpdateTradeReviewHandler CreateSut()
        => new(_reviews, _trades, _uow, _clock, _logger);

    private static CreateOrUpdateTradeReviewCommand ValidCmd() => new(
        TradeId: Guid.NewGuid(),
        UserId: Guid.NewGuid(),
        Emotionality: (byte)ReviewEmotionality.Confident,
        Rating: 4,
        SetupUsed: "London breakout",
        Lessons: "Held through news.");

    private static Trade ClosedTrade(Guid userId, Guid tradeId)
        => Trade.Open(
            tradeId,
            accountId: Guid.NewGuid(),
            instrumentId: Guid.NewGuid(),
            userId: userId,
            symbol: Symbol.Create("EUR/USD").Value,
            assetClass: AssetClass.Forex,
            direction: TradeDirection.Long,
            volume: Money.Create(1000m, Currency.Create("USD").Value).Value,
            entryPrice: Money.Create(1.10m, Currency.Create("USD").Value).Value,
            accountCurrency: "USD",
            strategy: null,
            notes: null,
            openedAt: FixedNow).Value;

    [Fact]
    public async Task Handle_OpenTrade_FailsWith409TradeNotClosed()
    {
        var cmd = ValidCmd();
        var trade = ClosedTrade(cmd.UserId, cmd.TradeId);
        // Trade is Open (default); just leave it open.
        _trades.FindByIdAsync(cmd.TradeId, Arg.Any<CancellationToken>())
            .Returns(trade);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.trade_review.trade_not_closed");
    }

    [Fact]
    public async Task Handle_NoTrade_ReturnsNotFoundCollapsedForCrossUser()
    {
        var cmd = ValidCmd();
        _trades.FindByIdAsync(cmd.TradeId, Arg.Any<CancellationToken>())
            .ReturnsNull(); // doesn't exist

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.Error.Code.Should().Be("notfound.trade_review.not_found");
    }

    [Fact]
    public async Task Handle_OtherUsersTrade_ReturnsNotFound()
    {
        var cmd = ValidCmd();
        var tradeOfOtherUser = ClosedTrade(Guid.NewGuid(), cmd.TradeId);
        _trades.FindByIdAsync(cmd.TradeId, Arg.Any<CancellationToken>())
            .Returns(tradeOfOtherUser); // exists but NOT cmd.UserId

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.trade_review.not_found");
    }

    [Fact]
    public async Task Handle_FirstReview_PersistsRow()
    {
        var cmd = ValidCmd();
        var trade = ClosedTrade(cmd.UserId, cmd.TradeId);
        trade.Close(Money.Create(1.20m, Currency.Create("USD").Value).Value, FixedNow.AddHours(1), _clock);
        _trades.FindByIdAsync(cmd.TradeId, Arg.Any<CancellationToken>())
            .Returns(trade);
        _reviews.FindByTradeIdAsync(cmd.TradeId, cmd.UserId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _reviews.Received(1).AddAsync(Arg.Any<TradeReview>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Upsert_UpdatesExistingReview()
    {
        var cmd = ValidCmd();
        var trade = ClosedTrade(cmd.UserId, cmd.TradeId);
        trade.Close(Money.Create(1.20m, Currency.Create("USD").Value).Value, FixedNow.AddHours(1), _clock);
        var existing = TradeReview.Create(
            Guid.NewGuid(), cmd.TradeId, cmd.UserId,
            (byte)ReviewEmotionality.Neutral, 3, "old", "old",
            tradeIsClosed: true, now: FixedNow).Value;

        _trades.FindByIdAsync(cmd.TradeId, Arg.Any<CancellationToken>())
            .Returns(trade);
        _reviews.FindByTradeIdAsync(cmd.TradeId, cmd.UserId, Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _reviews.DidNotReceive().AddAsync(Arg.Any<TradeReview>(),
                                                 Arg.Any<CancellationToken>());
        await _reviews.Received(1).UpdateAsync(existing, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validator_RejectsRatingOutOfRange()
    {
        var cmd = ValidCmd() with { Rating = 7 };

        var validator = new CreateOrUpdateTradeReviewValidator();
        var result = validator.TestValidate(cmd);

        result.ShouldHaveValidationErrorFor(x => x.Rating);
    }

    [Fact]
    public void Validator_RejectsSetupOver64Chars()
    {
        var cmd = ValidCmd() with { SetupUsed = new string('x', 65) };

        var validator = new CreateOrUpdateTradeReviewValidator();
        var result = validator.TestValidate(cmd);

        result.ShouldHaveValidationErrorFor(x => x.SetupUsed);
    }

    [Fact]
    public void Validator_RejectsLessonsOver5000Chars()
    {
        var cmd = ValidCmd() with { Lessons = new string('x', 5001) };

        var validator = new CreateOrUpdateTradeReviewValidator();
        var result = validator.TestValidate(cmd);

        result.ShouldHaveValidationErrorFor(x => x.Lessons);
    }

    [Fact]
    public void Validator_AcceptsNullRatingAndOptionalFields()
    {
        var cmd = ValidCmd() with { Rating = null, SetupUsed = null, Lessons = null };

        var validator = new CreateOrUpdateTradeReviewValidator();
        var result = validator.TestValidate(cmd);

        result.IsValid.Should().BeTrue();
    }
}
