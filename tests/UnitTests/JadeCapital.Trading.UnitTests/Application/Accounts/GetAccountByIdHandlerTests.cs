using NSubstitute.ReturnsExtensions;
using JadeCapital.Trading.Domain.Accounts;

namespace JadeCapital.Trading.UnitTests.Application.Accounts;

public class GetAccountByIdHandlerTests
{
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();

    private GetAccountByIdHandler CreateSut() => new(_accounts);

    private static Account CreateActiveAccount(Guid userId)
    {
        var c = Substitute.For<IClock>();
        c.UtcNow.Returns(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero));
        return Account.Open(
            Guid.NewGuid(), userId,
            "Name", "Broker", "USD",
            1000m, 100m, 0.85m, c).Value;
    }

    [Fact]
    public async Task Handle_OwnAccount_ReturnsDto()
    {
        var userId = Guid.NewGuid();
        var account = CreateActiveAccount(userId);
        _accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);

        var result = await CreateSut().Handle(new GetAccountByIdQuery(account.Id, userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(account.Id);
    }

    [Fact]
    public async Task Handle_ForeignOwner_ReturnsNotFound()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var account = CreateActiveAccount(owner);
        _accounts.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);

        var result = await CreateSut().Handle(new GetAccountByIdQuery(account.Id, other), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.account");
    }

    [Fact]
    public async Task Handle_MissingAccount_ReturnsNotFound()
    {
        _accounts.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var result = await CreateSut().Handle(new GetAccountByIdQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.account");
    }
}
