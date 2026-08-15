using JadeCapital.Billing.Application.Features.Subscriptions;

namespace JadeCapital.Billing.UnitTests.Features.Subscriptions;

/// <summary>
/// Slice 0f.1 — application-layer RED tests for the Subscription admin handlers
/// (list/search, change-tier, cancel, extend-trial).
///
/// Each test asserts the application's contract surface (commands, queries,
/// DTOs, and the handlers' success/failure shapes) so the GREEN step wires
/// concrete handlers that satisfy them. Domain rules are NOT retested here —
/// those live in 0e's SubscriptionTests; this layer exercises the application
/// boundary (mediator contracts, repositories, version match, actor capture).
/// </summary>
public class ListSubscriptionsHandlerTests
{
    [Fact]
    public async Task ListSubscriptions_PagedByQuery_FiltersByStatus_AndReturnsPaged()
    {
        // The handler MUST accept a query with a status filter and pagination
        // bounds, and return a paged DTO whose items come from the repository.
        // No "all" status (slice 0f is admin-only; status filter is required).
        var repo = Substitute.For<ISubscriptionAdminRepository>();
        var plans = Substitute.For<IPlanLookup>();
        var now = DateTimeOffset.UnixEpoch;
        var items = new List<SubscriptionListItem>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "starter", "Active", now, 1),
            new(Guid.NewGuid(), Guid.NewGuid(), "pro", "Active", now, 2)
        };
        repo.ListPagedAsync("active", 1, 10, Arg.Any<CancellationToken>())
            .Returns(new PagedSubscriptions(items, Page: 1, PageSize: 10, Total: 2));

        var sut = new ListSubscriptionsHandler(repo, plans);
        var result = await sut.Handle(
            new ListSubscriptionsQuery(Status: "active", Page: 1, PageSize: 10),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(10);
        result.Value.Total.Should().Be(2);
        await repo.Received(1).ListPagedAsync("active", 1, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptions_PagedByQuery_PassesPageAndSizeToRepository()
    {
        var repo = Substitute.For<ISubscriptionAdminRepository>();
        var plans = Substitute.For<IPlanLookup>();
        repo.ListPagedAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedSubscriptions(Array.Empty<SubscriptionListItem>(), 2, 5, 0));

        var sut = new ListSubscriptionsHandler(repo, plans);
        await sut.Handle(new ListSubscriptionsQuery("cancelled", Page: 2, PageSize: 5), CancellationToken.None);

        await repo.Received(1).ListPagedAsync("cancelled", 2, 5, Arg.Any<CancellationToken>());
    }
}
