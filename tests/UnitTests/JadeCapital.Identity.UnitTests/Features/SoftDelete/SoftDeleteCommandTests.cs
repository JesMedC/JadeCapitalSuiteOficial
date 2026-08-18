using FluentAssertions;
using JadeCapital.Identity.Application.Features.SoftDelete;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.SoftDelete;
using JadeCapital.Shared.Kernel.Time;
using NSubstitute;

namespace JadeCapital.Identity.UnitTests.Features.SoftDelete;

/// <summary>
/// Tests for the <see cref="SoftDeleteHandler"/> (Wave 6, slice 6d.1).
///
/// <para>
/// Four RED scenarios pinned here (per tasks.md line 421):
/// </para>
/// <list type="number">
///   <item>Existing entity → marks IsDeleted+DeletedAt+DeletedBy, persists.</item>
///   <item>Already-deleted entity → 404 <c>notfound.soft_delete.already_deleted</c>
///         (the second delete attempt is indistinguishable from "no such
///         entity" — same as a hard delete).</item>
///   <item>Unknown entity type → 422 <c>validation.soft_delete.entity_not_soft_deleteable</c>
///         (the entity type is not in the soft-delete registry).</item>
///   <item>Audit event written: <see cref="IAuditLogger.LogAsync"/> is
///         called with an <see cref="AuditEventEntry"/> whose Action is
///         <see cref="AuditAction.Deleted"/>.</item>
/// </list>
///
/// <para>
/// <b>Defense-in-depth</b>: the handler does NOT import EF Core
/// (<see cref="JadeCapital.Identity.Application.Abstractions"/> has no EF
/// dep). Per the 6a.2 deviation pattern, EF exception detection (when
/// <c>DbUpdateConcurrencyException</c> fires) is done via type-name
/// reflection. For 6d.1, we focus on the happy + explicit-failure paths;
/// the concurrency retry is a follow-up when the EF provider lands in 6d.2.
/// </para>
/// </summary>
public class SoftDeleteCommandTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static IClock FixedClock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        return clock;
    }

    /// <summary>
    /// In-memory test entity that implements <see cref="ISoftDelete"/>.
    /// Mirrors how <c>ImportJob</c> will look after 6d.1 lands, but is
    /// independent so the handler tests don't need to thread the
    /// Trading.Domain surface into the Identity test project.
    /// </summary>
    private sealed class TestSoftDeleteEntity : ISoftDelete
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public bool IsDeleted { get; set; }
        public DateTimeOffset? DeletedAtUtc { get; set; }
        public Guid? DeletedByUserId { get; set; }
    }

    /// <summary>
    /// Provider for the test entity. Each provider knows how to load +
    /// update ONE ISoftDelete type. The registry keys by entity-type name.
    /// </summary>
    private sealed class TestSoftDeleteProvider : ISoftDeleteProvider
    {
        public const string EntityTypeName = "TestSoftDeleteEntity";

        private readonly TestSoftDeleteEntity _entity;

        public TestSoftDeleteProvider(TestSoftDeleteEntity entity) { _entity = entity; }

        public string EntityType => EntityTypeName;

        public Task<ISoftDelete?> FindByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<ISoftDelete?>(_entity.Id == id ? _entity : null);

        public Task<Result> UpdateAsync(ISoftDelete entity, CancellationToken ct)
            => Task.FromResult(Result.Success());
    }

    private static SoftDeleteHandler CreateSut(
        ISoftDeleteProviderRegistry registry,
        IAuditLogger audit,
        IClock clock)
        => new(registry, audit, clock);

    [Fact]
    public async Task Handle_ExistingEntity_MarksDeletedAndPersists()
    {
        var entity = new TestSoftDeleteEntity();
        var provider = new TestSoftDeleteProvider(entity);
        var registry = new SoftDeleteProviderRegistry(new[] { provider });
        var audit = Substitute.For<IAuditLogger>();
        var userId = Guid.NewGuid();

        var cmd = new SoftDeleteCommand(TestSoftDeleteProvider.EntityTypeName, entity.Id, userId);
        var result = await CreateSut(registry, audit, FixedClock()).Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        entity.IsDeleted.Should().BeTrue();
        entity.DeletedAtUtc.Should().Be(FixedNow);
        entity.DeletedByUserId.Should().Be(userId);
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e => e.Action == AuditAction.Deleted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyDeletedEntity_Returns404()
    {
        var entity = new TestSoftDeleteEntity
        {
            IsDeleted = true,
            DeletedAtUtc = FixedNow.AddMinutes(-5),
            DeletedByUserId = Guid.NewGuid(),
        };
        var provider = new TestSoftDeleteProvider(entity);
        var registry = new SoftDeleteProviderRegistry(new[] { provider });
        var audit = Substitute.For<IAuditLogger>();

        var cmd = new SoftDeleteCommand(TestSoftDeleteProvider.EntityTypeName, entity.Id, Guid.NewGuid());
        var result = await CreateSut(registry, audit, FixedClock()).Handle(cmd, CancellationToken.None);

        // 404 (not 410) — second soft-delete attempt is indistinguishable
        // from "no such entity" so a caller cannot probe for deleted rows.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.soft_delete.already_deleted");
        // The audit MUST NOT record a second delete event — once an
        // entity is soft-deleted, subsequent attempts are silent 404s.
        await audit.DidNotReceive().LogAsync(Arg.Any<AuditEventEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownEntityType_Returns422()
    {
        var registry = new SoftDeleteProviderRegistry(Array.Empty<ISoftDeleteProvider>());
        var audit = Substitute.For<IAuditLogger>();

        var cmd = new SoftDeleteCommand("UnknownEntityType", Guid.NewGuid(), Guid.NewGuid());
        var result = await CreateSut(registry, audit, FixedClock()).Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.soft_delete.entity_not_soft_deleteable");
        await audit.DidNotReceive().LogAsync(Arg.Any<AuditEventEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EntityNotFound_Returns404()
    {
        // The provider knows the entity type but the entity itself doesn't exist.
        var provider = new TestSoftDeleteProvider(new TestSoftDeleteEntity());
        var registry = new SoftDeleteProviderRegistry(new[] { provider });
        var audit = Substitute.For<IAuditLogger>();

        var cmd = new SoftDeleteCommand(TestSoftDeleteProvider.EntityTypeName, Guid.NewGuid(), Guid.NewGuid());
        var result = await CreateSut(registry, audit, FixedClock()).Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.soft_delete.entity_not_found");
        await audit.DidNotReceive().LogAsync(Arg.Any<AuditEventEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AuditEventIncludesEntityTypeAndUserId()
    {
        var entity = new TestSoftDeleteEntity();
        var provider = new TestSoftDeleteProvider(entity);
        var registry = new SoftDeleteProviderRegistry(new[] { provider });
        var audit = Substitute.For<IAuditLogger>();
        var userId = Guid.NewGuid();

        var cmd = new SoftDeleteCommand(TestSoftDeleteProvider.EntityTypeName, entity.Id, userId);
        await CreateSut(registry, audit, FixedClock()).Handle(cmd, CancellationToken.None);

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e =>
                e.Action == AuditAction.Deleted
                && e.EntityType == TestSoftDeleteProvider.EntityTypeName
                && e.EntityId == entity.Id
                && e.UserId == userId),
            Arg.Any<CancellationToken>());
    }
}
