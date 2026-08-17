using JadeCapital.Identity.Application.Features.RiskProfileActions.GetActiveRiskProfile;
using JadeCapital.Identity.Domain.RiskProfile;
using JadeCapital.Shared.Kernel.Money;

namespace JadeCapital.Identity.UnitTests.Features.RiskProfileActions;

/// <summary>
/// Tests del query handler <c>GetActiveRiskProfileHandler</c> — slice 1a.1a.
///
/// Cobertura (2 escenarios del spec):
/// <list type="number">
///   <item><b>Existing active profile</b>: el repo devuelve un profile;
///   el handler proyecta a DTO preservando todos los campos.</item>
///   <item><b>No active profile</b>: el repo devuelve null; el handler
///   returns 404 (NotFound error).</item>
/// </list>
/// </summary>
public class GetActiveRiskProfileQueryTests
{
    private readonly IRiskProfileRepository _repo = Substitute.For<IRiskProfileRepository>();
    private readonly ILogger<GetActiveRiskProfileHandler> _logger = Substitute.For<ILogger<GetActiveRiskProfileHandler>>();

    private GetActiveRiskProfileHandler CreateSut() => new(_repo, _logger);

    [Fact]
    public async Task Handle_ExistingActiveProfile_ProjectsToDto()
    {
        var userId = Guid.NewGuid();
        var profile = RiskProfile.Create(
            Guid.NewGuid(), userId,
            Money.FromTrusted(10_000m, "USD"),
            MaxDrawdownPercent.Create(20m).Value,
            RiskPerTradePercent.Create(1m).Value,
            RiskRewardRatio.Create(2m).Value,
            Substitute.For<JadeCapital.Shared.Kernel.Time.IClock>()).Value;

        _repo.GetActiveAsync(userId, Arg.Any<CancellationToken>()).Returns(profile);

        var result = await CreateSut().Handle(new GetActiveRiskProfileQuery(userId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(userId);
        result.Value.CapitalAmount.Should().Be(10_000m);
        result.Value.CapitalCurrency.Should().Be("USD");
        result.Value.MaxDrawdownPercent.Should().Be(20m);
        result.Value.RiskPerTradePercent.Should().Be(1m);
        result.Value.RiskRewardTarget.Should().Be(2m);
        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NoActiveProfile_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        _repo.GetActiveAsync(userId, Arg.Any<CancellationToken>()).Returns((RiskProfile?)null);

        var result = await CreateSut().Handle(new GetActiveRiskProfileQuery(userId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.risk_profile.not_found");
    }
}
