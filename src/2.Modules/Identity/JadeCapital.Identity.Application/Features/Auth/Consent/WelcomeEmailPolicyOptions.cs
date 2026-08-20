namespace JadeCapital.Identity.Application.Features.Auth.Consent;

// ============================================================================
//  WelcomeEmailPolicyOptions — Wave 12 slice 12.2.
//
//  Per-environment tunability for the post-registration welcome email that
//  Wave 11.4 (slice 11.4) had hard-coded with a 7-day suppression window.
//  Bound from the <c>WelcomeEmailPolicy</c> section of appsettings via the
//  canonical Configure + AddOptions + Bind + ValidateOnStart chain (see
//  IdentityModuleRegistration.cs).
//
//  <para>
//  <b>Why extracted now</b>: operations needs to disable the welcome email
//  (e.g. during a marketing blackout) without a recompile. Setting
//  <c>WelcomeEmailPolicy:SendOnRegister=false</c> is an appsettings change.
//  </para>
//
//  <para>
//  <b>Why defaults match Wave 11.4</b>: zero behavioral change at first
//  deployment. Operators opt in to non-default values explicitly.
//  </para>
// ============================================================================

public class WelcomeEmailPolicyOptions
{
    /// <summary>
    /// Section name bound from <c>appsettings.json</c>. Stable contract —
    /// changing it is a breaking config change.
    /// </summary>
    public const string SectionName = "WelcomeEmailPolicy";

    /// <summary>
    /// Days since the last welcome email after which a re-registration
    /// re-fires the email. Wave 11.4 default: 7 days. Zero = never suppress
    /// (every registration sends). Negative is rejected by the validator.
    /// </summary>
    public int SuppressionDays { get; set; } = 7;

    /// <summary>
    /// Master switch for the post-registration welcome email. When false
    /// the handler skips the send entirely (the suppression window is moot
    /// because no email ever fires). Default true to preserve Wave 11.4.
    /// </summary>
    public bool SendOnRegister { get; set; } = true;

    /// <summary>
    /// Convenience projection: <see cref="SuppressionDays"/> as <see cref="TimeSpan"/>.
    /// Used by <see cref="WelcomeEmailPolicy.SuppressionWindow"/>.
    /// </summary>
    public TimeSpan Suppression => TimeSpan.FromDays(SuppressionDays);
}
