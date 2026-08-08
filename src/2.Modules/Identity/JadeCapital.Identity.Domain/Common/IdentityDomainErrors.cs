using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Domain.Common;

/// <summary>
/// Errores semanticos del bounded context Identity.
/// Convencion: codigo = "{boundedContext}.{entidad}.{detalle}" para que el
/// DomainGuard pueda enrutarlos correctamente.
/// </summary>
public static class IdentityDomainErrors
{
    public static class User
    {
        public static readonly Error IdRequired =
            Error.Validation("user.id_required", "User identifier cannot be empty.");

        public static readonly Error EmailInvalid =
            Error.Validation("user.email_invalid", "Email is not a valid address.");

        public static readonly Error DisplayNameInvalid =
            Error.Validation("user.display_name_invalid", "Display name must be at least 2 characters.");

        public static readonly Error PasswordHashRequired =
            Error.Validation("user.password_hash_required", "Password hash is required.");

        public static readonly Error TimezoneInvalid =
            Error.Validation("user.timezone_invalid", "Timezone is invalid or too long.");

        public static readonly Error CannotSuspendCancelled =
            Error.Conflict("user.cannot_suspend_cancelled", "Cancelled users cannot be suspended.");

        public static readonly Error CannotReactivateCancelled =
            Error.Conflict("user.cannot_reactivate_cancelled", "Cancelled users cannot be reactivated.");

        public static readonly Error AlreadyCancelled =
            Error.Conflict("user.already_cancelled", "User is already cancelled.");

        public static readonly Error NotSuspended =
            Error.Conflict("user.not_suspended", "User is not in a suspendable state.");

        public static readonly Error SuspensionReasonRequired =
            Error.Validation("user.suspension_reason_required", "Suspension reason is required.");

        public static readonly Error CancelledCannotLogin =
            Error.Forbidden("user.cancelled_cannot_login", "Cancelled accounts cannot log in.");

        public static readonly Error SuspendedCannotLogin =
            Error.Forbidden("user.suspended_cannot_login", "Suspended accounts cannot log in.");

        public static readonly Error LockedOutCannotLogin =
            Error.Forbidden("user.locked_out_cannot_login", "Account is temporarily locked out.");

        public static readonly Error NotActiveCannotLogin =
            Error.Forbidden("user.not_active_cannot_login", "Account is not active.");
    }

    public static class RefreshToken
    {
        public static readonly Error AlreadyRevoked =
            Error.Conflict("refresh_token.already_revoked", "Refresh token is already revoked.");

        public static readonly Error Expired =
            Error.Unauthorized("refresh_token.expired", "Refresh token has expired.");

        public static readonly Error NotFound =
            Error.NotFound("refresh_token.not_found", "Refresh token not found.");

        public static readonly Error Reuse =
            Error.Unauthorized("refresh_token.reuse_detected", "Refresh token reuse detected. All sessions revoked.");
    }
}