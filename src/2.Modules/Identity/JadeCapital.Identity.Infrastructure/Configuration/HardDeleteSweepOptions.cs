using Microsoft.Extensions.Options;

namespace JadeCapital.Identity.Infrastructure.Configuration;

// ============================================================================
//  HardDeleteSweepOptions — Wave 11 slice 11.3
//
//  Configuration for `HardDeleteSweepBackgroundService`.
//
//  <para>
//  Extracted from the hardcoded constants in Wave 10.5's
//  `HardDeleteSweepBackgroundService.cs`. The Wave 10 sdd-verify run
//  flagged (warning #4) those constants as a tuning barrier — per-env
//  tweaks required a recompile. This class binds the same values from
//  the `HardDeleteSweep` configuration section so an operator can
//  shorten the cadence in staging without touching source code.
//  </para>
//
//  <para>
//  Defaults reproduce the Wave 10.5 behavior verbatim:
//  <list type="bullet">
//    <item>InitialDelaySeconds = 120 (2 min) — matches the
//          `TimeSpan.FromMinutes(2)` await at startup.</item>
//    <item>IntervalHours      = 24   — matches the base cycle delay.</item>
//    <item>GracePeriodDays    = 30   — matches `public const int
//          GracePeriodDays = 30`. <b>Note</b>: this class is the BG
//          service's CYCLE cadence knob, NOT the per-user grace window
//          enforced by `User.ScheduleHardDelete`. The grace window
//          constant (HardDeleteGraceDays = 30) lives in
//          `DeleteAccountHandler` and is unchanged by this slice.</item>
//    <item>MaxJitterMinutes   = 30   — matches `30d * 60d * 1000d`
//          jitter cap.</item>
//    <item>BatchLimit         = 100  — Wave 10.5 had no batch limit; we
//          pick a safe default that bounds per-cycle wall clock + lock
//          duration without rewriting the sweep logic.</item>
//  </list>
//  </para>
//
//  <para>
//  <b>Why a separate `Configuration/` folder</b> (not nested under
//  `BackgroundServices/`): the Wave 9.1 `AuditRetentionOptions` ships in
//  `Audit/Configuration/`; we mirror that layout for one canonical place
//  to discover options classes per slice.
//  </para>
// ============================================================================

public class HardDeleteSweepOptions
{
    /// <summary>Configuration section name. Kept in sync with the DI
    /// binding in <c>IdentityModuleRegistration</c>; drift here breaks
    /// appsettings binding.</summary>
    public const string SectionName = "HardDeleteSweep";

    /// <summary>Seconds to wait after host startup before the first
    /// sweep cycle. Default: <c>120</c> (2 minutes) — matches Wave 10.5.
    /// Allowed to be 0 (operator may opt to run immediately).</summary>
    public int InitialDelaySeconds { get; set; } = 120;

    /// <summary>Hours between consecutive <c>HardDeleteSweep</c> cycles.
    /// Default: <c>24</c> — matches Wave 10.5. MUST be &gt; 0.</summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>Maximum number of due-users purged in a single cycle.
    /// Default: <c>100</c>. Wave 10.5 had no cap; this is a new bound
    /// introduced in slice 11.3 so a backlog of overdue users does not
    /// blow up cycle wall-clock time.</summary>
    public int BatchLimit { get; set; } = 100;

    /// <summary>BackgroundService cadence knob, NOT the per-user grace
    /// window. Retained for parity with Wave 10.5's `GracePeriodDays`
    /// constant which it documented. The actual per-user grace is
    /// enforced by <see cref="JadeCapital.Identity.Domain.Users.User.ScheduleHardDelete"/>
    /// (30 days, fixed). Default: <c>30</c>.</summary>
    public int GracePeriodDays { get; set; } = 30;

    /// <summary>Per-cycle random jitter cap, in minutes. Random.Shared
    /// produces <c>[0, +MaxJitterMinutes]</c>; the value is added to the
    /// base interval. Default: <c>30</c> (matches Wave 10.5).</summary>
    public int MaxJitterMinutes { get; set; } = 30;

    /// <summary>Initial-delay as a <see cref="TimeSpan"/>. Computed
    /// from <see cref="InitialDelaySeconds"/> on read; the validator
    /// guarantees the underlying integer is non-negative.</summary>
    public TimeSpan InitialDelay => TimeSpan.FromSeconds(InitialDelaySeconds);

    /// <summary>Cycle interval as a <see cref="TimeSpan"/>. Computed
    /// from <see cref="IntervalHours"/>; the validator guarantees the
    /// underlying integer is strictly positive.</summary>
    public TimeSpan Interval => TimeSpan.FromHours(IntervalHours);

    /// <summary>Grace-period as a <see cref="TimeSpan"/>. Computed from
    /// <see cref="GracePeriodDays"/>; the validator guarantees the
    /// underlying integer is at least 1.</summary>
    public TimeSpan GracePeriod => TimeSpan.FromDays(GracePeriodDays);
}

// ============================================================================
//  HardDeleteSweepOptionsValidator
//
//  Companion validator for `ValidateOnStart`. Registered as a singleton
//  via `services.AddSingleton<IValidateOptions<HardDeleteSweepOptions>, ...>`
//  in `IdentityModuleRegistration`. Pattern matches the
//  `AuditRetentionOptionsValidator` precedent in slice 9b.1.
//
//  <para>
//  Each `failures.Add(...)` line cites the offending property name in
//  PascalCase so the operator reading the startup error message can
//  locate the misconfigured key in `appsettings.json` (where keys are
//  also PascalCase in this slice — convention set by the existing
//  `AuditRetention` + `Audit` config sections).
//  </para>
// ============================================================================

public class HardDeleteSweepOptionsValidator : IValidateOptions<HardDeleteSweepOptions>
{
    public ValidateOptionsResult Validate(string? name, HardDeleteSweepOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.InitialDelaySeconds < 0 || options.InitialDelaySeconds > 3600)
            failures.Add("HardDeleteSweep:InitialDelaySeconds must be in [0, 3600] (≤1 hour)");

        if (options.IntervalHours < 1 || options.IntervalHours > 168)
            failures.Add("HardDeleteSweep:IntervalHours must be in [1, 168] (≤1 week)");

        if (options.BatchLimit < 1 || options.BatchLimit > 10_000)
            failures.Add("HardDeleteSweep:BatchLimit must be in [1, 10000]");

        if (options.GracePeriodDays < 1 || options.GracePeriodDays > 365)
            failures.Add("HardDeleteSweep:GracePeriodDays must be in [1, 365] (≤1 year)");

        if (options.MaxJitterMinutes < 0 || options.MaxJitterMinutes > 720)
            failures.Add("HardDeleteSweep:MaxJitterMinutes must be in [0, 720] (≤12 hours)");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
