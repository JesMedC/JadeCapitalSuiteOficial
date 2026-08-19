using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Identity.Application.Features.Auth.Consent;

// ============================================================================
//  Cookie consent command — Wave 11 slice 11.4.
//
//  Issued by `POST /api/auth/consent` from the Angular cookie banner after
//  the user clicks "Aceptar todas" / "Solo esenciales". The handler persists
//  the choice + the acceptance timestamp on the user row (ePrivacy
//  Directive + GDPR Art. 6(1)(a)) and returns a 200 with the canonical
//  tier echoed back so the FE can update its in-memory state.
//
//  <para>
//  <b>Auth model</b>: the command requires a valid JWT. The JWT claim
//  resolves the userId; the handler reads the user, mutates the consent
//  columns, and saves via the unit-of-work. Cross-tenant usage is
//  irrelevant here — consent is per-user, and the userId comes from the
//  token, not a path/body parameter.
//  </para>
//
//  <para>
//  <b>Idempotency</b>: re-POSTing the same choice is a silent no-op (the
//  handler preserves the original <c>cookie_consent_accepted_at</c>
//  timestamp — see <c>User.RecordCookieConsent</c>). Re-POSTing a
//  DIFFERENT choice updates the column to the new choice + bumps the
//  timestamp to <c>now()</c>.
//  </para>
// ============================================================================

public sealed record ConsentCommand(
    Guid UserId,
    string Choice) : IRequest<Result<ConsentResult>>;

/// <summary>
/// Response shape for `POST /api/auth/consent`. The handler echoes back
/// the choice + the timestamp at which it was recorded so the FE can
/// synchronise its localStorage copy.
/// </summary>
public sealed record ConsentResult(
    string Choice,
    DateTimeOffset CookieConsentAcceptedAt);

/// <summary>
/// Canonical cookie-tier values enforced by the validator. The set is
/// intentionally small (only 'all' + 'essential') so the FE banner can
/// stay binary. New tiers ('functional', 'none') would land as a follow-up
/// slice and add themselves here without a schema migration.
/// </summary>
public static class CookieConsentChoices
{
    public const string All = "all";
    public const string Essential = "essential";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        All,
        Essential,
    };

    /// <summary>
    /// Application-layer normaliser: trims + lowercases the FE-supplied
    /// choice so the wire-format doesn't dictate casing. Returns
    /// <c>false</c> on an unrecognised tier (handler maps this to 422).
    /// </summary>
    public static bool TryNormalize(string input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var candidate = input.Trim().ToLowerInvariant();
        if (!Allowed.Contains(candidate)) return false;

        normalized = candidate;
        return true;
    }
}
