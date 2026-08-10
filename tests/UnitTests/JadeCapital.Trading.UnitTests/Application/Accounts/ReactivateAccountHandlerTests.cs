using NSubstitute.ReturnsExtensions;
using JadeCapital.Trading.Domain.Accounts;

namespace JadeCapital.Trading.UnitTests.Application.Accounts;

public class ReactivateAccountHandlerTests
{
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<ReactivateAccountHandler> _logger = Substitute.For<ILogger<ReactivateAccountHandler>>();

    private ReactivateAccountHandler CreateSut() => new(_accounts, _uow, _clock, _logger);

    private static Account CreateInactiveAccount(Guid userId)
    {
        var c = Substitute.For<IClock>();
        c.UtcNow.Returns(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero));
        var account = Account.Open(
            Guid.NewGuid(), userId,
            "Name", "Broker", "USD",
            1000m, 100m, 0.85m, c).Value;
        account.Deactivate(c);
        return account;
    }

    [Fact]
    public async Task Handle_InactiveAccount_ReactivatesAndReturnsDto()
    {
        var userId = Guid.NewGuid();
        var account = CreateInactiveAccount(userId);
        _accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var result = await CreateSut().Handle(new ReactivateAccountCommand(account.Id, userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeTrue();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyActiveAccount_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var c = Substitute.For<IClock>();
        c.UtcNow.Returns(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero));
        var account = Account.Open(
            Guid.NewGuid(), userId,
            "Name", "Broker", "USD",
            1000m, 100m, 0.85m, c).Value; // ya activo por defecto
        _accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);

        var result = await CreateSut().Handle(new ReactivateAccountCommand(account.Id, userId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.account.already_active");
    }

    [Fact]
    public async Task Handle_ForeignOwner_ReturnsNotFound()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var account = CreateInactiveAccount(owner);
        _accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);

        var result = await CreateSut().Handle(new ReactivateAccountCommand(account.Id, other), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.account");
    }
}
