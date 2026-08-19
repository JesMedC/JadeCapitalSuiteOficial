using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Application._Common;

/// <summary>
/// Errores de aplicacion para Identity. Distintos de los errores de dominio:
/// viven en Application porque son reglas de orquestacion (ej: "password muy corto")
/// que NO son invariantes del modelo.
/// </summary>
public static class IdentityApplicationErrors
{
    public static class Auth
    {
        public static readonly Error PasswordTooShort =
            Error.Validation("auth.password_too_short", "Password must be at least 10 characters.");

        public static readonly Error PasswordRequiresComplexity =
            Error.Validation("auth.password_requires_complexity", "Password must contain letters and digits.");

        public static readonly Error PasswordTooLong =
            Error.Validation("auth.password_too_long", "Password cannot exceed 128 characters.");

        public static readonly Error RefreshTokenInvalid =
            Error.Unauthorized("auth.refresh_token_invalid", "Refresh token is invalid.");

        public static readonly Error AccessDenied =
            Error.Forbidden("auth.access_denied", "You do not have permission for this action.");

        public static readonly Error AccountLockedOut =
            Error.Forbidden("auth.account_locked_out", "Account is temporarily locked out.");

        public static readonly Error RecoveryInvalid =
            Error.Unauthorized("auth.recovery_invalid", "Recovery grant is invalid or has been consumed.");

        public static readonly Error RecoveryExpired =
            Error.Unauthorized("auth.recovery_expired", "Recovery grant has expired.");

        public static readonly Error ConcurrentUpdate =
            Error.Conflict("auth.concurrent_update", "Another change is in progress for this user.");

        // Wave 11 slice 11.4 — cookie consent tier validation.
        public static readonly Error CookieConsentChoiceInvalid =
            Error.Validation("auth.cookie_consent_choice_invalid",
                "Cookie choice must be one of: 'all' or 'essential'.");

        // Wave 11 slice 11.4 — GDPR Art. 7 consent capture at registration.
        public static readonly Error TermsNotAccepted =
            Error.Validation("auth.terms_required",
                "Terms of Service must be accepted to register.");

        public static readonly Error PrivacyNotAccepted =
            Error.Validation("auth.privacy_required",
                "Privacy Policy must be accepted to register.");

        public static readonly Error ConsentIpRequired =
            Error.Validation("auth.consent_ip_required",
                "Consent IP is required for GDPR Art. 7 audit trail.");
    }
}