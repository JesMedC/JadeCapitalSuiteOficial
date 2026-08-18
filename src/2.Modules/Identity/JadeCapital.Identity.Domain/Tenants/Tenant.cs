using System.Text.RegularExpressions;
using JadeCapital.Identity.Domain.Tenants.Events;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Identity.Domain.Tenants;

/// <summary>
/// Tenant aggregate root (Wave 6, slice 6c.1).
///
/// <para>
/// Represents an isolated workspace (Personal / Pro / Enterprise) that owns
/// a set of users via the <c>identity.users.tenant_id</c> FK. The column is
/// nullable in 6c.1 (per user decision: ONE migration atómica strategy),
/// becomes NOT NULL in slice 6c.3 after the 6c.2 backfill.
/// </para>
///
/// <para>
/// <b>Invariants</b> (design.md § Tenant):
/// <list type="bullet">
///   <item><see cref="Slug"/> matches <c>[a-z0-9-]+</c>, length 1..64, UNIQUE in DB</item>
///   <item><see cref="Name"/> length 2..120, trimmed</item>
///   <item><see cref="OwnerUserId"/> non-empty, immutable</item>
///   <item><see cref="Status"/> transitions: <c>Active → Suspended → Archived</c>. No back.</item>
///   <item><see cref="Plan"/> is one of <see cref="TenantPlan"/> values</item>
///   <item><see cref="Id"/>, <see cref="Slug"/>, <see cref="OwnerUserId"/>,
///   <see cref="CreatedAt"/> are immutable after <see cref="Create"/></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Idempotency</b>: <see cref="Slug"/> uniqueness is enforced at the DB
/// level (migration 0024 — <c>UNIQUE INDEX ux_tenants_slug</c>); the
/// handler in slice 6c.1 catches the unique-constraint violation and maps
/// it to <c>tenant.slug_taken</c>.
/// </para>
/// </summary>
public class Tenant : AggregateRoot<Guid>
{
    /// <summary>Max name length per design.md § Tenant.</summary>
    public const int MaxNameLength = 120;

    /// <summary>Min name length per design.md § Tenant.</summary>
    public const int MinNameLength = 2;

    /// <summary>Max slug length per design.md § Tenant.</summary>
    public const int MaxSlugLength = 64;

    /// <summary>Slug regex — lowercase letters, digits, dashes.</summary>
    public const string SlugPattern = "^[a-z0-9-]+$";

    private static readonly Regex SlugRegex = new(SlugPattern, RegexOptions.Compiled);

    public string Name { get; private set; } = default!;
    public string Slug { get; private set; } = default!;
    public Guid OwnerUserId { get; private set; }
    public TenantPlan Plan { get; private set; }
    public TenantStatus Status { get; private set; }

    // EF Core materialization.
    private Tenant() { }

    private Tenant(
        Guid id,
        string name,
        string slug,
        Guid ownerUserId,
        TenantPlan plan,
        TenantStatus status,
        DateTimeOffset createdAt) : base(id)
    {
        Name = name;
        Slug = slug;
        OwnerUserId = ownerUserId;
        Plan = plan;
        Status = status;
        SetCreatedAt(createdAt);
    }

    /// <summary>
    /// Factory. Validates every input and returns either the new aggregate
    /// or a <see cref="Result.Failure{T}(Error)"/> with the relevant
    /// <see cref="TenantErrors.Validation"/> code.
    /// </summary>
    public static Result<Tenant> Create(
        Guid id,
        string name,
        string slug,
        Guid ownerUserId,
        TenantPlan plan,
        IClock clock)
    {
        if (id == Guid.Empty)
            return Result.Failure<Tenant>(TenantErrors.Validation.IdRequired);

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Tenant>(TenantErrors.Validation.NameRequired);

        var trimmedName = name.Trim();
        if (trimmedName.Length < MinNameLength)
            return Result.Failure<Tenant>(TenantErrors.Validation.NameTooShort);

        if (trimmedName.Length > MaxNameLength)
            return Result.Failure<Tenant>(TenantErrors.Validation.NameTooLong);

        if (string.IsNullOrWhiteSpace(slug))
            return Result.Failure<Tenant>(TenantErrors.Validation.SlugRequired);

        var trimmedSlug = slug.Trim();
        if (trimmedSlug.Length > MaxSlugLength)
            return Result.Failure<Tenant>(TenantErrors.Validation.SlugTooLong);

        if (!SlugRegex.IsMatch(trimmedSlug))
            return Result.Failure<Tenant>(TenantErrors.Validation.SlugFormatInvalid);

        if (ownerUserId == Guid.Empty)
            return Result.Failure<Tenant>(TenantErrors.Validation.OwnerRequired);

        if (!Enum.IsDefined(plan))
            return Result.Failure<Tenant>(TenantErrors.Validation.PlanInvalid);

        var now = clock.UtcNow;
        var tenant = new Tenant(
            id, trimmedName, trimmedSlug, ownerUserId,
            plan, TenantStatus.Active, now);
        tenant.RaiseDomainEvent(new TenantCreatedDomainEvent(
            tenant.Id, tenant.OwnerUserId, tenant.Slug, now));
        return Result.Success(tenant);
    }

    /// <summary>
    /// Rehydrate from a trusted source (EF hydration, migrations, admin
    /// scripts). Does NOT validate inputs — the persistence layer already
    /// enforced invariants when the row was written. Use ONLY when the row
    /// is known to be valid.
    /// </summary>
    public static Tenant FromTrusted(
        Guid id,
        string name,
        string slug,
        Guid ownerUserId,
        TenantPlan plan,
        TenantStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        var tenant = new Tenant(
            id, name, slug, ownerUserId, plan, status, createdAt);
        if (updatedAt.HasValue)
        {
            // Entity's UpdatedAt setter is protected; reflection-set in
            // production EF hydration OR explicit trusted path.
            typeof(Entity<Guid>)
                .GetProperty(nameof(Entity<Guid>.UpdatedAt))!
                .SetValue(tenant, updatedAt);
        }
        return tenant;
    }

