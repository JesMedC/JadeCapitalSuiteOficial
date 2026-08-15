using JadeCapital.Billing.Application.Features.Subscriptions;

namespace JadeCapital.Billing.UnitTests.Features.Subscriptions;

/// <summary>
/// Slice 0f.1 — RED test that asserts the ChangeTier application handler
/// surfaces a Conflict when the observed version does not match the
/// aggregate's current version. Domain invariants live in the aggregate; the
/// handler's job is to look up, hand off to the mutator, persist, and surface
/// the result.
/// </summary>
public class ChangeTierHandlerTests
{
    [Fact]
    public async Task ChangeTier_RequiresVersion_ReturnsConflictWhenStale()
    {
        var repo = Substitute.For<ISubscriptionAdminRepository>();
        var plans = Substitute.For<IPlanLookup>();
        var uow = Substitute.For<ISubscriptionAdminUnitOfWork>();
        var clock = Substitute.For<IClock>();
        var now = DateTimeOffset.UnixEpoch;
        clock.UtcNow.Returns(now);
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());

        var sub = BuildActiveSubscription(version: 5);
        var newPlan = BuildPlan("pro");
        repo.LoadForUpdateAsync(sub.Id, Arg.Any<CancellationToken>()).Returns(sub);
        plans.FindByCodeAsync("pro", Arg.Any<CancellationToken>()).Returns(newPlan);

        var sut = new ChangeTierHandler(repo, plans, uow, clock);
        // observedVersion=3 < aggregate version=5 ⇒ stale → Conflict.
        var result = await sut.Handle(
            new ChangeTierCommand(sub.Id, "pro", ObservedVersion: 3, Actor: "admin@local"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.subscription.version_conflict");
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeTier_RequiresVersion_SucceedsWhenVersionMatches()
    {
        var repo = Substitute.For<ISubscriptionAdminRepository>();
        var plans = Substitute.For<IPlanLookup>();
        var uow = Substitute.For<ISubscriptionAdminUnitOfWork>();
        var clock = Substitute.For<IClock>();
        var now = DateTimeOffset.UnixEpoch;
        clock.UtcNow.Returns(now);
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());

        var sub = BuildActiveSubscription(version: 5);
        var newPlan = BuildPlan("pro");
        repo.LoadForUpdateAsync(sub.Id, Arg.Any<CancellationToken>()).Returns(sub);
        plans.FindByCodeAsync("pro", Arg.Any<CancellationToken>()).Returns(newPlan);

        var sut = new ChangeTierHandler(repo, plans, uow, clock);
        var result = await sut.Handle(
            new ChangeTierCommand(sub.Id, "pro", ObservedVersion: 5, Actor: "admin@local"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sub.PlanCode.Value.Should().Be("pro");
        sub.Version.Should().Be(6);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeTier_Handler_CallsUoWAddHistoryEntry()
    {
        var repo = Substitute.For<ISubscriptionAdminRepository>();
        var plans = Substitute.For<IPlanLookup>();
        var uow = Substitute.For<ISubscriptionAdminUnitOfWork>();
        var clock = Substitute.For<IClock>();
        var now = DateTimeOffset.UnixEpoch;
        clock.UtcNow.Returns(now);
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());

        var sub = BuildActiveSubscription(version: 5);
        var newPlan = BuildPlan("pro");
        repo.LoadForUpdateAsync(sub.Id, Arg.Any<CancellationToken>()).Returns(sub);
        plans.FindByCodeAsync("pro", Arg.Any<CancellationToken>()).Returns(newPlan);

        var sut = new ChangeTierHandler(repo, plans, uow, clock);
        await sut.Handle(
            new ChangeTierCommand(sub.Id, "pro", ObservedVersion: 5, Actor: "admin@local"),
            CancellationToken.None);

        uow.Received(1).AddHistoryEntry(Arg.Is<SubscriptionHistoryEntry>(e =>
            e.Action == SubscriptionAction.TierChanged && e.Actor == "admin@local"));
    }

    private static Subscription BuildActiveSubscription(int version)
    {
        var plan = Plan.Create(
            Guid.NewGuid(), PlanCode.FromTrusted("starter"), "Starter",
            Money.FromTrusted(0m, "USD"), isEligibleForSelfService: true).Value;
        var r = Subscription.Create(Guid.NewGuid(), Guid.NewGuid(), plan,
            SubscriptionStatus.Active, trialEndsAt: null, DateTimeOffset.UnixEpoch);
        r.IsSuccess.Should().BeTrue();
        // Force the version to the test value via the aggregate's own mutator.
        // Each bump alternates between starter and pro so the no-op guard does
        // not silently short-circuit the version increment.
        var alt = Plan.Create(Guid.NewGuid(), PlanCode.FromTrusted("pro"), "Pro",
            Money.FromTrusted(0m, "USD"), isEligibleForSelfService: true).Value;
        for (var i = 1; i < version; i++)
        {
            var target = (i % 2 == 0) ? plan : alt;
            var bump = r.Value.ChangeTier(target, r.Value.Version, "setup@test", DateTimeOffset.UnixEpoch);
            bump.IsSuccess.Should().BeTrue();
        }
        return r.Value;
    }

    private static Plan BuildPlan(string code)
        => Plan.Create(Guid.NewGuid(), PlanCode.FromTrusted(code), code, Money.FromTrusted(0m, "USD"),
            isEligibleForSelfService: true).Value;
}
