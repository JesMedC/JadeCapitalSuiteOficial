using JadeCapital.Identity.Domain.Users;
using Microsoft.Extensions.Options;

namespace JadeCapital.Identity.Application.Features.Auth.Consent;

// ============================================================================
//  WelcomeEmailPolicy — Wave 11 slice 11.4 → Wave 12 slice 12.2.
//
//  Pure decision logic for the post-registration welcome email. Wave 11.4
//  shipped a static class with a hard-coded 7-day suppression window. Wave
//  12.2 converts it to an instance class wrapping
//  <see cref="WelcomeEmailPolicyOptions"/> so the suppression window +
//  SendOnRegister master switch are per-environment tunable from
//  appsettings (IdentityModuleRegistration registers the IOptions binding).
//
//  <para>
//  <b>Why an instance class with IOptions</b>: the policy is deterministic
//  + branchless + free of I/O — perfect for a value-object pattern. The
//  refactor adds a single constructor dependency (IOptions<...>) and
//  registers the class as a scoped service; the decision logic itself is
//  byte-for-byte identical to the Wave 11.4 static implementation, so all
//  existing unit tests pass without behavior changes.
//  </para>
// ============================================================================

public class WelcomeEmailPolicy
{
    private readonly WelcomeEmailPolicyOptions _options;

    public WelcomeEmailPolicy(IOptions<WelcomeEmailPolicyOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// The suppression window projected from <see cref="WelcomeEmailPolicyOptions.SuppressionDays"/>.
    /// Wave 11.4 default: 7 days. Operators can shorten / lengthen via appsettings.
    /// </summary>
    public TimeSpan SuppressionWindow => _options.Suppression;

    /// <summary>
    /// Master switch exposed as a property so callers can short-circuit
    /// without depending on the options directly. When false, the handler
    /// skips the welcome-email send entirely.
    /// </summary>
    public bool IsEnabled => _options.SendOnRegister;

    /// <summary>
    /// Result of the pre-send gate. The handler branches on the decision
    /// so the call site can log explicitly.
    /// </summary>
    public enum Decision
    {
        /// <summary>The user has never been welcomed (WelcomeEmailSentAt == null). Send.</summary>
        SendFirstTime,
        /// <summary>The user has been welcomed within the suppression window. Skip.</summary>
        SuppressedRecentSend,
        /// <summary>The previous welcome was sent BEYOND the suppression window. Re-send.</summary>
        SuppressionLapsed,
    }

    /// <summary>
    /// Pure decision: should we re-fire the welcome email for <paramref name="user"/>
    /// at <paramref name="now"/>? Returns the categorical decision so callers
    /// can log explicitly.
    /// </summary>
    public Decision ShouldSend(User user, DateTimeOffset now)
    {
        if (user.WelcomeEmailSentAt is null) return Decision.SendFirstTime;
        if ((now - user.WelcomeEmailSentAt.Value) < SuppressionWindow) return Decision.SuppressedRecentSend;
        return Decision.SuppressionLapsed;
    }

    /// <summary>
    /// Convenience: returns true when the policy recommends firing the email.
    /// </summary>
    public bool ShouldSendBool(User user, DateTimeOffset now)
        => ShouldSend(user, now) != Decision.SuppressedRecentSend;
}
