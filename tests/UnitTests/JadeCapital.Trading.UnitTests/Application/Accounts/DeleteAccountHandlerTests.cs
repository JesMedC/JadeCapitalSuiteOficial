using NSubstitute.ReturnsExtensions;
using JadeCapital.Trading.Domain.Accounts;
using JadeCapital.Trading.Domain.Enums;

namespace JadeCapital.Trading.UnitTests.Application.Accounts;

public class DeleteAccountHandlerTests
{
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<DeleteAccountHandler> _logger = Substitute.For<ILogger<DeleteAccountHandler>>();

    private DeleteAccountHandler CreateSut() => new(_accounts, _trades, _uow, _logger);

    private static Account CreateActiveAccount(Guid userId)
    {
        var c = Substitute.For<IClock>();
        c.UtcNow.Returns(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero));
        return Account.Open(
            Guid.NewGuid(), userId,
            "Name", "Broker", MarketType.Forex, "USD",
            1000m, 100m, c).Value;
    }

    [Fact]
    public async Task Handle_NoTrades_DeletesAccount()
    {
        var userId = Guid.NewGuid();
        var account = CreateActiveAccount(userId);
        _accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        _trades.CountByUserIdAsync(userId, Arg.Any<CancellationToken>(),
                Arg.Any<TradeStatus?>(), Arg.Any<string?>(), account.Id)
            .Returns(0);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var result = await CreateSut().Handle(new DeleteAccountCommand(account.Id, userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _accounts.Received(1).DeleteAsync(account, Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_HasTrades_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var account = CreateActiveAccount(userId);
        _accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        _trades.CountByUserIdAsync(userId, Arg.Any<CancellationToken>(),
                Arg.Any<TradeStatus?>(), Arg.Any<string?>(), account.Id)
            .Returns(3);

        var result = await CreateSut().Handle(new DeleteAccountCommand(account.Id, userId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.account.has_trades");
        await _accounts.DidNotReceive().DeleteAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AccountNotFound_ReturnsNotFound()
    {
        _accounts.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var result = await CreateSut().Handle(new DeleteAccountCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.account");
    }

    [Fact]
    public async Task Handle_ForeignOwner_ReturnsNotFound()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var account = CreateActiveAccount(owner);
        _accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);

        var result = await CreateSut().Handle(new DeleteAccountCommand(account.Id, other), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.account");
    }
}
