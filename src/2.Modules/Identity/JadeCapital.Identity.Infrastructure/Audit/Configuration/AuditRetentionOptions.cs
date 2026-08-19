using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JadeCapital.Identity.Infrastructure.Audit.Configuration;

/// <summary>
/// Configuration for the <see cref="AuditRetentionBackgroundService"/>
/// (Wave 9, slice 9b.1 — audit retention, sub-scope C).
///
/// <para>
/// Bound from the <c>AuditRetention</c> config section in
/// <c>appsettings.json</c> + env vars. Defaults match the spec; the
/// <see cref="ValidateOnStart"/> hook enforces non-positive ranges at
/// startup so a broken config fails the host before the first run.
/// </para>
///
/// <para>
/// Defaults rationale (per spec):
/// <list type="bullet">
///   <item><b>RetentionDays = 90</b> — compliance horizon. Older rows
///         are GDPR/CCPA-eligible to discard once the retention
///         obligation expires.</item>
///   <item><b>CleanupIntervalHours = 24</b> — one cycle per day keeps
///         the lock duration short + aligns with daily DBA routines.</item>
///   <item><b>BatchLimit = 10000</b> — caps the per-call delete so a
///         single cycle never holds a long lock on the table.</item>
///   <item><b>InitialDelaySeconds = 120</b> — 2-minute startup settle
///         (matches the spec's "<c>BackfillTenantsHostedService</c>
///         precedent scaled for a heavier first run").</item>
/// </list>
/// </para>
/// </summary>
public sealed class AuditRetentionOptions
{
    /// <summary>Configuration section name (used by the DI binding in <c>IdentityModuleRegistration</c>).</summary>
    public const string SectionName = "AuditRetention";

    /// <summary>
    /// Audit rows older than this many days are deleted each cycle.
    /// Default: <c>90</c>.
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// Hours between consecutive <see cref="AuditRetentionBackgroundService"/>
    /// cycles. Default: <c>24</c>.
    /// </summary>
    public int CleanupIntervalHours { get; set; } = 24;

    /// <summary>
    /// Maximum number of rows deleted in a single
    /// <see cref="IAuditRetentionService.PurgeOldAsync"/> call. The
    /// BackgroundService loops within a single cycle until
    /// <c>PurgeOldAsync</c> returns <c>0</c>. Default: <c>10000</c>.
    /// </summary>
    public int BatchLimit { get; set; } = 10000;

    /// <summary>
    /// Seconds to wait after host startup before the first retention
    /// run. Lets the rest of the pipeline (DB connection pool warm-up,
    /// migration runner) settle. Default: <c>120</c> (2 minutes).
    /// </summary>
    public int InitialDelaySeconds { get; set; } = 120;

    /// <summary>
    /// First-run delay as a <see cref="TimeSpan"/>. Computed once so
    /// the BackgroundService loop reads a stable value (the
    /// <c>IOptionsMonitor</c> in the service re-reads the underlying
    /// values, but the derived <see cref="TimeSpan"/> is cached on
    /// first access).
    /// </summary>
    public TimeSpan InitialDelay => TimeSpan.FromSeconds(InitialDelaySeconds);

    /// <summary>
    /// Per-cycle delay as a <see cref="TimeSpan"/>.
    /// </summary>
    public TimeSpan CleanupInterval => TimeSpan.FromHours(CleanupIntervalHours);

    /// <summary>
    /// Validates the options at startup via
    /// <c>ValidateOnStart</c>. Invalid config fails the host BEFORE the
    /// first retention run (matches the Wave 6 6c.2 <c>JwtOptions</c>
    /// precedent).
    /// </summary>
    public static IValidateOptions<AuditRetentionOptions> Validator => new AuditRetentionOptionsValidator();

    private sealed class AuditRetentionOptionsValidator : IValidateOptions<AuditRetentionOptions>
    {
        public ValidateOptionsResult Validate(string? name, AuditRetentionOptions options)
        {
            var failures = new List<string>();
            if (options.RetentionDays <= 0)
                failures.Add($"{nameof(AuditRetentionOptions.RetentionDays)} must be > 0.");
            if (options.CleanupIntervalHours <= 0)
                failures.Add($"{nameof(AuditRetentionOptions.CleanupIntervalHours)} must be > 0.");
            if (options.BatchLimit <= 0)
                failures.Add($"{nameof(AuditRetentionOptions.BatchLimit)} must be > 0.");
            if (options.InitialDelaySeconds < 0)
                failures.Add($"{nameof(AuditRetentionOptions.InitialDelaySeconds)} must be >= 0.");
            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}