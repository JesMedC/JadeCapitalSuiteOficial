using JadeCapital.Trading.Domain.Accounts;
using JadeCapital.Trading.Domain.Enums;

namespace JadeCapital.Trading.UnitTests.Accounts;

public class AccountUpdateTests
{
    private static readonly IClock Clock = Substitute.For<IClock>();

    private static Account OpenValidAccount(MarketType marketType = MarketType.Forex, decimal? leverage = 100m)
    {
        Clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        return Account.Open(
            Guid.NewGuid(), Guid.NewGuid(),
            "Original", "IC Markets", marketType, "USD",
            1000m, leverage, Clock).Value;
    }

    [Fact]
    public void UpdateMetadata_WithValidData_UpdatesAndEmitsEvent()
    {
        var account = OpenValidAccount();
        account.ClearDomainEvents();
        var updatedAt = new DateTimeOffset(2026, 6, 2, 9, 0, 0, TimeSpan.Zero);
        Clock.UtcNow.Returns(updatedAt);

        var r = account.UpdateMetadata(
            "Renamed", "Deriv", MarketType.Forex, "EUR",
            200m);

        r.IsSuccess.Should().BeTrue();
        account.Name.Should().Be("Renamed");
        account.Broker.Should().Be("Deriv");
        account.Currency.Should().Be("EUR");
        account.MarketType.Should().Be(MarketType.Forex);
        account.Leverage.Should().Be(200m);
        account.UpdatedAt.Should().NotBeNull();
        account.DomainEvents.Should().ContainSingle(e => e is AccountUpdatedDomainEvent);
        account.DomainEvents.OfType<AccountUpdatedDomainEvent>().Single().MarketType.Should().Be(MarketType.Forex);
    }

    [Fact]
    public void UpdateMetadata_WithInvalidCurrency_Fails()
    {
        var account = OpenValidAccount();
        account.ClearDomainEvents();

        var r = account.UpdateMetadata(
            "Renamed", "Deriv", MarketType.Forex, "XYZ",
            200m);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.account.currency_code_invalid");
        account.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void UpdateMetadata_DoesNotTouchBalances()
    {
        var account = OpenValidAccount();
        var initialBalance = account.InitialBalance;

        account.UpdateMetadata(
            "Renamed", "Deriv", MarketType.Forex, "EUR",
            200m);

        account.InitialBalance.Should().Be(initialBalance);
    }

    [Fact]
    public void UpdateMetadata_FromBinaryToForexWithoutLeverage_Fails()
    {
        var account = OpenValidAccount(MarketType.Binary, leverage: 50m);
        account.ClearDomainEvents();

        var r = account.UpdateMetadata(
            "Renamed", "Deriv", MarketType.Forex, "USD",
            null);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.account.leverage_required_for_forex");
        account.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void UpdateMetadata_FromForexToBinary_KeepsProvidedLeverage()
    {
        var account = OpenValidAccount(MarketType.Forex, leverage: 100m);
        account.ClearDomainEvents();

        var r = account.UpdateMetadata(
            "Renamed", "Deriv", MarketType.Binary, "USD",
            100m);

        r.IsSuccess.Should().BeTrue();
        account.MarketType.Should().Be(MarketType.Binary);
        account.Leverage.Should().Be(100m);
    }

    [Fact]
    public void UpdateMetadata_FromForexToBinaryWithoutLeverage_DefaultsToOne()
    {
        var account = OpenValidAccount(MarketType.Forex, leverage: 100m);
        account.ClearDomainEvents();

        var r = account.UpdateMetadata(
            "Renamed", "Deriv", MarketType.Binary, "USD",
            null);

        r.IsSuccess.Should().BeTrue();
        account.MarketType.Should().Be(MarketType.Binary);
        account.Leverage.Should().Be(1m);
    }

    [Fact]
    public void UpdateMetadata_WithInvalidMarketType_Fails()
    {
        var account = OpenValidAccount();
        account.ClearDomainEvents();

        var r = account.UpdateMetadata(
            "Renamed", "Deriv", (MarketType)999, "USD",
            100m);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("validation.account.invalid_market_type");
    }

    [Fact]
    public void Deactivate_FromActive_DeactivatesAndEmitsEvent()
    {
        var account = OpenValidAccount();
        account.ClearDomainEvents();
        var deactAt = new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero);
        Clock.UtcNow.Returns(deactAt);

        var r = account.Deactivate(Clock);

        r.IsSuccess.Should().BeTrue();
        account.IsActive.Should().BeFalse();
        account.DomainEvents.Should().ContainSingle(e => e is AccountDeactivatedDomainEvent);
    }

    [Fact]
    public void Deactivate_AlreadyInactive_Fails()
    {
        var account = OpenValidAccount();
        account.Deactivate(Clock);

        var r = account.Deactivate(Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("conflict.account.already_inactive");
    }

    [Fact]
    public void Reactivate_FromInactive_ReactivatesAndEmitsEvent()
    {
        var account = OpenValidAccount();
        account.Deactivate(Clock);
        account.ClearDomainEvents();
        var reactAt = new DateTimeOffset(2026, 6, 4, 0, 0, 0, TimeSpan.Zero);
        Clock.UtcNow.Returns(reactAt);

        var r = account.Reactivate(Clock);

        r.IsSuccess.Should().BeTrue();
        account.IsActive.Should().BeTrue();
        account.DomainEvents.Should().ContainSingle(e => e is AccountReactivatedDomainEvent);
    }

    [Fact]
    public void Reactivate_AlreadyActive_Fails()
    {
        var account = OpenValidAccount();

        var r = account.Reactivate(Clock);

        r.IsFailure.Should().BeTrue();
        r.Error.Code.Should().Be("conflict.account.already_active");
    }
}
