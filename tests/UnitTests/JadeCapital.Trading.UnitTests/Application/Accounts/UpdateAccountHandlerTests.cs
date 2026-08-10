using NSubstitute.ReturnsExtensions;
using JadeCapital.Trading.Domain.Accounts;

namespace JadeCapital.Trading.UnitTests.Application.Accounts;

public class UpdateAccountHandlerTests
{
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<UpdateAccountHandler> _logger = Substitute.For<ILogger<UpdateAccountHandler>>();

    private UpdateAccountHandler CreateSut() => new(_accounts, _uow, _logger);

    private static Account CreateActiveAccount(Guid userId)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero));
        return Account.Open(
            Guid.NewGuid(), userId,
            "Original Name", "Original Broker", "USD",
            1000m, 100m, 0.85m, clock).Value;
    }

    private static UpdateAccountCommand ValidCommand(Guid accountId, Guid userId) => new(
        AccountId: accountId,
        UserId: userId,
        Name: "Renamed",
        Broker: "Renamed Broker",
        Currency: "EUR",
        Leverage: 200m,
        PayoutPercent: 0.90m);

    [Fact]
    public async Task Handle_OwnAccount_UpdatesAndReturnsDto()
    {
        var userId = Guid.NewGuid();
        var account = CreateActiveAccount(userId);
        _accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand(account.Id, userId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Renamed");
        result.Value.Broker.Should().Be("Renamed Broker");
        result.Value.Currency.Should().Be("EUR");
        result.Value.Leverage.Should().Be(200m);
        result.Value.PayoutPercent.Should().Be(0.90m);

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForeignOwner_ReturnsNotFound()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var account = CreateActiveAccount(owner);
        _accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);

        var cmd = ValidCommand(account.Id, other);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.account");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AccountNotFound_ReturnsNotFound()
    {
        _accounts.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var cmd = ValidCommand(Guid.NewGuid(), Guid.NewGuid());
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.account");
    }

    [Fact]
    public async Task Handle_InvalidCurrency_ReturnsValidationFailure()
    {
        var userId = Guid.NewGuid();
        var account = CreateActiveAccount(userId);
        _accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);

        var cmd = ValidCommand(account.Id, userId) with { Currency = "USDA" };
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
