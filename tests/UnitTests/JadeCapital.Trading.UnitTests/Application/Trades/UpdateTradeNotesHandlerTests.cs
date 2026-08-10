using FluentValidation.TestHelper;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Trading.UnitTests.Application.Trades;

public class UpdateTradeNotesHandlerTests
{
    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<UpdateTradeNotesHandler> _logger = Substitute.For<ILogger<UpdateTradeNotesHandler>>();

    private UpdateTradeNotesHandler CreateSut() => new(_trades, _uow, _logger);

    private static Trade CreateOpenTrade(Guid userId)
    {
        var symbol = Symbol.Create("EUR/USD").Value;
        var volume = Money.Create(1000m, Currency.Usd).Value;
        var entry = Money.Create(1.10m, Currency.Usd).Value;
        return Trade.Open(
            Guid.NewGuid(), userId, symbol, AssetClass.Forex, TradeDirection.Long,
            volume, entry, "USD", null, null,
            new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero)).Value;
    }

    [Fact]
    public async Task Handle_ValidUpdate_ModifiesStrategyAndNotes()
    {
        var userId = Guid.NewGuid();
        var trade = CreateOpenTrade(userId);
        _trades.FindByIdAsync(trade.Id, Arg.Any<CancellationToken>()).Returns(trade);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = new UpdateTradeNotesCommand(trade.Id, userId, "scalping", "Updated notes");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        trade.Strategy.Should().Be("scalping");
        trade.Notes.Should().Be("Updated notes");
    }

    [Fact]
    public async Task Handle_CancelledTrade_ReturnsAlreadyCancelled()
    {
        var userId = Guid.NewGuid();
        var trade = CreateOpenTrade(userId);
        trade.Cancel(Substitute.For<IClock>());
        _trades.FindByIdAsync(trade.Id, Arg.Any<CancellationToken>()).Returns(trade);

        var cmd = new UpdateTradeNotesCommand(trade.Id, userId, "any", "any");
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.trade.already_cancelled");
    }

    [Fact]
    public void Validator_NotesTooLong_Fails()
    {
        var sut = new UpdateTradeNotesValidator();
        var cmd = new UpdateTradeNotesCommand(
            Guid.NewGuid(), Guid.NewGuid(), "ok", new string('n', 2001));

        sut.TestValidate(cmd).IsValid.Should().BeFalse();
    }
}