    /// <summary>
    /// Renames the tenant. Trims whitespace; rejects empty / too-short /
    /// too-long values. Does NOT change <see cref="Slug"/> — the slug is
    /// permanent (URL stability invariant).
    /// </summary>
    public Result Rename(string newName, IClock _)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return Result.Failure(TenantErrors.Validation.NameRequired);

        var trimmed = newName.Trim();
        if (trimmed.Length < MinNameLength)
            return Result.Failure(TenantErrors.Validation.NameTooShort);
        if (trimmed.Length > MaxNameLength)
            return Result.Failure(TenantErrors.Validation.NameTooLong);

        Name = trimmed;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Changes the plan tier. Validates the new plan is a defined enum
    /// value. No-op when the plan is unchanged.
    /// </summary>
    public Result ChangePlan(TenantPlan newPlan, IClock _)
    {
        if (!Enum.IsDefined(newPlan))
            return Result.Failure(TenantErrors.Validation.PlanInvalid);

        if (Plan == newPlan) return Result.Success();

        Plan = newPlan;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Transitions <see cref="TenantStatus.Active"/> → <see cref="TenantStatus.Suspended"/>.
    /// Requires a non-empty reason. Idempotent: re-suspending is reported as
    /// a conflict so callers can distinguish.
    /// </summary>
    public Result Suspend(string reason, IClock _)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(TenantErrors.Validation.SuspensionReasonRequired);

        if (Status == TenantStatus.Suspended)
            return Result.Failure(TenantErrors.Conflict.AlreadySuspended);

        if (Status == TenantStatus.Archived)
            return Result.Failure(TenantErrors.Conflict.AlreadyArchived);

        if (Status != TenantStatus.Active)
            return Result.Failure(TenantErrors.Conflict.CannotReactivate);

        Status = TenantStatus.Suspended;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Transitions <see cref="TenantStatus.Suspended"/> → <see cref="TenantStatus.Archived"/>.
    /// Cannot archive an Active tenant (must go through Suspended first).
    /// Idempotent: re-archiving is reported as a conflict.
    /// </summary>
    public Result Archive(IClock _)
    {
        if (Status == TenantStatus.Archived)
            return Result.Failure(TenantErrors.Conflict.AlreadyArchived);

        if (Status == TenantStatus.Active)
            return Result.Failure(TenantErrors.Conflict.CannotArchiveActive);

        if (Status != TenantStatus.Suspended)
            return Result.Failure(TenantErrors.Conflict.CannotReactivate);

        Status = TenantStatus.Archived;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Test-only: tries to reactivate a Suspended tenant. The real
    /// reactivation API lands in a future slice (no business need yet —
    /// suspended tenants escalate to Archived via the compliance flow).
    /// Pinned here as a guard against accidental back-transitions.
    /// </summary>
    public static Result TryReactivate(IClock _)
    {
        // No back-transitions per design.md. Suspended → Active is NOT
        // allowed; if business needs it later, it becomes a separate
        // method with its own audit event.
        return Result.Failure(TenantErrors.Conflict.CannotReactivate);
    }

    /// <summary>
    /// Test-only: tries to suspend an Archived tenant. Pinned to verify
    /// the no-back-transition invariant is enforced end-to-end.
    /// </summary>
    public Result TrySuspendAgain(string reason, IClock _)
    {
        if (Status == TenantStatus.Archived)
            return Result.Failure(TenantErrors.Conflict.AlreadyArchived);

        // Delegate to the regular path so the test surface is realistic.
        return Suspend(reason, _);
    }

    /// <summary>
    /// Test-only: forces a status value. Validates the value is a defined
    /// enum entry. Used by <c>ChangeStatus_OutOfRangeValue_Fails</c>. Public
    /// only because the <c>Identity.UnitTests</c> project doesn't have
    /// <c>InternalsVisibleTo</c> access; do NOT call from production code.
    /// </summary>
    public Result ForceSetStatusForTests(TenantStatus status)
    {
        if (!Enum.IsDefined(status))
            return Result.Failure(TenantErrors.Validation.StatusInvalid);

        Status = status;
        return Result.Success();
    }

    // ============================================
    // Wave 6c.3 — Plan capacity + suspended-mutation guard
    // ============================================

    /// <summary>
    /// Maximum users allowed per plan tier (slice 6c.3). Personal=10,
    /// Pro=100, Enterprise=1000. The InviteTenantUserHandler enforces
    /// this BEFORE creating the invite so a saturated tenant never sends
    /// a misleading email.
    /// </summary>
    public static int MaxUsersForPlan(TenantPlan plan) => plan switch
    {
        TenantPlan.Personal => 10,
        TenantPlan.Pro => 100,
        TenantPlan.Enterprise => 1000,
        _ => 0,
    };

    /// <summary>
    /// Returns the domain failure for a suspended tenant mutation
    /// (rename, plan change, invite, remove). Used by every 6c.3 handler
    /// to short-circuit before reaching the aggregate.
    /// </summary>
    public static Result GuardNotSuspendedForMutation(TenantStatus status)
        => status == TenantStatus.Suspended
            ? Result.Failure(TenantErrors.Capacity.CannotModifySuspended)
            : Result.Success();
}
