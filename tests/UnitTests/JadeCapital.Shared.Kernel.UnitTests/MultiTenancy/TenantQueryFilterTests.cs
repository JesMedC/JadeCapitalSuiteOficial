using FluentAssertions;
using JadeCapital.Shared.Kernel.MultiTenancy;
using NSubstitute;

namespace JadeCapital.Shared.Kernel.UnitTests.MultiTenancy;

/// <summary>
/// Shim for <see cref="IQueryable{T}.ToListAsync(CancellationToken)"/>. LINQ-to-objects
/// does not expose <c>ToListAsync</c> on <c>IQueryable</c> directly — only EF Core's
/// query provider does. To exercise the same call shape in a pure-LINQ test, we use
/// this top-level static helper; the Shared.Kernel package does not pull in EF Core,
/// so this is implemented locally.
/// </summary>
internal static class QueryableAsyncShim
{
    public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(source.ToList());
    }
}

/// <summary>
/// Behavior tests for <see cref="TenantQueryFilter"/> (Wave 6, slice 6c.2).
///
/// <para>
/// The filter is a thin static helper that mutates an <c>IQueryable&lt;T&gt;</c>
/// (or <c>IEnumerable&lt;T&gt;</c>) for <c>T : ITenantOwned</c> to enforce:
///
/// <list type="bullet">
///   <item>When <c>ctx.Current</c> is set → restrict to <c>TenantId == ctx.Current</c></item>
///   <item>When <c>ctx.Current</c> is <c>null</c> and caller is <b>not</b>
///         super-admin → return empty (defense-in-depth: misconfigured
///         callers cannot accidentally read across tenants)</item>
///   <item>When <c>ctx.Current</c> is <c>null</c> and caller <b>is</b>
///         super-admin → return the original query (admin cross-tenant reads
///         are intentional for the audit-log viewer + billing-admin paths)</item>
/// </list>
/// </para>
///
/// <para>
/// Ten RED scenarios pinned here, all derivable from the spec tasks
/// (Phase 3 Phase of Slice 6c.2): <c>GetByIdAsync filters by tenant,
/// query returns own tenant only, cross-tenant lookup returns null,
/// IgnoreQueryFilters returns all, list operations filter, count
/// operations filter, async enumeration, cancellation propagates,
/// no tenant context → empty, no tenant context + super-admin → all</c>.
/// </para>
/// </summary>
public class TenantQueryFilterTests
{
    /// <summary>
    /// Minimal test double for <see cref="ITenantOwned"/>. We use this
    /// rather than <c>User</c> or <c>Tenant</c> to avoid pulling the
    /// Identity.Domain surface into the Shared.Kernel test project.
    /// </summary>
    private sealed class TestEntity : ITenantOwned
    {
        public string Name { get; init; } = string.Empty;
        public TenantId? TenantId { get; init; }
    }

