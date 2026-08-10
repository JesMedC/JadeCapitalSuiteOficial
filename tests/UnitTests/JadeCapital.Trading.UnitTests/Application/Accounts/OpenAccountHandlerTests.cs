using NSubstitute.ReturnsExtensions;
using JadeCapital.Trading.Domain.Accounts;
using JadeCapital.Trading.Domain.Enums;

namespace JadeCapital.Trading.UnitTests.Application.Accounts;

public class OpenAccountHandlerTests
{
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<OpenAccountHandler> _logger = Substitute.For<ILogger<OpenAccountHandler>>();

    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private OpenAccountHandler CreateSut() => new(_accounts, _uow, _clock, _logger);

    private static OpenAccountCommand ValidCommand() => new(
        UserId: Guid.NewGuid(),
        Name: "IC Markets EUR",
        Broker: "IC Markets",
        MarketType: MarketType.Forex,
        Currency: "USD",
        InitialBalance: 1000m,
        Leverage: 100m);

    [Fact]
    public async Task Handle_ForexWithLeverage_PersistsAndReturnsAccountDto()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand();
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(cmd.UserId);
        result.Value.Name.Should().Be("IC Markets EUR");
        result.Value.Broker.Should().Be("IC Markets");
        result.Value.Currency.Should().Be("USD");
        result.Value.InitialBalance.Should().Be(1000m);
        result.Value.Leverage.Should().Be(100m);
        result.Value.MarketType.Should().Be(MarketType.Forex);
        result.Value.IsActive.Should().BeTrue();
        result.Value.CreatedAt.Should().Be(FixedNow);

        await _accounts.Received(1).AddAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForexWithNullLeverage_FailsValidation()
    {
        _clock.UtcNow.Returns(FixedNow);

        var cmd = ValidCommand() with { Leverage = null };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        // El handler no intercepta: deja que FluentValidation falle en la pipeline.
        // Acá el call sin MediatR pipeline no falla por validator, pero Account.Open
        // rechaza. Por la naturaleza del test unitario (sin pipeline), validamos
        // que el factory falle.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.account.leverage_required_for_forex");
    }

    [Fact]
    public async Task Handle_BinaryWithoutLeverage_SucceedsWithDefaultOne()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand() with { MarketType = MarketType.Binary, Leverage = null };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.MarketType.Should().Be(MarketType.Binary);
        result.Value.Leverage.Should().Be(1m); // default
    }

    [Fact]
    public async Task Handle_BinaryWithLeverage_KeepsProvidedLeverage()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand() with { MarketType = MarketType.Binary, Leverage = 50m };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.MarketType.Should().Be(MarketType.Binary);
        result.Value.Leverage.Should().Be(50m);
    }

    [Fact]
    public async Task Handle_LowercaseCurrency_NormalizesToUppercase()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand() with { Currency = "usd" };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Handle_InvalidCurrency_ReturnsValidationFailure()
    {
        _clock.UtcNow.Returns(FixedNow);

        var cmd = ValidCommand() with { Currency = "ARS" }; // no esta en whitelist

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.");
        await _accounts.DidNotReceive().AddAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ZeroLeverage_ReturnsDomainFailure()
    {
        _clock.UtcNow.Returns(FixedNow);

        var cmd = ValidCommand() with { Leverage = 0m };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.account.leverage_required_for_forex");
    }

    [Fact]
    public async Task Handle_InvalidMarketType_ReturnsDomainFailure()
    {
        _clock.UtcNow.Returns(FixedNow);

        var cmd = ValidCommand() with { MarketType = (MarketType)999 };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.account.invalid_market_type");
    }

    [Fact]
    public async Task Handle_SaveChangesFails_ThrowsConflictDomainException()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Failure<int>(Error.Conflict("db.concurrency", "Concurrency conflict.")));

        var act = () => CreateSut().Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictDomainException>()
            .WithMessage("Concurrency conflict.");
    }
}
