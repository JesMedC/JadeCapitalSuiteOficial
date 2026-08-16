using JadeCapital.Trading.Domain.PositionSize;

namespace JadeCapital.Trading.UnitTests.Domain.PositionSize;

// ============================================================================
//  PositionSizeCalculator — slice 1b (pure-math RED tests).
//
//  The calculator is a static pure function: Money + risk% + stop distance ->
//  Result<PositionSizeCalculation>. No EF, no DB, no I/O — pure decimal math
//  so it can be reused by the API handler, future background jobs, etc.
//
//  Scenarios covered (8):
//    1. Valid calculation:           capital=10000, risk=1%,  stop=0.0050 → volume=20000
//    2. Override vs profile:         capital=10000, profile=1%, override=2% → uses override
//    3. Zero stop loss rejection:    stop=0 → Result.Failure(invalid_stop_loss)
//    4. Negative stop loss reject:   stop=-0.0010 → Result.Failure(invalid_stop_loss)
//    5. High override no overflow:   override=5% (max), stop=0.0001 → large volume but no decimal overflow
//    6. Override above profile uses override (sanity for #2): explicit 3% override
//    7. Zero capital + valid stop:   capital=0 → Result.Failure(invalid_capital) (defensive)
//    8. Negative risk percent:       effectiveRisk <= 0 → Result.Failure(invalid_risk_percent)
//
//  Spec reference: openspec/changes/.../specs/position-size-calculator/spec.md
//  Scenario "Valid inputs": volume = (C × p / 100) / |E - S|
// ============================================================================

public class PositionSizeCalculatorTests
{
    private static Money Usd(decimal amount) => Money.FromTrusted(amount, "USD");
    private static Money Eur(decimal amount) => Money.FromTrusted(amount, "EUR");

    [Fact]
    public void Calculate_ValidInputs_ReturnsExpectedVolume()
    {
        // capital=10000, p=1.0, stop=0.0050 → riskAmount=100 → volume=20000
        var capital = Usd(10_000m);

        var result = PositionSizeCalculator.Calculate(
            capital: capital,
            riskPerTradePercent: 1.0m,
            stopLossDistance: 0.0050m);

        result.IsSuccess.Should().BeTrue();
        result.Value.Volume.Should().Be(20_000m);
        result.Value.RiskAmount.Should().Be(100m);
        result.Value.RiskPerTradePercent.Should().Be(1.0m);
    }

    [Fact]
    public void Calculate_OverrideReplacesProfileRiskPercent()
    {
        // Profile says 1.0%, but override is 2.0% → effectiveRisk=2.0%
        var capital = Usd(10_000m);

        var result = PositionSizeCalculator.Calculate(
            capital: capital,
            riskPerTradePercent: 1.0m,
            stopLossDistance: 0.0050m,
            riskOverridePercent: 2.0m);

        result.IsSuccess.Should().BeTrue();
        result.Value.RiskPerTradePercent.Should().Be(2.0m);
        result.Value.RiskAmount.Should().Be(200m);
        result.Value.Volume.Should().Be(40_000m);
    }

    [Fact]
    public void Calculate_OverrideAboveProfileUsesOverride()
    {
        // Profile=1.0%, override=3.0% → effectiveRisk=3.0% (override wins even when above profile)
        var capital = Usd(10_000m);

        var result = PositionSizeCalculator.Calculate(
            capital: capital,
            riskPerTradePercent: 1.0m,
            stopLossDistance: 0.0050m,
            riskOverridePercent: 3.0m);

        result.IsSuccess.Should().BeTrue();
        result.Value.RiskPerTradePercent.Should().Be(3.0m);
        result.Value.RiskAmount.Should().Be(300m);
        result.Value.Volume.Should().Be(60_000m);
    }

    [Fact]
    public void Calculate_ZeroStopLoss_ReturnsFailure()
    {
        var capital = Usd(10_000m);

        var result = PositionSizeCalculator.Calculate(
            capital: capital,
            riskPerTradePercent: 1.0m,
            stopLossDistance: 0m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.position_size.invalid_stop_loss");
    }

    [Fact]
    public void Calculate_NegativeStopLoss_ReturnsFailure()
    {
        var capital = Usd(10_000m);

        var result = PositionSizeCalculator.Calculate(
            capital: capital,
            riskPerTradePercent: 1.0m,
            stopLossDistance: -0.0010m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.position_size.invalid_stop_loss");
    }

    [Fact]
    public void Calculate_HighOverrideWithSmallStop_NoDecimalOverflow()
    {
        // Override at the spec's allowed ceiling (5.00%), tiny stop (0.0001) →
        // riskAmount = 500, volume = 5,000,000 units. No decimal overflow.
        // The domain does NOT clamp riskAmount to capitalAmount; the application
        // boundary validates override in [0.01, 5.00].
        var capital = Usd(10_000m);

        var result = PositionSizeCalculator.Calculate(
            capital: capital,
            riskPerTradePercent: 1.0m,
            stopLossDistance: 0.0001m,
            riskOverridePercent: 5.0m);

        result.IsSuccess.Should().BeTrue();
        result.Value.RiskAmount.Should().Be(500m);
        result.Value.Volume.Should().Be(5_000_000m);
    }

    [Fact]
    public void Calculate_ZeroCapital_ReturnsFailure()
    {
        // Defensive: capital=0 makes riskAmount=0 regardless of risk%, and
        // volume would be 0/stop=0 (NaN). The calculator rejects before
        // that arithmetic.
        var capital = Usd(0m);

        var result = PositionSizeCalculator.Calculate(
            capital: capital,
            riskPerTradePercent: 1.0m,
            stopLossDistance: 0.0050m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.position_size.invalid_capital");
    }

    [Fact]
    public void Calculate_NegativeEffectiveRisk_ReturnsFailure()
    {
        // riskOverridePercent=-0.5 (sanity check; the validator caps to >= 0.01,
        // but the calculator stays defensive in case a future caller forgets).
        var capital = Usd(10_000m);

        var result = PositionSizeCalculator.Calculate(
            capital: capital,
            riskPerTradePercent: 1.0m,
            stopLossDistance: 0.0050m,
            riskOverridePercent: -0.5m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.position_size.invalid_risk_percent");
    }

    [Fact]
    public void Calculate_EurCapital_ComputesInEurNotUsd()
    {
        // Pure-math sanity: changing capital currency does not change the
        // formula — the calculator is currency-agnostic, the caller is
        // responsible for currency matching (checked at the handler level).
        var capital = Eur(10_000m);

        var result = PositionSizeCalculator.Calculate(
            capital: capital,
            riskPerTradePercent: 1.0m,
            stopLossDistance: 0.0050m);

        result.IsSuccess.Should().BeTrue();
        result.Value.Volume.Should().Be(20_000m);
        result.Value.RiskAmount.Should().Be(100m);
    }
}
