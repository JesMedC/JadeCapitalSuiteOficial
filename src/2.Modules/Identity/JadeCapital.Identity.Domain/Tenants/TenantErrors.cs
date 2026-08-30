using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Domain.Tenants;

/// <summary>
/// Error catalog for the <see cref="Tenant"/> aggregate (Wave 6, slice 6c.1).
///
/// <para>
/// Codes are namespaced under <c>tenant.</c> for consistent routing through
/// <see cref="JadeCapital.Shared.Kernel.Validation.DomainGuard"/>.
/// </para>
/// </summary>
public static class TenantErrors
{
    public static class Validation
    {
        public static readonly Error IdRequired =
            Error.Validation("tenant.id_required", "Tenant identifier is required.");

        public static readonly Error NameRequired =
            Error.Validation("tenant.name_required", "Tenant name is required.");

        public static readonly Error NameTooShort =
            Error.Validation("tenant.name_too_short", "Tenant name must be at least 2 characters.");

        public static readonly Error NameTooLong =
            Error.Validation("tenant.name_too_long", "Tenant name cannot exceed 120 characters.");

        public static readonly Error SlugRequired =
            Error.Validation("tenant.slug_required", "Tenant slug is required.");

        public static readonly Error SlugTooLong =
            Error.Validation("tenant.slug_too_long", "Tenant slug cannot exceed 64 characters.");

        public static readonly Error SlugFormatInvalid =
            Error.Validation("tenant.slug_format_invalid",
                "Tenant slug must match [a-z0-9-]+ (lowercase letters, digits, dashes).");

        public static readonly Error OwnerRequired =
            Error.Validation("tenant.owner_required", "Tenant owner user id is required.");

        public static readonly Error PlanInvalid =
            Error.Validation("tenant.plan_invalid", "Tenant plan is not a defined value.");

        public static readonly Error StatusInvalid =
            Error.Validation("tenant.status_invalid", "Tenant status is not a defined value.");

        public static readonly Error SuspensionReasonRequired =
            Error.Validation("tenant.suspension_reason_required", "Suspension reason is required.");
    }

    public static class Conflict
    {
        public static readonly Error AlreadySuspended =
            Error.Conflict("tenant.already_suspended", "Tenant is already suspended.");

        public static readonly Error AlreadyArchived =
            Error.Conflict("tenant.already_archived", "Tenant is already archived.");

        public static readonly Error CannotArchiveActive =
            Error.Conflict("tenant.cannot_archive_active",
                "Tenant must be suspended before it can be archived.");

        public static readonly Error CannotReactivate =
            Error.Conflict("tenant.cannot_reactivate",
                "Tenant status does not allow reactivation.");

        public static readonly Error SlugTaken =
            Error.Conflict("tenant.slug_taken", "A tenant with this slug already exists.");
    }

    public static class NotFound
    {
        public static readonly Error OwnerNotFound =
            Error.NotFound("tenant.owner_not_found", "Owner user not found.");

        public static readonly Error TenantNotFound =
            Error.NotFound("tenant.not_found", "Tenant not found.");

        public static readonly Error CrossTenantAccess =
            Error.NotFound("tenant.not_found",
                "Tenant not found or not accessible from the current context.");

        public static readonly Error UserNotInTenant =
            Error.NotFound("tenant.user_not_in_tenant",
                "User is not a member of this tenant.");

        /// <summary>
        /// Slice 6c.3 — the Personal default tenant (stable slug
        /// <c>personal-default</c>, created by 0026_backfill_personal_tenant.sql
        /// and <c>BackfillTenantsRunner</c>) was not found in the DB. This
        /// should be impossible after the Wave-6 migrations run; surfacing
        /// a dedicated error keeps the failure observable rather than
        /// silently writing NULL into the (NOT NULL) tenant_id column.
        /// </summary>
        public static readonly Error PersonalDefaultMissing =
            Error.NotFound("tenant.personal_default_missing",
                "Personal default tenant is missing — migration 0026_backfill_personal_tenant.sql has not run.");
    }

    public static class Capacity
    {
        /// <summary>
        /// Slice 6c.3 — tenant has reached its plan-bounded user capacity.
        /// Personal=10, Pro=100, Enterprise=1000 (see
        /// <see cref="Tenant.MaxUsersForPlan"/>). Returning 422 lets the
        /// client surface an upsell ("upgrade to Pro") rather than a 500.
        /// </summary>
        public static readonly Error AtCapacity =
            Error.Validation("tenant.at_capacity",
                "Tenant has reached its plan-bounded user capacity.");

        /// <summary>
        /// Slice 6c.3 — owner cannot remove themselves from their own tenant.
        /// Invariant from design.md: removing the owner would leave the
        /// tenant without an owner, which the API doesn't support
        /// (transfer-ownership is a separate operation, not in Wave 6).
        /// </summary>
        public static readonly Error OwnerCannotRemoveSelf =
            Error.Validation("tenant.owner_cannot_remove_self",
                "Tenant owner cannot remove themselves from the tenant.");

        /// <summary>
        /// Slice 6c.3 — Suspended tenants reject all mutations
        /// (rename, plan change, invite, remove). Matches the
        /// "compliance hold" invariant documented in design.md.
        /// </summary>
        public static readonly Error CannotModifySuspended =
            Error.Validation("tenant.cannot_modify_suspended",
                "Suspended tenants are read-only until reactivated.");
    }
}
