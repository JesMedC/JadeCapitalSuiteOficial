using NSubstitute.ReturnsExtensions;
using JadeCapital.Trading.Domain.Accounts;

namespace JadeCapital.Trading.UnitTests.Application.Accounts;

public class GetAccountsHandlerTests
{
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();

    private GetAccountsHandler CreateSut() => new(_accounts);

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
    public async Task Handle_ReturnsMappedDtosForUser()
    {
        var userId = Guid.NewGuid();
        var list = new List<Account>
        {
            CreateActiveAccount(userId),
            CreateActiveAccount(userId),
            CreateActiveAccount(userId)
        };
        _accounts.ListByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns(list);

        var result = await CreateSut().Handle(new GetAccountsQuery(userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        await _accounts.Received(1).ListByUserIdAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoAccounts_ReturnsEmptyList()
    {
        var userId = Guid.NewGuid();
        _accounts.ListByUserIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new List<Account>());

        var result = await CreateSut().Handle(new GetAccountsQuery(userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
