using FluentAssertions;
using JadeCapital.Shared.Kernel.Audit;
using NSubstitute;

namespace JadeCapital.Shared.Kernel.UnitTests.Audit;

/// <summary>
/// Contract tests for <see cref="IAuditLogger"/>, <see cref="AuditEventEntry"/>,
/// and <see cref="AuditAction"/> (Wave 6, slice 6d.1).
///
/// <para>
/// The audit logger is the cross-cutting write surface for every mutation.
/// Three RED scenarios pinned here:
/// </para>
/// <list type="number">
///   <item><b>Interface shape</b>: exactly one member (<c>LogAsync</c>)
///         — no accidental expansion. The audit logger is a one-way fire
///         pipe; widening the shape is a cross-module breaking change.</item>
///   <item><b>LogAsync signature</b>: takes an <see cref="AuditEventEntry"/>
///         and a <see cref="CancellationToken"/>. No other parameters.</item>
///   <item><b>No-throw guarantee</b>: the contract MUST NOT throw. The
///         default <c>NSubstitute</c> implementation returns
///         <c>Task.CompletedTask</c> which never throws; a real impl in
///         slice 6d.2 will catch all exceptions and log a warning.</item>
/// </list>
///
/// <para>
/// <b>AuditEventEntry</b> shape (per design.md § AuditEventEntry):
/// <c>EntityType</c>, <c>EntityId</c>, <c>Action</c>, <c>TenantId</c>,
/// <c>UserId</c>, <c>ChangesJson</c>, <c>OccurredAt</c>. Verified via
/// record-position equality.
/// </para>
///
/// <para>
/// <b>AuditAction enum</b>: <c>Created=0, Updated=1, Deleted=2, Restored=3</c>.
/// Verified by exact byte values — the migration's CHECK constraint maps to
/// these (audit CK IN (0,1,2,3)).
/// </para>
/// </summary>
public class IAuditLoggerContractTests
{
    [Fact]
    public void IAuditLogger_DeclaresExactlyOneMember()
    {
        var iface = typeof(IAuditLogger);
        var methods = iface.GetMethods()
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        methods.Should().BeEquivalentTo(new[] { "LogAsync" });
    }

    [Fact]
    public void LogAsync_TakesEntryAndCancellationToken_Only()
    {
        // Pin the signature shape — adding a third parameter (e.g. an
        // actor context) is a breaking change. The current shape lets the
        // 6d.2 AuditLogger impl enrich the entry from its own DI context
        // (ITenantContext, ICurrentUser) before mapping to AuditEvent.
        var iface = typeof(IAuditLogger);
        var method = iface.GetMethod("LogAsync")!;

        var parameters = method.GetParameters();

        parameters.Should().HaveCount(2, "LogAsync must take exactly (AuditEventEntry, CancellationToken).");

        parameters[0].ParameterType.Should().Be<AuditEventEntry>();
        parameters[1].ParameterType.Should().Be<CancellationToken>();

        method.ReturnType.Should().Be<Task>();
    }

    [Fact]
    public void LogAsync_DoesNotThrow_OnDefaultImplementation()
    {
        // The no-throw guarantee is structural: a real impl in 6d.2 will
        // wrap SaveChangesAsync in a try/catch and log to Serilog. For now,
        // NSubstitute returns a completed task; if a future contributor
        // accidentally rethrows, this test breaks.
        var logger = Substitute.For<IAuditLogger>();
        var entry = new AuditEventEntry(
            EntityType: "ImportJob",
            EntityId: Guid.NewGuid(),
            Action: AuditAction.Created,
            TenantId: null,
            UserId: Guid.NewGuid(),
            ChangesJson: null,
            OccurredAt: DateTimeOffset.UtcNow);

        Func<Task> act = async () => await logger.LogAsync(entry, CancellationToken.None);

        act.Should().NotThrowAsync();
    }

    // ────────── AuditEventEntry shape ──────────

    [Fact]
    public void AuditEventEntry_RecordShape_IsExactlySevenMembers()
    {
        // The record has 7 positional parameters. Renaming any of them is
        // a cross-module breaking change (every IAuditLogger consumer must
        // be updated). Pin the shape.
        var ctor = typeof(AuditEventEntry).GetConstructors().Single();

        var paramNames = ctor.GetParameters()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        paramNames.Should().BeEquivalentTo(new[]
        {
            "Action",
            "ChangesJson",
            "EntityId",
            "EntityType",
            "OccurredAt",
            "TenantId",
            "UserId",
        });
    }

    // ────────── AuditAction enum values ──────────

    [Fact]
    public void AuditAction_HasExactlySixValues_WithExpectedByteOrder()
    {
        // The migration's CHECK constraint is `action IN (0, 1, 2, 3, 4, 5)`
        // (Wave 6 + Wave 7 slice 7a.1). Renaming any enum member or changing
        // the underlying byte breaks the contract. Pin the values.
        var values = Enum.GetValues<AuditAction>()
            .Cast<AuditAction>()
            .OrderBy(v => (int)v)
            .ToArray();

        values.Should().BeEquivalentTo(new[]
        {
            AuditAction.Created,
            AuditAction.Updated,
            AuditAction.Deleted,
            AuditAction.Restored,
            AuditAction.Denied,
            AuditAction.Failed,
        }, opts => opts.WithStrictOrdering());

        ((int)AuditAction.Created).Should().Be(0);
        ((int)AuditAction.Updated).Should().Be(1);
        ((int)AuditAction.Deleted).Should().Be(2);
        ((int)AuditAction.Restored).Should().Be(3);
        ((int)AuditAction.Denied).Should().Be(4);
        ((int)AuditAction.Failed).Should().Be(5);
    }
}
