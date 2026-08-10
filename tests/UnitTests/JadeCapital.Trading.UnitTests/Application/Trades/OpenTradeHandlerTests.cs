using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Trading.UnitTests.Application.Trades;

public class OpenTradeHandlerTests
{
    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<OpenTradeHandler> _logger = Substitute.For<ILogger<OpenTradeHandler>>();

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private OpenTradeHandler CreateSut() => new(_trades, _uow, _clock, _logger);

    private static OpenTradeCommand ValidCommand() => new(
        UserId: Guid.NewGuid(),
        Symbol: "EUR/USD",
        AssetClass: AssetClass.Forex,
        Direction: TradeDirection.Long,
        Volume: 1000m,
        VolumeCurrency: "USD",
        EntryPrice: 1.10m,
        EntryPriceCurrency: "USD",
        Strategy: "trend-following",
        Notes: "Entry at breakout");

    [Fact]
    public async Task Handle_ValidCommand_PersistsAndReturnsTradeDto()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand();
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Symbol.Should().Be("EUR/USD");
        result.Value.AssetClass.Should().Be(AssetClass.Forex);
        result.Value.Direction.Should().Be(TradeDirection.Long);
        result.Value.Status.Should().Be(TradeStatus.Open);
        result.Value.Volume.Should().Be(1000m);
        result.Value.VolumeCurrency.Should().Be("USD");
        result.Value.EntryPrice.Should().Be(1.10m);
        result.Value.EntryPriceCurrency.Should().Be("USD");
        result.Value.AccountCurrency.Should().Be("USD");
        result.Value.UserId.Should().Be(cmd.UserId);
        result.Value.OpenedAt.Should().Be(FixedNow);

        await _trades.Received(1).AddAsync(Arg.Any<Trade>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidSymbol_ReturnsValidationFailure()
    {
        _clock.UtcNow.Returns(FixedNow);

        var cmd = ValidCommand() with { Symbol = "ab" }; // < 3 chars

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.symbol");
        await _trades.DidNotReceive().AddAsync(Arg.Any<Trade>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ZeroVolume_ReturnsValidationFailure()
    {
        _clock.UtcNow.Returns(FixedNow);

        var cmd = ValidCommand() with { Volume = 0m };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        // La validacion la hace Trade.Open (dominio), no Money.Create.
        result.Error.Code.Should().Be("validation.trade.volume_must_be_positive");
        await _trades.DidNotReceive().AddAsync(Arg.Any<Trade>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnsupportedVolumeCurrency_ReturnsValidationFailure()
    {
        _clock.UtcNow.Returns(FixedNow);

        var cmd = ValidCommand() with { VolumeCurrency = "ARS" }; // no esta en la whitelist

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.currency.code_unsupported");
        await _trades.DidNotReceive().AddAsync(Arg.Any<Trade>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DomainRuleFails_ReturnsDomainError()
    {
        _clock.UtcNow.Returns(FixedNow);

        // EUR/USD pide quote=USD; pasamos entry en EUR -> mismatch.
        var cmd = ValidCommand() with { EntryPriceCurrency = "EUR" };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.trade.entry_price_currency_mismatch");
    }

    [Fact]
    public async Task Handle_RepositoryNotCalled_WhenValidationFails()
    {
        _clock.UtcNow.Returns(FixedNow);

        var cmd = ValidCommand() with { EntryPrice = 0m };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}