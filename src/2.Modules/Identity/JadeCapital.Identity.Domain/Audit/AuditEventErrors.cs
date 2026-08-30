using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Domain.Audit;

/// <summary>
/// Error catalog for the <see cref="AuditEvent"/> aggregate (Wave 6, slice 6d.1).
///
/// <para>
/// Codes are namespaced under <c>audit.</c> for consistent routing through
/// <see cref="JadeCapital.Shared.Kernel.Validation.DomainGuard"/>.
/// </para>
/// </summary>
public static class AuditEventErrors
{
    public static class Validation
    {
        /// <summary>EntityType is empty / whitespace.</summary>
        public static readonly Error EntityTypeRequired =
            Error.Validation("audit.entity_type_required",
                "Audit event entity_type is required.");

        /// <summary>EntityType exceeds 80 chars (DB column is VARCHAR(80)).</summary>
        public static readonly Error EntityTypeTooLong =
            Error.Validation("audit.entity_type_too_long",
                "Audit event entity_type cannot exceed 80 characters.");

        /// <summary>EntityId is Guid.Empty — every audit row must reference a real entity.</summary>
        public static readonly Error EntityIdRequired =
            Error.Validation("audit.entity_id_required",
                "Audit event entity_id is required.");

        /// <summary>Action is outside the AuditAction enum range (0..3).</summary>
        public static readonly Error ActionInvalid =
            Error.Validation("audit.action_invalid",
                "Audit event action is not a defined value (must be 0..3).");
    }
}
