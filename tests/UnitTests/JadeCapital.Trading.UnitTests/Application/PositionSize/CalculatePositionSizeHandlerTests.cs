using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Trading.Application.Features.PositionSize;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.UnitTests.Application.PositionSize;

// ============================================================================
//  CalculatePositionSizeHandler — slice 1b (RED tests).
//
//  The handler resolves the user's active risk profile via the cross-module
//  projection `IIdentityUserRiskProfileReader` (slice 1a.1b), runs the pure
//  `PositionSizeCalculator`, and maps the result to a `PositionSizeDto`. NO
//  persistence (the calculator is informational per the spec).
//
//  Scenarios covered:
//    1. Valid inputs                              → 200 with PositionSizeDto
//    2. Override above profile                    → uses override, dto reflects
//    3. Currency mismatch (req.USD vs profile.EUR) → 422 currency_mismatch
//    4. No active profile (reader returns null)   → 404 no_active_profile
//    5. Missing override + no profile             → 404 (defensive — same path as 4)
// ============================================================================

public class CalculatePositionSizeHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static CalculatePositionSizeQuery ValidQuery(decimal? riskOverride = null)
        => new(
            UserId: UserId,
            StopLossDistance: 0.0050m,
            RiskPerTradeOverride: riskOverride,
            Currency: "USD");

    private static UserRiskProfileSnapshot ActiveProfile(
        decimal capital = 10_000m,
        string capitalCurrency = "USD",
        decimal riskPerTradePercent = 1.0m)
        => new(capital, capitalCurrency, riskPerTradePercent, RiskRewardTarget: 2.0m);

    private static CalculatePositionSizeHandler BuildHandler(IIdentityUserRiskProfileReader reader)
    {
        var logger = Substitute.For<ILogger<CalculatePositionSizeHandler>>();
        return new CalculatePositionSizeHandler(reader, logger);
    }

    // ===== Happy path =====

    [Fact]
    public async Task Handle_ValidInputs_ReturnsPositionSizeDto()
    {
        var reader = Substitute.For<IIdentityUserRiskProfileReader>();
        reader.GetActiveAsync(UserId, Arg.Any<CancellationToken>())
              .Returns(ActiveProfile(capital: 10_000m, riskPerTradePercent: 1.0m));

        var handler = BuildHandler(reader);
        var query = ValidQuery();

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Volume.Should().Be(20_000m);
        result.Value.RiskAmount.Should().Be(100m);
        result.Value.RiskPerTradePercent.Should().Be(1.0m);
        result.Value.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Handle_OverrideAboveProfile_UsesOverride()
    {
        var reader = Substitute.For<IIdentityUserRiskProfileReader>();
        reader.GetActiveAsync(UserId, Arg.Any<CancellationToken>())
              .Returns(ActiveProfile(riskPerTradePercent: 1.0m));

        var handler = BuildHandler(reader);
        var query = ValidQuery(riskOverride: 3.0m);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RiskPerTradePercent.Should().Be(3.0m);
        result.Value.RiskAmount.Should().Be(300m);
        result.Value.Volume.Should().Be(60_000m);
    }

    // ===== Validation =====

    [Fact]
    public async Task Handle_CurrencyMismatch_ReturnsFailureWithCurrencyMismatchCode()
    {
        // Request says USD, profile capital is EUR → cannot divide.
        var reader = Substitute.For<IIdentityUserRiskProfileReader>();
        reader.GetActiveAsync(UserId, Arg.Any<CancellationToken>())
              .Returns(ActiveProfile(capitalCurrency: "EUR"));

        var handler = BuildHandler(reader);
        var query = ValidQuery();

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.position_size.currency_mismatch");
    }

    // ===== NotFound =====

    [Fact]
    public async Task Handle_NoActiveProfile_ReturnsFailureWithNotFoundCode()
    {
        var reader = Substitute.For<IIdentityUserRiskProfileReader>();
        reader.GetActiveAsync(UserId, Arg.Any<CancellationToken>())
              .Returns((UserRiskProfileSnapshot?)null);

        var handler = BuildHandler(reader);
        var query = ValidQuery();

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.risk_profile.no_active_profile");
    }

    [Fact]
    public async Task Handle_MissingOverrideAndMissingProfile_StillReturnsNotFound()
    {
        // Same code path as "no profile" — covered explicitly because the
        // override-null branch must not accidentally short-circuit the reader.
        var reader = Substitute.For<IIdentityUserRiskProfileReader>();
        reader.GetActiveAsync(UserId, Arg.Any<CancellationToken>())
              .Returns((UserRiskProfileSnapshot?)null);

        var handler = BuildHandler(reader);
        var query = new CalculatePositionSizeQuery(
            UserId: UserId,
            StopLossDistance: 0.0050m,
            RiskPerTradeOverride: null,
            Currency: "USD");

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.risk_profile.no_active_profile");
    }
}
