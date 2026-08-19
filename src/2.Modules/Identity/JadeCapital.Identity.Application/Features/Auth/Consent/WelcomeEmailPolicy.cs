using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Infrastructure.Email;

namespace JadeCapital.Identity.Application.Features.Auth.Consent;

// ============================================================================
//  WelcomeEmailPolicy — Wave 11 slice 11.4.
//
//  Pure decision logic for the post-registration welcome email. Extracted
//  from RegisterUserHandler so the 7-day suppression window is unit-
//  testable WITHOUT spinning up a DB + SMTP transport. The handler is a
//  thin adapter: load → check policy → maybe send → persist timestamp.
//
//  <para>
//  <b>Why an immutable static class instead of an interface + DI</b>:
//  the policy is deterministic + branchless + free of I/O. A new
//  implementation (e.g., a config-driven "suppress on Sundays only")
//  would land via the existing pattern (IOptions<...> + class), but for
//  slice 11.4 the contract is a hard-coded 7-day window with no DI
//  wiring cost.
//  </para>
// ============================================================================

public static class WelcomeEmailPolicy
{
    /// <summary>
    /// The hard-coded suppression window. Per the slice-11.4 spec: a
    /// re-registration inside this window does NOT re-fire the email.
    /// </summary>
    public static readonly TimeSpan SuppressionWindow = TimeSpan.FromDays(7);

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
    public static Decision ShouldSend(User user, DateTimeOffset now)
    {
        if (user.WelcomeEmailSentAt is null) return Decision.SendFirstTime;
        if ((now - user.WelcomeEmailSentAt.Value) < SuppressionWindow) return Decision.SuppressedRecentSend;
        return Decision.SuppressionLapsed;
    }

    /// <summary>
    /// Convenience: returns true when the policy recommends firing the email.
    /// </summary>
    public static bool ShouldSendBool(User user, DateTimeOffset now)
        => ShouldSend(user, now) != Decision.SuppressedRecentSend;
}
