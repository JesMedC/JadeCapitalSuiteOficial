using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Domain.Audit;

/// <summary>
/// Error catalog for the soft-delete feature (Wave 6, slice 6d.1).
///
/// <para>
/// Codes are namespaced under <c>soft_delete.</c> for consistent routing
/// through <see cref="JadeCapital.Shared.Kernel.Validation.DomainGuard"/>
/// + the minimal-API error mappers in <c>TenantEndpoints</c>.
/// </para>
/// </summary>
public static class SoftDeleteErrors
{
    public static class Validation
    {
        /// <summary>
        /// The requested entity type is not registered as soft-deleteable
        /// (no <c>ISoftDeleteProvider</c> matches). Returns 422 so callers
        /// can distinguish "this endpoint doesn't support that entity"
        /// from "the entity doesn't exist" (404).
        /// </summary>
        public static readonly Error EntityNotSoftDeleteable =
            Error.Validation("soft_delete.entity_not_soft_deleteable",
                "The requested entity type is not soft-deleteable.");
    }

    public static class NotFound
    {
        /// <summary>
        /// No entity with the given id exists for the requested entity
        /// type. Returns 404 — same shape as a hard-delete lookup miss.
        /// </summary>
        public static readonly Error EntityNotFound =
            Error.NotFound("soft_delete.entity_not_found",
                "Entity not found.");

        /// <summary>
        /// The entity exists but is already soft-deleted. Returns 404
        /// (NOT 410) — second-delete attempts are indistinguishable from
        /// "no such entity" so a caller cannot probe for deleted rows.
        /// </summary>
        public static readonly Error AlreadyDeleted =
            Error.NotFound("soft_delete.already_deleted",
                "Entity is already deleted.");
    }
}
