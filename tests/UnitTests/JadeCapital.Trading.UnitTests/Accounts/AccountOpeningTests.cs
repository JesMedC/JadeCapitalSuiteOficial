using JadeCapital.Trading.Domain.Accounts;
using JadeCapital.Trading.Domain.Enums;

namespace JadeCapital.Trading.UnitTests.Accounts;

public class AccountOpeningTests
{
    private static readonly IClock Clock = Substitute.For<IClock>();

    [Fact]
    public void Open_WithValidData_CreatesActiveAccount()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        Clock.UtcNow.Returns(now);

        var r = Account.Open(
            id, userId,
            name: "IC Markets EUR",
            broker: "IC Markets",
            marketType: MarketType.Forex,
            currency: "USD",
            initialBalance: 1000m,
            leverage: 100m,
            clock: Clock);

        r.IsSuccess.Should().BeTrue();
        var account = r.Value;
        account.Id.Should().Be(id);
        account.UserId.Should().Be(userId);
        account.Name.Should().Be("IC Markets EUR");
        account.Broker.Should().Be("IC Markets");
        account.MarketType.Should().Be(MarketType.Forex);
        account.Currency.Should().Be("USD");
        account.InitialBalance.Should().Be(1000m);
        account.Leverage.Should().Be(100m);
        account.IsActive.Should().BeTrue();
        account.CreatedAt.Should().Be(now);
        account.UpdatedAt.Should().BeNull();
        account.DomainEvents.Should().ContainSingle(e => e is AccountOpenedDomainEvent);
    }

    [Fact]
    public void Open_BinaryWithoutLeverage_DefaultsLeverageToOne()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        Clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));

        var r = Account.Open(
            id, userId,
            name: "Deriv Synthetic",
            broker: "Deriv",
            marketType: MarketType.Binary,
            currency: "USD",
            initialBalance: 100m,
            leverage: null,
            clock: Clock);

        r.IsSuccess.Should().BeTrue();
        r.Value.MarketType.Should().Be(MarketType.Binary);
        r.Value.Leverage.Should().Be(1m);
        r.Value.DomainEvents.OfType<AccountOpenedDomainEvent>().Single().MarketType.Should().Be(MarketType.Binary);
    }

    [Fact]
    public void Open_BinaryWithLeverage_KeepsProvidedLeverage()
    {
        Clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));

        var r = Account.Open(
            Guid.NewGuid(), Guid.NewGuid(),
            name: "Deriv",
            broker: "Deriv",
            marketType: MarketType.Binary,
            currency: "USD",
            initialBalance: 0m,
            leverage: 50m,
            clock: Clock);

        r.IsSuccess.Should().BeTrue();
        r.Value.Leverage.Should().Be(50m);
    }

    [Fact]
    public void Open_ForexWithoutLeverage_Fails()
    {
        var r = Account.Open(
            Guid.NewGuid(), Guid.NewGuid(),
            "name", "broker", MarketType.Forex, "USD",
            0m, null, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.account.leverage_required_for_forex");
    }

    [Fact]
    public void Open_ForexWithZeroLeverage_Fails()
    {
        var r = Account.Open(
            Guid.NewGuid(), Guid.NewGuid(),
            "name", "broker", MarketType.Forex, "USD",
            0m, 0m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.account.leverage_required_for_forex");
    }

    [Fact]
    public void Open_WithInvalidMarketType_Fails()
    {
        var r = Account.Open(
            Guid.NewGuid(), Guid.NewGuid(),
            "name", "broker", (MarketType)999, "USD",
            0m, 100m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.account.invalid_market_type");
    }

    [Fact]
    public void Open_WithEmptyId_Fails()
    {
        var r = Account.Open(
            Guid.Empty, Guid.NewGuid(),
            "name", "broker", MarketType.Forex, "USD",
            0m, 100m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.account.id_required");
    }

    [Fact]
    public void Open_WithEmptyUserId_Fails()
    {
        var r = Account.Open(
            Guid.NewGuid(), Guid.Empty,
            "name", "broker", MarketType.Forex, "USD",
            0m, 100m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.account.user_id_required");
    }

    [Fact]
    public void Open_WithEmptyName_Fails()
    {
        var r = Account.Open(
            Guid.NewGuid(), Guid.NewGuid(),
            "", "broker", MarketType.Forex, "USD",
            0m, 100m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.account.name_required");
    }

    [Fact]
    public void Open_WithNameTooLong_Fails()
    {
        var r = Account.Open(
            Guid.NewGuid(), Guid.NewGuid(),
            new string('a', Account.MaxNameLength + 1), "broker", MarketType.Forex, "USD",
            0m, 100m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.account.name_too_long");
    }

    [Fact]
    public void Open_WithInvalidCurrency_Fails()
    {
        // "USDA" no es 3 letras. Currency.Create falla.
        var r = Account.Open(
            Guid.NewGuid(), Guid.NewGuid(),
            "name", "broker", MarketType.Forex, "USDA",
            0m, 100m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.account.currency_code_invalid");
    }

    [Fact]
    public void Open_WithLowercaseCurrency_NormalizesToUppercase()
    {
        var r = Account.Open(
            Guid.NewGuid(), Guid.NewGuid(),
            "name", "broker", MarketType.Forex, "usd",
            0m, 100m, Clock);

        r.IsSuccess.Should().BeTrue();
        r.Value.Currency.Should().Be("USD");
    }

    [Fact]
    public void Open_WithNegativeInitialBalance_Fails()
    {
        var r = Account.Open(
            Guid.NewGuid(), Guid.NewGuid(),
            "name", "broker", MarketType.Forex, "USD",
            -1m, 100m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.account.initial_balance_must_be_non_negative");
    }

    [Fact]
    public void Open_WithZeroInitialBalance_Succeeds()
    {
        // Cuentas demo con balance 0 son validas.
        var r = Account.Open(
            Guid.NewGuid(), Guid.NewGuid(),
            "Demo", "broker", MarketType.Forex, "USD",
            0m, 100m, Clock);

        r.IsSuccess.Should().BeTrue();
        r.Value.InitialBalance.Should().Be(0m);
    }

    [Fact]
    public void Open_WithNegativeLeverage_Fails()
    {
        var r = Account.Open(
            Guid.NewGuid(), Guid.NewGuid(),
            "name", "broker", MarketType.Forex, "USD",
            0m, -10m, Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.account.leverage_required_for_forex");
    }
}
