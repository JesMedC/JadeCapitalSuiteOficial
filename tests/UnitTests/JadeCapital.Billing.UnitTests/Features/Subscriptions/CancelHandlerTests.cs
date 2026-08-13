using JadeCapital.Billing.Application.Features.Subscriptions;

namespace JadeCapital.Billing.UnitTests.Features.Subscriptions;

/// <summary>
/// Slice 0f.1 — RED tests for the Cancel handler. Two contract checks:
/// (1) the cancellation history entry MUST record the actor + the moment the
/// commit happened (the design.md invariant), and
/// (2) the handler refuses to commit when the version is stale.
/// </summary>
public class CancelHandlerTests
{
    [Fact]
    public async Task Cancel_RecordsActorAndTimestamp_InNewestHistoryEntry()
    {
        var repo = Substitute.For<ISubscriptionAdminRepository>();
        var uow = Substitute.For<ISubscriptionAdminUnitOfWork>();
        var clock = Substitute.For<IClock>();
        var actor = "admin@local";
        var commitAt = new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero);
        clock.UtcNow.Returns(commitAt);
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());

        var sub = BuildActiveSubscription();
        repo.LoadForUpdateAsync(sub.Id, Arg.Any<CancellationToken>()).Returns(sub);

        var sut = new CancelHandler(repo, uow, clock);
        var result = await sut.Handle(
            new CancelCommand(sub.Id, "user requested", sub.Version, actor),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sub.Status.Should().Be(SubscriptionStatus.Cancelled);
        sub.History.Should().HaveCount(1);
        var entry = sub.History[0];
        entry.Action.Should().Be(SubscriptionAction.Cancelled);
        entry.Actor.Should().Be(actor, "actor MUST be captured into the history entry");
        entry.OccurredAt.Should().Be(commitAt, "commit time MUST be captured into the history entry");
    }

    [Fact]
    public async Task Cancel_ReturnsConflict_WhenVersionStale()
    {
        var repo = Substitute.For<ISubscriptionAdminRepository>();
        var uow = Substitute.For<ISubscriptionAdminUnitOfWork>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());

        var sub = BuildActiveSubscription();
        repo.LoadForUpdateAsync(sub.Id, Arg.Any<CancellationToken>()).Returns(sub);

        var sut = new CancelHandler(repo, uow, clock);
        var result = await sut.Handle(
            new CancelCommand(sub.Id, "user requested", ObservedVersion: sub.Version - 1, "admin@local"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.subscription.version_conflict");
        sub.Status.Should().Be(SubscriptionStatus.Active, "stale mutations MUST NOT mutate state");
    }

    private static Subscription BuildActiveSubscription()
    {
        var plan = Plan.Create(
            Guid.NewGuid(), PlanCode.FromTrusted("starter"), "Starter",
            Money.FromTrusted(0m, "USD"), isEligibleForSelfService: true).Value;
        var r = Subscription.Create(Guid.NewGuid(), Guid.NewGuid(), plan,
            SubscriptionStatus.Active, trialEndsAt: null, DateTimeOffset.UnixEpoch);
        r.IsSuccess.Should().BeTrue();
        return r.Value;
    }
}
