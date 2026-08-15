using JadeCapital.Billing.Application.Features.Subscriptions;

namespace JadeCapital.Billing.UnitTests.Features.Subscriptions;

/// <summary>
/// Slice 0f.1 — RED test for the ExtendTrial handler. The domain rule is
/// enforced by the Subscription aggregate (see 0e), so the application layer
/// must surface a Conflict when the subscription is NOT in Trial status.
/// </summary>
public class ExtendTrialHandlerTests
{
    [Fact]
    public async Task ExtendTrial_RejectsIfNotActiveTrial_ReturnsConflict()
    {
        var repo = Substitute.For<ISubscriptionAdminRepository>();
        var uow = Substitute.For<ISubscriptionAdminUnitOfWork>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());

        var sub = BuildActiveSubscription();
        repo.LoadForUpdateAsync(sub.Id, Arg.Any<CancellationToken>()).Returns(sub);

        var sut = new ExtendTrialHandler(repo, uow, clock);
        var result = await sut.Handle(
            new ExtendTrialCommand(sub.Id, DateTimeOffset.UnixEpoch.AddDays(7), sub.Version, "admin@local"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.subscription.not_in_trial");
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtendTrial_OnTrialSubscription_SucceedsAndAppendsHistory()
    {
        var repo = Substitute.For<ISubscriptionAdminRepository>();
        var uow = Substitute.For<ISubscriptionAdminUnitOfWork>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());

        var sub = BuildTrialSubscription(DateTimeOffset.UnixEpoch.AddDays(3));
        repo.LoadForUpdateAsync(sub.Id, Arg.Any<CancellationToken>()).Returns(sub);

        var sut = new ExtendTrialHandler(repo, uow, clock);
        var result = await sut.Handle(
            new ExtendTrialCommand(sub.Id, DateTimeOffset.UnixEpoch.AddDays(14), sub.Version, "admin@local"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sub.TrialEndsAt.Should().Be(DateTimeOffset.UnixEpoch.AddDays(14));
        sub.History.Should().ContainSingle(e => e.Action == SubscriptionAction.TrialExtended);
    }

    [Fact]
    public async Task ExtendTrial_Handler_CallsUoWAddHistoryEntry()
    {
        var repo = Substitute.For<ISubscriptionAdminRepository>();
        var uow = Substitute.For<ISubscriptionAdminUnitOfWork>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success());

        var sub = BuildTrialSubscription(DateTimeOffset.UnixEpoch.AddDays(3));
        repo.LoadForUpdateAsync(sub.Id, Arg.Any<CancellationToken>()).Returns(sub);

        var sut = new ExtendTrialHandler(repo, uow, clock);
        await sut.Handle(
            new ExtendTrialCommand(sub.Id, DateTimeOffset.UnixEpoch.AddDays(14), sub.Version, "admin@local"),
            CancellationToken.None);

        uow.Received(1).AddHistoryEntry(Arg.Is<SubscriptionHistoryEntry>(e =>
            e.Action == SubscriptionAction.TrialExtended && e.Actor == "admin@local"));
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

    private static Subscription BuildTrialSubscription(DateTimeOffset trialEndsAt)
    {
        var plan = Plan.Create(
            Guid.NewGuid(), PlanCode.FromTrusted("starter"), "Starter",
            Money.FromTrusted(0m, "USD"), isEligibleForSelfService: true).Value;
        var r = Subscription.Create(Guid.NewGuid(), Guid.NewGuid(), plan,
            SubscriptionStatus.Trial, trialEndsAt, DateTimeOffset.UnixEpoch);
        r.IsSuccess.Should().BeTrue();
        return r.Value;
    }
}
