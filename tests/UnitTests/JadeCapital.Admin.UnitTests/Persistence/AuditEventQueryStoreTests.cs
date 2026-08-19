using FluentAssertions;
using JadeCapital.Admin.Application.Features.Audit;
using JadeCapital.Admin.Infrastructure.Persistence;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Admin.UnitTests.Persistence;

/// <summary>
/// Integration tests for <see cref="AuditEventQueryStore"/> (Wave 9, slice 9b.1).
///
/// <para>
/// Uses SQLite in-memory against the production <see cref="AuditDbContext"/>
/// + <c>AuditEventConfiguration</c> — the config is SQLite-compatible
/// (no Npgsql-specific converters; the <c>changes</c> JSONB column is
/// stored as TEXT on SQLite). Mirrors the Wave 6 6d.2
/// <c>AuditLoggerTests</c> pattern: an in-process provider exercises
/// the EF query path without a real Postgres or Testcontainers dependency.
/// </para>
///
/// <para>
/// Three RED scenarios pinned here (per tasks.md §9b.1 Phase 1.1 + design.md
/// §"Read-side architecture"):
/// <list type="number">
///   <item><b>No-filter list returns newest-first</b> — keyset on
///         <c>(occurred_at DESC, id DESC)</c>; rows ordered by
///         <see cref="AuditEvent.OccurredAt"/> descending.</item>
///   <item><b>EntityType filter narrows the result</b> — only rows matching
///         the supplied entity_type string are returned.</item>
///   <item><b>Compound UserId + TenantId filter narrows the result</b> —
///         the AND conjunction of both filters is applied; rows matching
///         both are returned.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Note</b>: the production store reads <c>AuditDbContext.AuditEvents</c>
/// directly (no extra abstraction). The test seeds via the same
/// <c>AuditDbContext</c> + the production <c>AuditEventConfiguration</c>
/// so the EF model + column names match exactly.
/// </para>
/// </summary>
public class AuditEventQueryStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AuditEventQueryStoreTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private AuditDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(_connection)
            .Options;
        var db = new AuditDbContext(opts);
        db.Database.EnsureCreated();
        return db;
    }

    private static AuditEvent SeedEvent(
        string entityType,
        AuditAction action,
        DateTimeOffset occurredAt,
        Guid? userId = null,
        Guid? tenantId = null,
        string? changesJson = null)
    {
        return AuditEvent.FromTrusted(
            id: Guid.NewGuid(),
            entityType: entityType,
            entityId: Guid.NewGuid(),
            action: action,
            tenantId: tenantId,
            userId: userId,
            changesJson: changesJson,
            occurredAt: occurredAt);
    }

    [Fact]
    public async Task ListAsync_NoFilters_ReturnsNewestFirst()
    {
        // Phase 1.1 #1: with no filters, the store returns rows ordered
        // by (occurred_at DESC, id DESC). Seed 3 events at distinct
        // timestamps; assert the returned order matches OccurredAt DESC.
        var db = BuildDb();
        var t0 = new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
        var oldest = SeedEvent("TradeAttachment", AuditAction.Updated, t0);
        var middle = SeedEvent("TradeAttachment", AuditAction.Updated, t0.AddHours(1));
        var newest = SeedEvent("TradeAttachment", AuditAction.Updated, t0.AddHours(2));
        db.AuditEvents.AddRange(oldest, middle, newest);
        await db.SaveChangesAsync();

        var sut = new AuditEventQueryStore(db);
        var page = await sut.ListAsync(
            new ListAuditEventsQuery(
                EntityType: null, Action: null, UserId: null, TenantId: null,
                From: null, To: null, Cursor: null, Limit: 50),
            CancellationToken.None);

        page.Items.Should().HaveCount(3);
        page.Items[0].OccurredAt.Should().Be(newest.OccurredAt,
            "the store returns newest-first by OccurredAt DESC.");
        page.Items[1].OccurredAt.Should().Be(middle.OccurredAt);
        page.Items[2].OccurredAt.Should().Be(oldest.OccurredAt);
        page.HasMore.Should().BeFalse();
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_WithEntityTypeFilter_AppliesFilter()
    {
        // Phase 1.1 #2: an EntityType filter narrows the result to only
        // rows whose EntityType matches. Seed 3 events across 2 entity
        // types; filter by one; assert only the matching rows are returned.
        var db = BuildDb();
        var t0 = new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
        var tradeEvt1 = SeedEvent("TradeAttachment", AuditAction.Updated, t0);
        var tradeEvt2 = SeedEvent("TradeAttachment", AuditAction.Updated, t0.AddHours(1));
        var userEvt = SeedEvent("User", AuditAction.Created, t0.AddHours(2));
        db.AuditEvents.AddRange(tradeEvt1, tradeEvt2, userEvt);
        await db.SaveChangesAsync();

        var sut = new AuditEventQueryStore(db);
        var page = await sut.ListAsync(
            new ListAuditEventsQuery(
                EntityType: "TradeAttachment", Action: null, UserId: null, TenantId: null,
                From: null, To: null, Cursor: null, Limit: 50),
            CancellationToken.None);

        page.Items.Should().HaveCount(2,
            "the EntityType filter narrows the result to the 2 TradeAttachment rows.");
        page.Items.Should().OnlyContain(e => e.EntityType == "TradeAttachment",
            "all returned rows must match the supplied EntityType.");
    }

    [Fact]
    public async Task ListAsync_WithUserIdAndTenantIdFilter_AppliesCompoundFilter()
    {
        // Phase 1.1 #3: a compound UserId + TenantId filter narrows the
        // result to rows matching BOTH. Seed 4 events across 2 users
        // × 2 tenants; filter by one user/tenant pair; assert only the
        // matching row is returned.
        var db = BuildDb();
        var t0 = new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        // 4 events: (userA, tenant1), (userA, tenant2), (userB, tenant1), (userB, tenant2)
        db.AuditEvents.AddRange(
            SeedEvent("TradeAttachment", AuditAction.Updated, t0,                userId: userA, tenantId: tenant1),
            SeedEvent("TradeAttachment", AuditAction.Updated, t0.AddHours(1),    userId: userA, tenantId: tenant2),
            SeedEvent("TradeAttachment", AuditAction.Updated, t0.AddHours(2),    userId: userB, tenantId: tenant1),
            SeedEvent("TradeAttachment", AuditAction.Updated, t0.AddHours(3),    userId: userB, tenantId: tenant2));
        await db.SaveChangesAsync();

        var sut = new AuditEventQueryStore(db);
        var page = await sut.ListAsync(
            new ListAuditEventsQuery(
                EntityType: null, Action: null,
                UserId: userA, TenantId: tenant1,
                From: null, To: null, Cursor: null, Limit: 50),
            CancellationToken.None);

        page.Items.Should().HaveCount(1,
            "the compound UserId + TenantId filter narrows the result to the single matching row.");
        page.Items[0].UserId.Should().Be(userA);
        page.Items[0].TenantId.Should().Be(tenant1);
    }
}
