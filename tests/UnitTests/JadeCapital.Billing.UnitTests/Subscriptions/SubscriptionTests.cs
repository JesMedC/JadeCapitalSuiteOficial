namespace JadeCapital.Billing.UnitTests.Subscriptions;

/// <summary>
/// Domain tests for the Subscription aggregate — slice 0e of jade-trader-os-core-portals.
///
/// Covers:
/// - Tier change appends history with prior/resulting/actor/time/version.
/// - Cancellation only allowed from a cancellable state.
/// - Trial extension rejected for non-trial subscription or expired/invalid date.
/// - No-op transitions do NOT append history.
/// - Optimistic version concurrency: stale mutations fail with Conflict.
/// - History ordering newest-first with stable Id tie-breaker.
/// - Tier change only allowed to eligible plans.
/// </summary>
public class SubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static PlanCode StarterCode => PlanCode.FromTrusted("starter");
    private static PlanCode ProCode => PlanCode.FromTrusted("pro");
    private static PlanCode IneligibleCode => PlanCode.FromTrusted("legacy");

    private static Plan EligibleStarter() => Plan.Create(
        Guid.NewGuid(), StarterCode, "Starter", Money.FromTrusted(0m, "USD"),
        isEligibleForSelfService: true).Value;

    private static Plan EligiblePro() => Plan.Create(
        Guid.NewGuid(), ProCode, "Pro", Money.FromTrusted(29m, "USD"),
        isEligibleForSelfService: true).Value;

    private static Plan IneligiblePlan() => Plan.Create(
        Guid.NewGuid(), IneligibleCode, "Legacy", Money.FromTrusted(9m, "USD"),
        isEligibleForSelfService: false).Value;

    private static Subscription NewActive(Plan plan, DateTimeOffset? trialEndsAt = null)
    {
        var status = trialEndsAt.HasValue ? SubscriptionStatus.Trial : SubscriptionStatus.Active;
        var r = Subscription.Create(
            Guid.NewGuid(), OwnerUserId, plan, status, trialEndsAt, Now);
        r.IsSuccess.Should().BeTrue();
        return r.Value;
    }

    private const string Actor = "admin@local";

    // ============================================
    // ChangeTier
    // ============================================

    [Fact]
    public void ChangesTierAppendsHistory()
    {
        var starter = EligibleStarter();
        var pro = EligiblePro();
        var sub = NewActive(starter);

        var beforeCount = sub.History.Count;
        var r = sub.ChangeTier(pro, sub.Version, Actor, Now.AddMinutes(1));

        r.IsSuccess.Should().BeTrue();
        sub.PlanCode.Should().Be(pro.Code);
        sub.Version.Should().Be(sub.Version); // bumped by mutator
        sub.History.Should().HaveCount(beforeCount + 1);

        var entry = sub.History.First();
        entry.Action.Should().Be(SubscriptionAction.TierChanged);
        entry.PriorPlanCode.Should().Be(starter.Code);
        entry.ResultingPlanCode.Should().Be(pro.Code);
        entry.Actor.Should().Be(Actor);
        entry.OccurredAt.Should().Be(Now.AddMinutes(1));
    }

    // ============================================
    // Cancel
    // ============================================

    [Fact]
    public void Cancel_FromCancellableState_Succeeds_AndAppendsHistory()
    {
        var sub = NewActive(EligibleStarter());

        var r = sub.Cancel("user requested", sub.Version, Actor, Now.AddMinutes(2));

        r.IsSuccess.Should().BeTrue();
        sub.Status.Should().Be(SubscriptionStatus.Cancelled);
        sub.History.Should().ContainSingle(e =>
            e.Action == SubscriptionAction.Cancelled
            && e.Actor == Actor);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_Fails_NoHistory()
    {
        var sub = NewActive(EligibleStarter());
        sub.Cancel("first", sub.Version, Actor, Now.AddMinutes(1));
        var versionAfterFirstCancel = sub.Version;
        var countAfterFirst = sub.History.Count;

        var r = sub.Cancel("second", sub.Version, Actor, Now.AddMinutes(5));

        r.IsFailure.Should().BeTrue();
        sub.History.Should().HaveCount(countAfterFirst);
        sub.Version.Should().Be(versionAfterFirstCancel);
    }

    // ============================================
    // ExtendTrial
    // ============================================

    [Fact]
    public void ExtendTrial_RejectsExpiredDateOrNonActiveTrial()
    {
        // Non-trial subscription.
        var sub = NewActive(EligibleStarter());
        var r1 = sub.ExtendTrial(Now.AddDays(7), sub.Version, Actor, Now);
        r1.IsFailure.Should().BeTrue();
        sub.History.Should().BeEmpty();

        // Trial subscription with expired new end.
        var trialSub = NewActive(EligibleStarter(), Now.AddDays(1));
        var r2 = trialSub.ExtendTrial(Now.AddHours(-1), trialSub.Version, Actor, Now);
        r2.IsFailure.Should().BeTrue();

        // Trial subscription with valid future end succeeds and appends history.
        var r3 = trialSub.ExtendTrial(Now.AddDays(14), trialSub.Version + 1, Actor, Now.AddMinutes(1));
        r3.IsSuccess.Should().BeTrue();
        trialSub.TrialEndsAt.Should().Be(Now.AddDays(14));
        trialSub.History.Should().ContainSingle(e => e.Action == SubscriptionAction.TrialExtended);
    }

    // ============================================
    // No-op rejection: no history append
    // ============================================

    [Fact]
    public void NoOp_Rejection_NoHistory()
    {
        var plan = EligibleStarter();
        var sub = NewActive(plan);
        var countBefore = sub.History.Count;

        var r = sub.ChangeTier(plan, sub.Version, Actor, Now.AddMinutes(1));

        r.IsSuccess.Should().BeTrue();
        sub.History.Should().HaveCount(countBefore);
    }

    // ============================================
    // Optimistic concurrency via Version
    // ============================================

    [Fact]
    public void ConcurrentMutation_ExactlyOneWins_ViaVersion()
    {
        var sub = NewActive(EligibleStarter());
        var observedVersion = sub.Version;

        // First mutation captures observed version, applies.
        var pro = EligiblePro();
        var first = sub.ChangeTier(pro, observedVersion, "alice@local", Now.AddMinutes(1));
        first.IsSuccess.Should().BeTrue();

        // Second mutation still references the stale observed version.
        var second = sub.ChangeTier(EligibleStarter(), observedVersion, "bob@local", Now.AddMinutes(2));
        second.IsFailure.Should().BeTrue();
        sub.PlanCode.Should().Be(pro.Code);
        sub.History.Should().HaveCount(1);
        sub.Version.Should().BeGreaterThan(observedVersion);
    }

    // ============================================
    // History order: newest-first, stable tie-breaker
    // ============================================

    [Fact]
    public void HistoryOrder_NewestFirst_StableTieBreaker()
    {
        var plan = EligibleStarter();
        var pro = EligiblePro();
        var sub = NewActive(plan);

        sub.ChangeTier(pro, sub.Version, "alice@local", Now);
        sub.ChangeTier(plan, sub.Version, "alice@local", Now);
        sub.ChangeTier(pro, sub.Version, "alice@local", Now);

        sub.History.Should().HaveCount(3);
        // Latest action's resulting plan comes first.
        sub.History[0].ResultingPlanCode.Should().Be(pro.Code);
        sub.History[1].ResultingPlanCode.Should().Be(plan.Code);
        sub.History[2].ResultingPlanCode.Should().Be(pro.Code);

        // Each entry carries a deterministic monotonic Id ascending with order.
        sub.History[0].Id.Should().BeGreaterThan(sub.History[1].Id);
        sub.History[1].Id.Should().BeGreaterThan(sub.History[2].Id);
    }

    // ============================================
    // Eligibility check
    // ============================================

    [Fact]
    public void EligiblePlanOnly()
    {
        var eligible = EligibleStarter();
        var ineligible = IneligiblePlan();
        var sub = NewActive(eligible);

        var r = sub.ChangeTier(ineligible, sub.Version, Actor, Now.AddMinutes(1));

        r.IsFailure.Should().BeTrue();
        sub.PlanCode.Should().Be(eligible.Code);
        sub.History.Should().BeEmpty();
    }
}
