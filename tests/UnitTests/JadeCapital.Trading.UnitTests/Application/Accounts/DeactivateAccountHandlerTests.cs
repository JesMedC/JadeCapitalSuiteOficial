using NSubstitute.ReturnsExtensions;
using JadeCapital.Trading.Domain.Accounts;
using JadeCapital.Trading.Domain.Enums;

namespace JadeCapital.Trading.UnitTests.Application.Accounts;

public class DeactivateAccountHandlerTests
{
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<DeactivateAccountHandler> _logger = Substitute.For<ILogger<DeactivateAccountHandler>>();

    private DeactivateAccountHandler CreateSut() => new(_accounts, _uow, _clock, _logger);

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
    public async Task Handle_ActiveAccount_DeactivatesAndReturnsDto()
    {
        var userId = Guid.NewGuid();
        var account = CreateActiveAccount(userId);
        _accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var result = await CreateSut().Handle(new DeactivateAccountCommand(account.Id, userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeFalse();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyInactiveAccount_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var account = CreateActiveAccount(userId);
        account.Deactivate(_clock);
        _accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);

        var result = await CreateSut().Handle(new DeactivateAccountCommand(account.Id, userId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.account.already_inactive");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForeignOwner_ReturnsNotFound()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var account = CreateActiveAccount(owner);
        _accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);

        var result = await CreateSut().Handle(new DeactivateAccountCommand(account.Id, other), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.account");
    }
}
