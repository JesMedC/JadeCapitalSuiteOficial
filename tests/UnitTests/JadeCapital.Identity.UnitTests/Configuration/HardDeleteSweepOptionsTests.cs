using FluentAssertions;
using JadeCapital.Identity.Infrastructure.Configuration;
using Xunit;

namespace JadeCapital.Identity.UnitTests.Configuration;

// ============================================================================
//  HardDeleteSweepOptionsTests — Wave 11 slice 11.3
//
//  RED-first TDD guard (Wave 10 W-04 closure). Pins the Wave 10.5 hardcoded
//  constants behind configurable values so per-environment tuning no longer
//  requires recompilation.
//
//  Defaults match Wave 10.5's `HardDeleteSweepBackgroundService` as-shipped:
//    - InitialDelaySeconds = 120  (2 min)
//    - IntervalHours      = 24
//    - GracePeriodDays    = 30
//    - MaxJitterMinutes   = 30
//    - BatchLimit         = 100  (Wave 10.5 had no limit; pick a safe default)
//
//  Section name is pinned to "HardDeleteSweep" so appsettings + env vars bind
//  identically to the DI registration in `IdentityModuleRegistration.cs`.
// ============================================================================

public sealed class HardDeleteSweepOptionsTests
{
    [Fact]
    public void SectionName_IsHardDeleteSweep()
    {
        // The DI wiring in IdentityModuleRegistration uses this literal to
        // bind to configuration. Drift here breaks appsettings binding.
        HardDeleteSweepOptions.SectionName.Should().Be("HardDeleteSweep");
    }

    [Fact]
    public void Defaults_MatchWave10HardcodedValues()
    {
        var options = new HardDeleteSweepOptions();

        options.InitialDelaySeconds.Should().Be(120,
            "Wave 10.5 used `await Task.Delay(TimeSpan.FromMinutes(2), ...)` as the first-run delay");
        options.IntervalHours.Should().Be(24,
            "Wave 10.5 used `TimeSpan.FromHours(24).Add(...)` as the per-cycle base interval");
        options.GracePeriodDays.Should().Be(30,
            "Wave 10.5 used `public const int GracePeriodDays = 30` as the hard-delete grace window");
        options.MaxJitterMinutes.Should().Be(30,
            "Wave 10.5 used `private const double MaxJitterMs = 30d * 60d * 1000d` for the per-cycle jitter");
        options.BatchLimit.Should().BeGreaterThan(0,
            "the per-cycle batch limit MUST be > 0 so RunOnceAsync always terminates on a finite window");
    }

    [Fact]
    public void TimeSpanProperties_ComputeFromSecondsAndHours()
    {
        var options = new HardDeleteSweepOptions
        {
            InitialDelaySeconds = 60,
            IntervalHours = 6,
            GracePeriodDays = 14,
        };

        options.InitialDelay.Should().Be(TimeSpan.FromSeconds(60));
        options.Interval.Should().Be(TimeSpan.FromHours(6));
        options.GracePeriod.Should().Be(TimeSpan.FromDays(14));
    }

    [Fact]
    public void Setters_AllBindingsAreMutable()
    {
        // The Bind(configuration).ValidateOnStart() pattern requires POCO
        // mutability. If any setter is missing or private, the config
        // section silently no-ops at startup.
        var options = new HardDeleteSweepOptions
        {
            InitialDelaySeconds = 1,
            IntervalHours = 2,
            BatchLimit = 3,
            GracePeriodDays = 4,
            MaxJitterMinutes = 5,
        };

        options.InitialDelaySeconds.Should().Be(1);
        options.IntervalHours.Should().Be(2);
        options.BatchLimit.Should().Be(3);
        options.GracePeriodDays.Should().Be(4);
        options.MaxJitterMinutes.Should().Be(5);
    }
}
