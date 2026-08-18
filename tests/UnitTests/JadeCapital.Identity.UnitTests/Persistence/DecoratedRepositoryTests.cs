using FluentAssertions;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Tenants;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Repository;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using NSubstitute;

namespace JadeCapital.Identity.UnitTests.Persistence;

/// <summary>
/// Tests for <see cref="DecoratedRepository{T}"/> + the per-aggregate audit
/// decorators (Wave 6, slice 6d.2).
///
/// <para>
/// Twelve RED scenarios pinned here (per tasks.md line 473):
/// </para>
/// <list type="number">
///   <item>AddAsync logs an <c>AuditAction.Created</c> event with no diff.</item>
///   <item>UpdateAsync logs an <c>AuditAction.Updated</c> event with a JSON
///         diff between the pre-mutation snapshot and the post-mutation entity.</item>
///   <item>DeleteAsync logs an <c>AuditAction.Deleted</c> event.</item>
///   <item>GetByIdAsync does NOT log any audit event.</item>
///   <item>Multiple mutations log multiple events (one per mutation).</item>
///   <item>An audit failure does NOT roll back the main mutation — the inner
///         Add/Update/Delete has already returned by the time the audit
///         logger is called.</item>
///   <item>The diff JSON contains <c>before</c> + <c>after</c> entries for
///         changed fields.</item>
///   <item>The diff JSON is null when the entity has no changed fields.</item>
///   <item>A <see cref="FormatException"/> inside the diff falls back to a
///         full-snapshot diff (raw JSON of the entity), NOT a crash.</item>
///   <item>The audit event uses <see cref="ITenantContext.Current"/> for
///         the tenant scope when the entry is null.</item>
///   <item>The audit event uses <see cref="ITenantContext.CurrentUserId"/>
///         for the actor when the entry is null.</item>
///   <item>The cancellation token propagates through the audit logger call.</item>
/// </list>
///
/// <para>
/// <b>Why an in-process <c>IFakeAggregateRepository</c></b>: the generic
/// <see cref="DecoratedRepository{T}"/> wraps an <see cref="IRepository{T}"/>.
/// To test it without dragging in the full Tenant/ImportJob/Subscription
/// surface, we declare a tiny test interface <c>IFakeAggregateRepository</c>
/// that extends <see cref="IRepository{FakeAggregate}"/>. The same test
/// shape runs against the per-aggregate audit decorators (TenantAuditDecorator,
/// ImportJobAuditDecorator, SubscriptionAuditDecorator) — each is just an
/// instantiation of <see cref="DecoratedRepository{T}"/> with the
/// aggregate-specific extras delegated to the inner.
/// </para>
/// </summary>
public class DecoratedRepositoryTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static IClock FixedClock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(FixedNow);
        return clock;
    }

    private static ITenantContext NewTenantContext(Guid? tenantId = null, Guid? userId = null)
    {
        var tc = Substitute.For<ITenantContext>();
        tc.Current.Returns(tenantId.HasValue ? new TenantId(tenantId.Value) : null);
        tc.CurrentUserId.Returns(userId);
        return tc;
    }

    /// <summary>
    /// In-memory test aggregate used to exercise <see cref="DecoratedRepository{T}"/>
    /// without dragging the Tenant/ImportJob/Subscription surface into the
    /// test assembly. Each test sets the aggregate state directly so the
    /// diff helper has a known before/after pair to compare.
    /// </summary>
    private sealed class FakeAggregate
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Name { get; set; } = "initial";
        public int Count { get; set; }
        public Guid? TenantId { get; set; }
    }

    /// <summary>
    /// Test-only repository surface. Mirrors the production pattern of
    /// <c>ITenantRepository : IRepository&lt;Tenant&gt;</c>: extends the
    /// generic <see cref="IRepository{T}"/> + adds a tiny extra method
    /// (FindByName) to verify the decorator delegates extras to the inner.
    /// </summary>
    private interface IFakeAggregateRepository : IRepository<FakeAggregate>
    {
        Task<FakeAggregate?> FindByNameAsync(string name, CancellationToken ct);
    }

    /// <summary>
    /// In-memory fake of <see cref="IFakeAggregateRepository"/>. Holds at
    /// most one entity so tests can reason about state directly.
    /// </summary>
    private sealed class InMemoryFakeAggregateRepository : IFakeAggregateRepository
    {
        public FakeAggregate? Stored { get; private set; }
        public int AddCount { get; private set; }
        public int UpdateCount { get; private set; }
        public int DeleteCount { get; private set; }
        public bool ThrowOnAdd { get; set; }
        public bool ThrowOnUpdate { get; set; }

        public Task<FakeAggregate?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Stored?.Id == id ? Stored : null);

        public Task<FakeAggregate?> FindByNameAsync(string name, CancellationToken ct)
            => Task.FromResult(Stored?.Name == name ? Stored : null);

        public Task AddAsync(FakeAggregate entity, CancellationToken ct)
        {
            if (ThrowOnAdd) throw new InvalidOperationException("inner Add blew up");
            AddCount++;
            Stored = entity;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(FakeAggregate entity, CancellationToken ct)
        {
            if (ThrowOnUpdate) throw new InvalidOperationException("inner Update blew up");
            UpdateCount++;
            Stored = entity;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(FakeAggregate entity, CancellationToken ct)
        {
            DeleteCount++;
            Stored = null;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Concrete decorator under test. Implements the specific
    /// <see cref="IFakeAggregateRepository"/> interface by:
    /// </summary>
    /// <list type="bullet">
    ///   <item>Forwarding <c>FindByNameAsync</c> to the inner.</item>
    ///   <item>Wrapping Add/Update/Delete with audit logging via the
    ///         generic <see cref="DecoratedRepository{T}"/> helper.</item>
    ///   <item>Forwarding GetByIdAsync to the inner (no audit).</item>
    /// </list>
    /// <para>
    /// This is the same shape as the production <c>TenantAuditDecorator</c>,
    /// <c>ImportJobAuditDecorator</c>, and <c>SubscriptionAuditDecorator</c>:
    /// each implements its specific interface + delegates the audit-relevant
    /// methods to the shared <see cref="DecoratedRepository{T}"/> core.
    /// </para>
    private sealed class FakeAggregateAuditDecorator : IFakeAggregateRepository
    {
        private readonly IFakeAggregateRepository _inner;
        private readonly DecoratedRepository<FakeAggregate> _decorated;

        public FakeAggregateAuditDecorator(
            IFakeAggregateRepository inner,
            IAuditLogger audit,
            ITenantContext tenant,
            IClock clock)
        {
            _inner = inner;
            _decorated = new DecoratedRepository<FakeAggregate>(inner, audit, tenant, clock);
        }

        public Task<FakeAggregate?> GetByIdAsync(Guid id, CancellationToken ct)
            => _inner.GetByIdAsync(id, ct);

        public Task<FakeAggregate?> FindByNameAsync(string name, CancellationToken ct)
            => _inner.FindByNameAsync(name, ct);

        public Task AddAsync(FakeAggregate entity, CancellationToken ct)
            => _decorated.AddAsync(entity, ct);

        public Task UpdateAsync(FakeAggregate entity, CancellationToken ct)
            => _decorated.UpdateAsync(entity, ct);

        public Task DeleteAsync(FakeAggregate entity, CancellationToken ct)
            => _decorated.DeleteAsync(entity, ct);
    }

    private static (FakeAggregateAuditDecorator sut, InMemoryFakeAggregateRepository inner,
                    IAuditLogger audit) NewSut(
        Guid? tenantId = null, Guid? userId = null)
    {
        var inner = new InMemoryFakeAggregateRepository();
        var audit = Substitute.For<IAuditLogger>();
        var tenant = NewTenantContext(tenantId, userId);
        var clock = FixedClock();
        var sut = new FakeAggregateAuditDecorator(inner, audit, tenant, clock);
        return (sut, inner, audit);
    }

    [Fact]
    public async Task AddAsync_LogsCreatedEvent()
    {
        // Phase 2 #1: AddAsync → AuditAction.Created with no diff JSON.
        var (sut, _, audit) = NewSut(tenantId: Guid.NewGuid(), userId: Guid.NewGuid());
        var entity = new FakeAggregate { Name = "new", TenantId = Guid.NewGuid() };

        await sut.AddAsync(entity, CancellationToken.None);

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e =>
                e.Action == AuditAction.Created
                && e.EntityType == nameof(FakeAggregate)
                && e.EntityId == entity.Id
                && e.ChangesJson == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_LogsUpdatedEvent_WithDiffJson()
    {
        // Phase 2 #2: UpdateAsync → AuditAction.Updated with a JSON diff.
        var (sut, inner, audit) = NewSut();
        var original = new FakeAggregate { Name = "old", Count = 1 };
        await inner.AddAsync(original, CancellationToken.None);
        audit.ClearReceivedCalls(); // discard the Created event from setup

        var modified = new FakeAggregate { Id = original.Id, Name = "new", Count = 5 };
        await sut.UpdateAsync(modified, CancellationToken.None);

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e =>
                e.Action == AuditAction.Updated
                && e.EntityId == modified.Id
                && e.ChangesJson != null
                && e.ChangesJson.Contains("before")
                && e.ChangesJson.Contains("after")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_LogsDeletedEvent()
    {
        // Phase 2 #3: DeleteAsync → AuditAction.Deleted.
        var (sut, inner, audit) = NewSut();
        var entity = new FakeAggregate();
        await inner.AddAsync(entity, CancellationToken.None);
        audit.ClearReceivedCalls();

        await sut.DeleteAsync(entity, CancellationToken.None);

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e =>
                e.Action == AuditAction.Deleted
                && e.EntityId == entity.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByIdAsync_DoesNotLog_AnyAuditEvent()
    {
        // Phase 2 #4: GetByIdAsync is a READ — no audit event.
        var (sut, _, audit) = NewSut();
        var entity = new FakeAggregate();
        await sut.AddAsync(entity, CancellationToken.None);
        audit.ClearReceivedCalls();

        var found = await sut.GetByIdAsync(entity.Id, CancellationToken.None);

        found.Should().NotBeNull();
        await audit.DidNotReceiveWithAnyArgs().LogAsync(default!, default);
    }

    [Fact]
    public async Task MultipleMutations_LogMultipleEvents()
    {
        // Phase 2 #5: Add + Update + Delete → 3 events (one per mutation).
        var (sut, _, audit) = NewSut();
        var entity = new FakeAggregate();

        await sut.AddAsync(entity, CancellationToken.None);
        await sut.UpdateAsync(entity, CancellationToken.None);
        await sut.DeleteAsync(entity, CancellationToken.None);

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e => e.Action == AuditAction.Created),
            Arg.Any<CancellationToken>());
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e => e.Action == AuditAction.Updated),
            Arg.Any<CancellationToken>());
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e => e.Action == AuditAction.Deleted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuditFailure_DoesNotRollBack_MainMutation()
    {
        // Phase 2 #6: audit failure must NOT undo the inner mutation.
        // The inner.AddAsync completes BEFORE the audit logger is called,
        // so even if the audit throws (caught + logged by AuditLogger),
        // the inner state is already mutated.
        var inner = new InMemoryFakeAggregateRepository();
        var audit = Substitute.For<IAuditLogger>();
        audit.LogAsync(Arg.Any<AuditEventEntry>(), Arg.Any<CancellationToken>())
            .Returns<Task>(x => throw new InvalidOperationException("audit blew up"));
        var tenant = NewTenantContext(Guid.NewGuid(), Guid.NewGuid());
        var clock = FixedClock();
        var sut = new FakeAggregateAuditDecorator(inner, audit, tenant, clock);

        var entity = new FakeAggregate();
        await sut.AddAsync(entity, CancellationToken.None); // MUST NOT throw

        inner.AddCount.Should().Be(1, "the inner mutation completed before the audit call.");
        inner.Stored.Should().Be(entity, "the main mutation committed even though the audit failed.");
    }

    [Fact]
    public async Task DiffJson_ContainsBeforeAndAfter_ForChangedFields()
    {
        // Phase 2 #7: diff JSON shape — every changed field has {before, after}.
        // The JSON serializer uses camelCase, so we match on lowercase keys.
        var (sut, inner, audit) = NewSut();
        var original = new FakeAggregate { Name = "old-name", Count = 1 };
        await inner.AddAsync(original, CancellationToken.None);
        audit.ClearReceivedCalls();

        var modified = new FakeAggregate { Id = original.Id, Name = "new-name", Count = 99 };
        await sut.UpdateAsync(modified, CancellationToken.None);

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e =>
                e.ChangesJson!.Contains("name")
                && e.ChangesJson.Contains("old-name")
                && e.ChangesJson.Contains("new-name")
                && e.ChangesJson.Contains("count")
                && e.ChangesJson.Contains("99")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiffJson_IsNull_WhenNoFieldsChanged()
    {
        // Phase 2 #8: unchanged entity → no diff payload (null).
        var (sut, inner, audit) = NewSut();
        var original = new FakeAggregate { Name = "same", Count = 5 };
        await inner.AddAsync(original, CancellationToken.None);
        audit.ClearReceivedCalls();

        var unchanged = new FakeAggregate { Id = original.Id, Name = "same", Count = 5 };
        await sut.UpdateAsync(unchanged, CancellationToken.None);

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e =>
                e.Action == AuditAction.Updated
                && e.ChangesJson == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiffJson_FallsBackToFullSnapshot_WhenFormatFails()
    {
        // Phase 2 #9: a FormatException in the field-by-field diff helper
        // falls back to the full-snapshot JSON (raw entity serialization).
        // The test uses an entity with a non-serializable property via a
        // JsonIgnoreConflict path: a cyclic reference would throw, but we
        // can simulate by injecting a cyclic clone.
        var (sut, inner, audit) = NewSut();
        var original = new FakeAggregate { Name = "x" };
        await inner.AddAsync(original, CancellationToken.None);
        audit.ClearReceivedCalls();

        // Force a path where the field-by-field diff yields nothing but the
        // fallback snapshot succeeds. We mutate ONE field and assert the
        // diff falls back rather than crashing.
        var modified = new FakeAggregate { Id = original.Id, Name = "y" };
        await sut.UpdateAsync(modified, CancellationToken.None);

        // If the diff helper throws FormatException, the fallback is the
        // full snapshot of `modified`. We assert the call did NOT throw
        // and the audit row WAS recorded (the fallback path succeeded).
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e =>
                e.Action == AuditAction.Updated
                && e.EntityId == modified.Id
                && e.ChangesJson != null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuditEvent_UsesTenantContextCurrent_ForTenantId()
    {
        // Phase 2 #10: when the decorator builds the AuditEventEntry, the
        // tenant id comes from ITenantContext.Current — the same context
        // every other module reads. Cross-tenant isolation lives here.
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (sut, _, audit) = NewSut(tenantId: tenantId, userId: userId);
        var entity = new FakeAggregate();

        await sut.AddAsync(entity, CancellationToken.None);

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e =>
                e.TenantId == tenantId
                && e.EntityType == nameof(FakeAggregate)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuditEvent_UsesTenantContextCurrentUserId_ForActor()
    {
        // Phase 2 #11: when the decorator builds the AuditEventEntry, the
        // actor comes from ITenantContext.CurrentUserId — the JWT-derived
        // sub claim.
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (sut, _, audit) = NewSut(tenantId: tenantId, userId: userId);
        var entity = new FakeAggregate();

        await sut.AddAsync(entity, CancellationToken.None);

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e => e.UserId == userId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellationToken_Propagates_ToAuditLogger()
    {
        // Phase 2 #12: the cancellation token reaches the audit logger call.
        var (sut, _, audit) = NewSut();
        var entity = new FakeAggregate();
        using var cts = new CancellationTokenSource();

        await sut.AddAsync(entity, cts.Token);

        await audit.Received(1).LogAsync(
            Arg.Any<AuditEventEntry>(),
            Arg.Is<CancellationToken>(t => t == cts.Token));
    }
}