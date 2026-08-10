using JadeCapital.Trading.Domain.Accounts;

namespace JadeCapital.Trading.UnitTests.Accounts;

public class AccountUpdateTests
{
    private static readonly IClock Clock = Substitute.For<IClock>();

    private static Account OpenValidAccount()
    {
        Clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        return Account.Open(
            Guid.NewGuid(), Guid.NewGuid(),
            "Original", "IC Markets", "USD",
            1000m, 100m, 0.85m, Clock).Value;
    }

    [Fact]
    public void UpdateMetadata_WithValidData_UpdatesAndEmitsEvent()
    {
        var account = OpenValidAccount();
        account.ClearDomainEvents();
        var updatedAt = new DateTimeOffset(2026, 6, 2, 9, 0, 0, TimeSpan.Zero);
        Clock.UtcNow.Returns(updatedAt);

        var r = account.UpdateMetadata(
            "Renamed", "Deriv", "EUR",
            200m, 0.90m);

        r.IsSuccess.Should().BeTrue();
        account.Name.Should().Be("Renamed");
        account.Broker.Should().Be("Deriv");
        account.Currency.Should().Be("EUR");
        account.Leverage.Should().Be(200m);
        account.PayoutPercent.Should().Be(0.90m);
        account.UpdatedAt.Should().NotBeNull();
        account.DomainEvents.Should().ContainSingle(e => e is AccountUpdatedDomainEvent);
    }

    [Fact]
    public void UpdateMetadata_WithInvalidCurrency_Fails()
    {
        var account = OpenValidAccount();
        account.ClearDomainEvents();

        var r = account.UpdateMetadata(
            "Renamed", "Deriv", "XYZ",
            200m, 0.90m);

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
            "Renamed", "Deriv", "EUR",
            200m, 0.90m);

        account.InitialBalance.Should().Be(initialBalance);
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
