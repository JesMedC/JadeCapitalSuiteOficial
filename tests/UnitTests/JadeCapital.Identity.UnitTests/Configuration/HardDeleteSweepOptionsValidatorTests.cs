using FluentAssertions;
using JadeCapital.Identity.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace JadeCapital.Identity.UnitTests.Configuration;

// ============================================================================
//  HardDeleteSweepOptionsValidatorTests — Wave 11 slice 11.3
//
//  RED-first TDD guard for the IValidateOptions<HardDeleteSweepOptions>
//  implementation. The validator runs at host start (ValidateOnStart hook)
//  so a misconfigured appsettings fails the host BEFORE the first sweep
//  cycle — matches the Wave 9b.1 AuditRetentionOptions + Wave 6c.2
//  JwtOptions precedent.
// ============================================================================

public sealed class HardDeleteSweepOptionsValidatorTests
{
    private static HardDeleteSweepOptionsValidator CreateValidator() => new();

    [Fact]
    public void Validate_Defaults_Passes()
    {
        var validator = CreateValidator();
        var result = validator.Validate(name: null, options: new HardDeleteSweepOptions());

        result.Succeeded.Should().BeTrue(
            "the default-constructed options MUST validate cleanly — defaults exist precisely to ship a working config");
    }

    [Theory]
    [InlineData(-1, 24, 100, 30, 30, "InitialDelaySeconds must be in")]
    [InlineData(3601, 24, 100, 30, 30, "InitialDelaySeconds must be in")]
    [InlineData(120, 0, 100, 30, 30, "IntervalHours must be in")]
    [InlineData(120, 200, 100, 30, 30, "IntervalHours must be in")]
    [InlineData(120, 24, 0, 30, 30, "BatchLimit must be in")]
    [InlineData(120, 24, 10_001, 30, 30, "BatchLimit must be in")]
    [InlineData(120, 24, 100, 0, 30, "GracePeriodDays must be in")]
    [InlineData(120, 24, 100, 400, 30, "GracePeriodDays must be in")]
    [InlineData(120, 24, 100, 30, -1, "MaxJitterMinutes must be in")]
    [InlineData(120, 24, 100, 30, 800, "MaxJitterMinutes must be in")]
    public void Validate_InvalidOption_Fails(
        int initialDelaySeconds,
        int intervalHours,
        int batchLimit,
        int gracePeriodDays,
        int maxJitterMinutes,
        string expectedFailure)
    {
        var options = new HardDeleteSweepOptions
        {
            InitialDelaySeconds = initialDelaySeconds,
            IntervalHours = intervalHours,
            BatchLimit = batchLimit,
            GracePeriodDays = gracePeriodDays,
            MaxJitterMinutes = maxJitterMinutes,
        };

        var result = CreateValidator().Validate(name: null, options: options);

        result.Failed.Should().BeTrue(
            "the validator MUST reject out-of-range values so the host refuses to start on a broken config");
        result.Failures.Should().Contain(f => f.Contains(expectedFailure, StringComparison.Ordinal),
            "the failure message MUST identify which option is out of range so operators can diagnose appsettings misconfiguration");
    }

    [Fact]
    public void Validate_PartialInvalidity_ReportsEveryFailure()
    {
        // Two simultaneous violations → both reported in a single
        // `Fail(failures)` call. Operators see every misconfigured field
        // at once, not one-at-a-time across restarts.
        var options = new HardDeleteSweepOptions
        {
            // Out-of-range on InitialDelaySeconds (negative) AND
            // IntervalHours (zero). Two failures, one validator call.
            InitialDelaySeconds = -5,
            IntervalHours = 0,
            BatchLimit = 100,
            GracePeriodDays = 30,
            MaxJitterMinutes = 30,
        };

        var result = CreateValidator().Validate(name: null, options: options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(2,
            "the validator MUST collect EVERY out-of-range violation, not short-circuit on the first");
    }

    [Fact]
    public void Validate_UpperBound_AtMax_IsAllowed()
    {
        // Boundary check: the spec sets hard upper bounds. The
        // exact-boundary value MUST validate successfully.
        var options = new HardDeleteSweepOptions
        {
            InitialDelaySeconds = 3600,   // upper bound (1h)
            IntervalHours = 168,         // upper bound (1 week)
            BatchLimit = 10_000,         // upper bound
            GracePeriodDays = 365,       // upper bound (1 year)
            MaxJitterMinutes = 720,      // upper bound (12h)
        };

        var result = CreateValidator().Validate(name: null, options: options);

        result.Succeeded.Should().BeTrue(
            "the exact upper-bound values must validate cleanly — operators on the edge MUST not have to under-tune to deploy");
    }

    [Fact]
    public void Validate_NegativeInitialDelay_AllowedWhenZero_IsAllowed()
    {
        // Boundary check: InitialDelaySeconds = 0 is valid (a deployer
        // may opt to run the first cycle immediately). Only negatives
        // are out-of-range.
        var options = new HardDeleteSweepOptions
        {
            InitialDelaySeconds = 0,
            IntervalHours = 24,
            BatchLimit = 100,
            GracePeriodDays = 30,
            MaxJitterMinutes = 0,
        };

        var result = CreateValidator().Validate(name: null, options: options);

        result.Succeeded.Should().BeTrue(
            "0 is a valid InitialDelaySeconds and 0 is a valid MaxJitterMinutes — neither must be rejected");
    }
}
