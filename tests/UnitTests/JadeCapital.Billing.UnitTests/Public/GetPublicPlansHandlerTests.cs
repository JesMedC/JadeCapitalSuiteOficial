using JadeCapital.Billing.Application.Features.Subscriptions;
using JadeCapital.Billing.PublicApi.Contracts;
using JadeCapital.Billing.PublicApi.Services;

namespace JadeCapital.Billing.UnitTests.Public;

/// <summary>
/// Wave-1.3 — application-layer unit tests for <see cref="GetPublicPlansHandler"/>.
///
/// Contract verified:
/// <list type="bullet">
///   <item>Delegates the <c>is_eligible_for_self_service AND !is_deprecated</c>
///   filter to <see cref="IPlanLookup.ListEligibleForSelfServiceAsync"/> (no
///   in-memory post-filtering — the EF query owns that contract).</item>
///   <item>Projects each <see cref="Plan"/> onto <see cref="PlanInfo"/>:
///   <c>Code</c>, <c>Name</c>, <c>MonthlyPrice</c> (decimal), <c>Currency</c>,
///   <c>IsEligibleForSelfService</c>.</item>
///   <item>Forwards the lookup result 1:1 — ordering &amp; filtering is the
///   repository's responsibility, not the handler's.</item>
/// </list>
/// </summary>
public class GetPublicPlansHandlerTests
{
    [Fact]
    public async Task Handler_DelegatesFilterToLookup_DoesNotPostFilterInMemory()
    {
        var lookup = Substitute.For<IPlanLookup>();
        var eligible = MakePlan("elite", "Elite", 99.99m, eligible: true, deprecated: false);
        var notEligible = MakePlan("legacy", "Legacy", 49.99m, eligible: false, deprecated: false);
        var deprecated = MakePlan("retired", "Retired", 19.99m, eligible: true, deprecated: true);
        // The mocked lookup returns ALL plans (no EF where-clause). The handler
        // MUST forward whatever it gets — flagging this as a contract test:
        // filtering belongs to the repository, not to the handler.
        lookup.ListEligibleForSelfServiceAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { eligible, notEligible, deprecated });

        var sut = new GetPublicPlansHandler(lookup);
        var result = await sut.Handle(new GetPublicPlansQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        await lookup.Received(1).ListEligibleForSelfServiceAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handler_ProjectsDomainFieldsToPublicDto_FlatMapping()
    {
        var lookup = Substitute.For<IPlanLookup>();
        var plan = MakePlan("pro", "Pro", 29.99m, eligible: true, deprecated: false);
        lookup.ListEligibleForSelfServiceAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { plan });

        var sut = new GetPublicPlansHandler(lookup);
        var result = await sut.Handle(new GetPublicPlansQuery(), CancellationToken.None);

        var dto = result.Value!.Single();
        dto.Code.Should().Be("pro");
        dto.Name.Should().Be("Pro");
        dto.MonthlyPrice.Should().Be(29.99m);
        dto.Currency.Should().Be("USD");
        dto.IsEligibleForSelfService.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_ForwardsEligibilityFlagAsReportedByDomain()
    {
        var lookup = Substitute.For<IPlanLookup>();
        var plan = MakePlan("starter", "Starter", 9.99m, eligible: false, deprecated: false);
        lookup.ListEligibleForSelfServiceAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { plan });

        var sut = new GetPublicPlansHandler(lookup);
        var result = await sut.Handle(new GetPublicPlansQuery(), CancellationToken.None);

        var dto = result.Value!.Single();
        dto.IsEligibleForSelfService.Should().BeFalse();
        dto.MonthlyPrice.Should().Be(9.99m);
    }

    private static Plan MakePlan(
        string code, string name, decimal price,
        bool eligible, bool deprecated)
        => Plan.FromTrusted(
            id: Guid.NewGuid(),
            code: PlanCode.FromTrusted(code),
            name: name,
            monthlyPrice: Money.FromTrusted(price, "USD"),
            isEligibleForSelfService: eligible,
            isDeprecated: deprecated);
}
