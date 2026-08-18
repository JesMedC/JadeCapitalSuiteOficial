using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Identity.Domain.Audit;

/// <summary>
/// AuditEvent aggregate root (Wave 6, slice 6d.1).
///
/// <para>
/// <b>Append-only invariant</b>: every property is read-only. The aggregate
/// exposes zero mutators. The compliance contract is enforced at three
/// layers:
/// </para>
/// <list type="number">
///   <item>Compile time — no public setters, no mutator methods.</item>
///   <item>Runtime — <see cref="AuditEventErrors.Validation"/> rejects
///         illegal inputs at construction time.</item>
///   <item>Infrastructure — the dedicated <c>AuditDbContext</c> is
///         write-only (registered separately from <c>IdentityDbContext</c>);
///         accidental UPDATE/DELETE through the main context is
///         structurally impossible.</item>
/// </list>
///
/// <para>
/// <b>Field semantics</b>:
/// <list type="bullet">
///   <item><see cref="EntityType"/> — short string identifying the entity
///         (e.g. "ImportJob", "Tenant"). Length 1..80.</item>
///   <item><see cref="EntityId"/> — Guid of the entity being audited.</item>
///   <item><see cref="Action"/> — Created/Updated/Deleted/Restored.</item>
///   <item><see cref="TenantId"/> — tenant scope (nullable for cross-tenant admin ops).</item>
///   <item><see cref="UserId"/> — actor (nullable for system actors).</item>
///   <item><see cref="ChangesJson"/> — JSONB diff payload. Null for Created.</item>
///   <item><see cref="OccurredAt"/> — UTC timestamp pinned from
///         <see cref="IClock.UtcNow"/> at construction time (the entry's
///         OccurredAt is ignored — the aggregate owns the timestamp).</item>
/// </list>
/// </para>
/// </summary>
public class AuditEvent : AggregateRoot<Guid>
{
    /// <summary>Maximum length for <see cref="EntityType"/> (matches the SQL VARCHAR(80) column).</summary>
    public const int MaxEntityTypeLength = 80;

    public string EntityType { get; private set; } = default!;
    public Guid EntityId { get; private set; }
    public AuditAction Action { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? ChangesJson { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; } = default!;

    // EF Core materialization.
    private AuditEvent() { }

    private AuditEvent(
        Guid id,
        string entityType,
        Guid entityId,
        AuditAction action,
        Guid? tenantId,
        Guid? userId,
        string? changesJson,
        DateTimeOffset occurredAt) : base(id)
    {
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        TenantId = tenantId;
        UserId = userId;
        ChangesJson = changesJson;
        OccurredAt = occurredAt;
    }

    /// <summary>
    /// Factory: validates every input and returns either a new aggregate
    /// or a <see cref="Result.Failure{T}(Error)"/> with the relevant
    /// <see cref="AuditEventErrors.Validation"/> code.
    /// </summary>
    public static Result<AuditEvent> Create(AuditEventEntry entry, IClock clock)
    {
        if (entry is null)
            return Result.Failure<AuditEvent>(AuditEventErrors.Validation.EntityIdRequired);

        if (string.IsNullOrWhiteSpace(entry.EntityType))
            return Result.Failure<AuditEvent>(AuditEventErrors.Validation.EntityTypeRequired);

        if (entry.EntityType.Length > MaxEntityTypeLength)
            return Result.Failure<AuditEvent>(AuditEventErrors.Validation.EntityTypeTooLong);

        if (entry.EntityId == Guid.Empty)
            return Result.Failure<AuditEvent>(AuditEventErrors.Validation.EntityIdRequired);

        if (!Enum.IsDefined(entry.Action))
            return Result.Failure<AuditEvent>(AuditEventErrors.Validation.ActionInvalid);

        var evt = new AuditEvent(
            id: Guid.NewGuid(),
            entityType: entry.EntityType,
            entityId: entry.EntityId,
            action: entry.Action,
            tenantId: entry.TenantId,
            userId: entry.UserId,
            changesJson: entry.ChangesJson,
            occurredAt: clock.UtcNow);
        return Result.Success(evt);
    }

    /// <summary>
    /// Test-only rehydrate from a trusted source. Does NOT validate inputs —
    /// the persistence layer already enforced invariants when the row was
    /// written. Used by EF hydration + the 6d.2 AuditLogger impl.
    /// </summary>
    public static AuditEvent FromTrusted(
        Guid id,
        string entityType,
        Guid entityId,
        AuditAction action,
        Guid? tenantId,
        Guid? userId,
        string? changesJson,
        DateTimeOffset occurredAt)
        => new(id, entityType, entityId, action, tenantId, userId, changesJson, occurredAt);
}