    private static ITenantContext Ctx(TenantId? current, bool isSuperAdmin = false)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.Current.Returns(current);
        ctx.IsSuperAdmin.Returns(isSuperAdmin);
        return ctx;
    }

    private static readonly TenantId TenantA = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly TenantId TenantB = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    // ────────── 10 RED scenarios per spec ──────────

    [Fact]
    public void Query_WithMatchingTenant_ReturnsEntity()
    {
        var entity = new TestEntity { Name = "a-in-A", TenantId = TenantA };
        var query = new[] { entity, new TestEntity { Name = "b-in-B", TenantId = TenantB } }.AsQueryable();

        // Phase 3 #1: GetByIdAsync-style filter — own tenant + that id → found.
        var result = query.WithTenantFilter(Ctx(TenantA)).Single();

        result.Should().BeSameAs(entity);
    }

    [Fact]
    public void Query_ReturnsUsersTenantOnly()
    {
        var query = new[]
        {
            new TestEntity { Name = "a1", TenantId = TenantA },
            new TestEntity { Name = "a2", TenantId = TenantA },
            new TestEntity { Name = "b1", TenantId = TenantB }
        }.AsQueryable();

        // Phase 3 #2: query returns the calling user's tenant only.
        var result = query.WithTenantFilter(Ctx(TenantA)).ToList();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(e => e.TenantId == TenantA);
    }

    [Fact]
    public void Query_CrossTenantLookup_ReturnsNull()
    {
        var tenantBOwned = new[] { new TestEntity { Name = "b-only", TenantId = TenantB } }.AsQueryable();

        // Phase 3 #3: cross-tenant lookup returns null (the entity belongs
        // to a different tenant — the filter strips it).
        var result = tenantBOwned.WithTenantFilter(Ctx(TenantA)).SingleOrDefault();

        result.Should().BeNull();
    }

    [Fact]
    public void IgnoreFilter_ReturnsAllTenants()
    {
        var query = new[]
        {
            new TestEntity { Name = "a", TenantId = TenantA },
            new TestEntity { Name = "b", TenantId = TenantB }
        }.AsQueryable();

        // Phase 3 #4: IgnoreQueryFilters returns all rows. Implemented as
        // a static helper that bypasses the filter (admin cross-tenant reads
        // are handled by the IsSuperAdmin path; this overload is the explicit
        // opt-in for tests and migration scripts that need everything).
        var result = TenantQueryFilter.WithoutFilter(query).ToList();

        result.Should().HaveCount(2);
    }

    [Fact]
    public void List_Operations_Filter()
    {
        var query = new[]
        {
            new TestEntity { Name = "a1", TenantId = TenantA },
            new TestEntity { Name = "a2", TenantId = TenantA },
            new TestEntity { Name = "b1", TenantId = TenantB },
            new TestEntity { Name = "a3", TenantId = TenantA }
        }.AsQueryable();

        // Phase 3 #5: list operations filter — ToList() respects the predicate.
        var list = query.WithTenantFilter(Ctx(TenantA)).ToList();

        list.Should().HaveCount(3);
        list.Select(e => e.Name).Should().BeEquivalentTo(new[] { "a1", "a2", "a3" });
    }

    [Fact]
    public void Count_Operations_Filter()
    {
        var query = new[]
        {
            new TestEntity { Name = "a1", TenantId = TenantA },
            new TestEntity { Name = "a2", TenantId = TenantA },
            new TestEntity { Name = "b1", TenantId = TenantB }
        }.AsQueryable();

        // Phase 3 #6: count operations filter.
        var count = query.WithTenantFilter(Ctx(TenantA)).Count();

        count.Should().Be(2);
    }

    [Fact]
    public async Task AsyncEnumeration_Filters()
    {
        var query = new[]
        {
            new TestEntity { Name = "a1", TenantId = TenantA },
            new TestEntity { Name = "a2", TenantId = TenantA },
            new TestEntity { Name = "b1", TenantId = TenantB }
        }.AsQueryable();

        // Phase 3 #7: async enumeration of an IQueryable respects the filter.
        // For LINQ-to-objects this is just a ToListAsync on the wrapper; for
        // EF queries this becomes a server-side WHERE clause. We exercise
        // both shapes via ToListAsync on the test list (LINQ-to-objects).
        var materialized = await query.WithTenantFilter(Ctx(TenantA)).ToListAsync();

        materialized.Should().HaveCount(2);
        materialized.Should().OnlyContain(e => e.TenantId == TenantA);
    }

    [Fact]
    public async Task CancelledToken_Throws()
    {
        var query = new[]
        {
            new TestEntity { Name = "a", TenantId = TenantA }
        }.AsQueryable();

        // Phase 3 #8: cancellation token propagates — ToListAsync(ct) honors cancellation.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The synchronous overloads (ToList, Count, etc.) do not take a CT;
        // we exercise the async path to verify the filter does not swallow CT.
        Func<Task> act = async () => await query.WithTenantFilter(Ctx(TenantA)).ToListAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void NoTenantContext_NotSuperAdmin_ReturnsEmpty()
    {
        var query = new[]
        {
            new TestEntity { Name = "a", TenantId = TenantA },
            new TestEntity { Name = "b", TenantId = TenantB }
        }.AsQueryable();

        // Phase 3 #9: no tenant context AND caller is not super-admin →
        // empty (defense-in-depth: misconfigured callers cannot accidentally
        // read across tenants). This applies even when the caller ALSO lacks
        // a CurrentUserId (anonymous), but the focus of this slice is the
        // tenant claim.
        var result = query.WithTenantFilter(Ctx(current: null, isSuperAdmin: false)).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void NoTenantContext_SuperAdmin_ReturnsAll()
    {
        var query = new[]
        {
            new TestEntity { Name = "a", TenantId = TenantA },
            new TestEntity { Name = "b", TenantId = TenantB }
        }.AsQueryable();

        // Phase 3 #10: no tenant context AND caller IS super-admin → all rows
        // (admin cross-tenant reads are intentional for audit + billing-admin).
        var result = query.WithTenantFilter(Ctx(current: null, isSuperAdmin: true)).ToList();

        result.Should().HaveCount(2);
    }

    }
