using FluentAssertions;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.Time;
using NSubstitute;

namespace JadeCapital.Identity.UnitTests.Audit;

/// <summary>
/// Tests for the <see cref="AuditEvent"/> aggregate (Wave 6, slice 6d.1).
///
/// <para>
/// Eight RED scenarios pinned here (per tasks.md line 419):
/// </para>
/// <list type="number">
///   <item>Create with a valid entry → success, all fields round-trip.</item>
///   <item><c>EntityType</c> empty → validation failure.</item>
///   <item><c>EntityType</c> longer than 80 chars → validation failure.</item>
///   <item><c>EntityId</c> empty → validation failure.</item>
///   <item><c>Action</c> out of enum range → validation failure.</item>
///   <item><c>TenantId</c> nullable — null is accepted (cross-tenant admin ops).</item>
///   <item><c>UserId</c> nullable — null is accepted (system actors).</item>
///   <item><c>OccurredAt</c> is set from the clock on creation.</item>
///   <item>No mutators: aggregate has no public setters for any property
///         after Create. Append-only invariant is enforced at compile time
///         + at runtime (no method changes any state).</item>
/// </list>
///
/// <para>
/// <b>Append-only invariant</b>: every property is read-only (no public
/// setter). The aggregate exposes zero mutators. Future 6d.2 + Wave 7
/// code CANNOT modify an existing AuditEvent row; the EF config also
/// prevents UPDATE/DELETE on the table via a dedicated AuditDbContext.
/// </para>
/// </summary>
public class AuditEventTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static IClock FixedClock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        return clock;
    }

    private static AuditEventEntry ValidEntry() => new(
        EntityType: "ImportJob",
        EntityId: Guid.NewGuid(),
        Action: AuditAction.Created,
        TenantId: Guid.NewGuid(),
        UserId: Guid.NewGuid(),
        ChangesJson: null,
        OccurredAt: FixedNow);

    [Fact]
    public void Create_WithValidEntry_Succeeds_AndAllFieldsRoundTrip()
    {
        var entry = ValidEntry();
        var result = AuditEvent.Create(entry, FixedClock());

        result.IsSuccess.Should().BeTrue();
        var evt = result.Value;
        evt.EntityType.Should().Be(entry.EntityType);
        evt.EntityId.Should().Be(entry.EntityId);
        evt.Action.Should().Be(entry.Action);
        evt.TenantId.Should().Be(entry.TenantId);
        evt.UserId.Should().Be(entry.UserId);
        evt.ChangesJson.Should().Be(entry.ChangesJson);
        evt.OccurredAt.Should().Be(FixedNow);
    }

    [Fact]
    public void Create_EmptyEntityType_Fails_WithEntityTypeRequired()
    {
        var entry = ValidEntry() with { EntityType = "" };

        var result = AuditEvent.Create(entry, FixedClock());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.audit.entity_type_required");
    }

    [Fact]
    public void Create_EntityTypeTooLong_Fails_WithEntityTypeTooLong()
    {
        var entry = ValidEntry() with { EntityType = new string('a', 81) };

        var result = AuditEvent.Create(entry, FixedClock());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.audit.entity_type_too_long");
    }

    [Fact]
    public void Create_EntityTypeAt80Chars_Succeeds()
    {
        var entry = ValidEntry() with { EntityType = new string('a', 80) };

        var result = AuditEvent.Create(entry, FixedClock());

        result.IsSuccess.Should().BeTrue();
        result.Value.EntityType.Length.Should().Be(80);
    }

    [Fact]
    public void Create_EmptyEntityId_Fails_WithEntityIdRequired()
    {
        var entry = ValidEntry() with { EntityId = Guid.Empty };

        var result = AuditEvent.Create(entry, FixedClock());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.audit.entity_id_required");
    }

    [Fact]
    public void Create_ActionOutOfRange_Fails_WithActionInvalid()
    {
        // The migration's CHECK constraint is action IN (0,1,2,3). Cast
        // an out-of-range byte to force the failure path; the aggregate
        // rejects it BEFORE the row would be rejected by the DB.
        var badAction = (AuditAction)99;
        var entry = ValidEntry() with { Action = badAction };

        var result = AuditEvent.Create(entry, FixedClock());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.audit.action_invalid");
    }

    [Fact]
    public void Create_NullTenantId_Succeeds_CrossTenantAdminScenario()
    {
        // AuditEventEntry.TenantId is Guid? — null means a cross-tenant
        // admin op (the 6d.2 logger enriches from ITenantContext when
        // possible; the aggregate itself accepts null without coercing).
        var entry = ValidEntry() with { TenantId = null };

        var result = AuditEvent.Create(entry, FixedClock());

        result.IsSuccess.Should().BeTrue();
        result.Value.TenantId.Should().BeNull();
    }

    [Fact]
    public void Create_NullUserId_Succeeds_SystemActorScenario()
    {
        // System actors (e.g. webhook handlers) have no caller id.
        var entry = ValidEntry() with { UserId = null };

        var result = AuditEvent.Create(entry, FixedClock());

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().BeNull();
    }

    [Fact]
    public void Create_OccurredAt_UsesClock_NotEntryValue()
    {
        // Per design.md: the aggregate pins OccurredAt from the clock,
        // NOT from the entry. This guarantees that the audit timestamp is
        // consistent with the rest of the system's notion of "now" and
        // that tests can pin time via IClock.
        var entry = ValidEntry() with { OccurredAt = FixedNow.AddDays(-1) };

        var result = AuditEvent.Create(entry, FixedClock());

        result.IsSuccess.Should().BeTrue();
        result.Value.OccurredAt.Should().Be(FixedNow, "the aggregate pins OccurredAt from IClock, ignoring the entry's value.");
    }

    [Fact]
    public void AuditEvent_HasNoPublicMutators_AppendOnlyInvariant()
    {
        // No PUBLIC setters — every property must be effectively read-only
        // from the caller's perspective. EF Core hydration uses the private
        // setters (via the parameterless ctor), but no caller can mutate
        // state after Create. Compliance invariant: "once persisted, immutable".
        var publicSetters = typeof(AuditEvent)
            .GetProperties()
            .Where(p => p.CanWrite && p.SetMethod!.IsPublic)
            .ToArray();

        publicSetters.Should().BeEmpty(
            "AuditEvent is append-only. No property may expose a public setter; mutating an existing row would defeat the compliance invariant.");
    }
}
