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
    }
}