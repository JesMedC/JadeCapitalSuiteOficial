using FluentAssertions;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Scanner;
using JadeCapital.Trading.Infrastructure.Audit;
using NSubstitute;

namespace JadeCapital.Trading.UnitTests.Persistence;

/// <summary>
/// Contract pin for <see cref="IScannerFilterRepository"/> (Wave 9, slice 9a.2
/// — sub-scope A coverage extension).
///
/// <para>
/// <b>Why this contract pin exists</b>: Wave 9 §9a.2 extends
/// <see cref="IScannerFilterRepository"/> to inherit from
/// <c>IRepository&lt;ScannerFilter&gt;</c> (see
/// <c>src/3.Shared/JadeCapital.Shared.Kernel/Repository/IRepository.cs</c>),
/// which contributes a <c>DeleteAsync(ScannerFilter, ct)</c> method.
/// However, <see cref="ScannerFilter"/> is a soft-delete-by-flag aggregate:
/// its canonical mutation surface is
/// <c>ScannerFilter.Deactivate(IClock)</c> (flips <c>IsActive = false</c>),
/// NOT a hard delete. The decorator therefore exposes a
/// <b>defensive stub</b> for <c>DeleteAsync</c> that emits an
/// <see cref="AuditAction.Failed"/> audit row BEFORE re-throwing
/// <see cref="NotSupportedException"/> — mirroring the Wave 7 7a.1
/// <c>UserAuditDecorator</c> + Wave 7 7b.1 <c>StrategyAuditDecorator</c>
/// precedent for non-deletable aggregates.
/// </para>
///
/// <para>
/// <b>Why the test is a focused unit test (not an integration test)</b>: the
/// contract pin asserts the decorator's <c>DeleteAsync</c> behavior + the
/// interface's <c>IRepository&lt;ScannerFilter&gt;</c> extension. We
/// deliberately use NSubstitute mocks for the inner repository + audit
/// logger so the test isolates the contract: the inner is never reached,
/// the audit logger captures the failed-action entry, and the throw is
/// verifiable in isolation. The integration test suite
/// (<c>ScannerFilterRepositoryIntegrationTests</c>) covers the full
/// wiring + cross-tenant + diff path against a SQLite in-memory database.
/// </para>
///
/// <para>
/// Both invariant checks below MUST hold for the slice 9a.2 contract:
/// <list type="bullet">
///   <item><b>Interface shape</b> — <see cref="IScannerFilterRepository"/>
///         extends <c>IRepository&lt;ScannerFilter&gt;</c> (the source of
///         the <c>DeleteAsync</c> method). Mirrors the Wave 7 7b.1
///         <c>IStrategyRepository : IRepository&lt;Strategy&gt;</c>
///         + Wave 7 7a.1 <c>IUserRepository : IRepository&lt;User&gt;</c>
///         precedent.</item>
///   <item><b>Defensive stub throws</b> — calling
///         <c>ScannerFilterAuditDecorator.DeleteAsync(filter, ct)</c>
///         throws <see cref="NotSupportedException"/> with the canonical
///         deactivation message. The inner
///         <c>IScannerFilterRepository.DeleteAsync</c> is NEVER reached.
///         The audit log receives an <see cref="AuditAction.Failed"/> entry
///         carrying the rejection reason.</item>
/// </list>
/// </para>
/// </summary>
public class IScannerFilterRepositoryContractTests
{
    /// <summary>
    /// Contract pin — single scenario per tasks.md §9a.2 Phase 1.1.
    /// Pins that:
    /// <list type="number">
    ///   <item><see cref="IScannerFilterRepository"/> inherits from
    ///         <c>IRepository&lt;ScannerFilter&gt;</c> (the source of the
    ///         <c>DeleteAsync</c> defensive stub).</item>
    ///   <item><c>ScannerFilterAuditDecorator.DeleteAsync</c> throws
    ///         <see cref="NotSupportedException"/> with the canonical
    ///         "use Deactivate" message.</item>
    ///   <item>The inner <c>IScannerFilterRepository.DeleteAsync</c> is
    ///         NEVER reached (the contract is "ScannerFilter deletion is
    ///         not supported — use Deactivate (IsActive = false)").</item>
    ///   <item>The audit logger receives an <see cref="AuditAction.Failed"/>
    ///         entry for the attempted misuse.</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task DeleteAsync_IsNotOnInterface_DefensiveStub_Throws()
    {
        // ===== Interface shape pin =====
        // The interface MUST inherit from IRepository<ScannerFilter> — this
        // is the source of the DeleteAsync defensive stub. A regression
        // here (someone removes the inheritance) would silently break the
        // soft-delete-by-flag contract.
        var interfaceType = typeof(IScannerFilterRepository);
        var inheritsFromGenericRepository = interfaceType
            .GetInterfaces()
            .Any(i => i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(JadeCapital.Shared.Kernel.Repository.IRepository<>)
                && i.GetGenericArguments()[0] == typeof(ScannerFilter));

        inheritsFromGenericRepository.Should().BeTrue(
            "IScannerFilterRepository must extend IRepository<ScannerFilter> in Wave 9 §9a.2 — " +
            "this is the source of the DeleteAsync defensive stub that the audit decorator " +
            "wraps with AuditAction.Failed + NotSupportedException. Mirrors the Wave 7 7b.1 " +
            "IStrategyRepository : IRepository<Strategy> + 7a.1 IUserRepository : IRepository<User> " +
            "precedent for non-deletable aggregates.");

        // ===== Defensive stub pin =====
        // Build the decorator with NSubstitute mocks (no SQLite, no DI —
        // the test isolates the contract). The inner mock is configured to
        // throw if DeleteAsync is called on it; the test asserts that the
        // decorator short-circuits with NotSupportedException BEFORE the
        // inner is ever reached.
        var tenant = new StaticTenantContext(
            current: new TenantId(Guid.NewGuid()),
            currentUserId: Guid.NewGuid());
        var clock = new StaticClock(new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.Zero));
        var audit = Substitute.For<IAuditLogger>();
        var inner = Substitute.For<IScannerFilterRepository>();

        // CRITICAL: configure the inner DeleteAsync to throw an
        // unmistakable error if it's ever reached. This proves the
        // decorator's defensive stub short-circuits before the inner is
        // called. If the decorator ever silently forwarded to the inner,
        // this test would fail with the "inner reached" exception.
        inner.DeleteAsync(Arg.Any<ScannerFilter>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException(
                "INNER DELETE REACHED — the defensive stub failed to short-circuit"));

        var decorator = new ScannerFilterAuditDecorator(inner, audit, tenant, clock);

        // Build a representative non-null ScannerFilter aggregate via the
        // canonical factory. The factory stamps the CreatedAt + raises the
        // ScannerFilterCreatedDomainEvent — we don't care about either for
        // this contract pin, only the entity's UserId is consulted by the
        // defensive stub.
        var filter = ScannerFilter.Create(
            userId: tenant.CurrentUserId!.Value,
            name: "Contract Pin Filter",
            minSpread: null,
            maxSpread: null,
            minVolume: null,
            minRiskReward: null,
            window: VolatilityWindow.Daily,
            activeHoursJson: null,
            clock: clock).Value;

        // ===== Act + Assert =====
        var act = async () => await decorator.DeleteAsync(filter, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Deactivate*",
                "the defensive stub message MUST refer to Deactivate (IsActive = false) so " +
                "future maintainers discovering the audit row know the canonical mutation path. " +
                "Mirrors the Wave 7 7b.1 StrategyAuditDecorator + 7a.1 UserAuditDecorator " +
                "message style.");

        // ===== Inner never reached + audit row emitted =====
        await inner.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);

        // The audit logger must have received an AuditAction.Failed entry
        // for the attempted misuse. The Failed row is the entire point of
        // the defensive stub — without it, the misuse would vanish into
        // silence. We assert the action via parameter capture (NSubstitute's
        // Arg.Is + Received.ConfigureAwait false isn't needed here because
        // we only inspect the last call's first argument).
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEventEntry>(e =>
                e.EntityType == nameof(ScannerFilter)
                && e.EntityId == filter.Id
                && e.Action == AuditAction.Failed
                && e.TenantId == tenant.Current!.Value
                && e.UserId == tenant.CurrentUserId),
            Arg.Any<CancellationToken>());
    }

    private sealed class StaticClock : IClock
    {
        public StaticClock(DateTimeOffset now) { UtcNow = now; }
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class StaticTenantContext : ITenantContext
    {
        public StaticTenantContext(TenantId? current, Guid? currentUserId)
        {
            Current = current;
            CurrentUserId = currentUserId;
        }
        public TenantId? Current { get; }
        public Guid? CurrentUserId { get; }
        public bool IsSuperAdmin => false;
    }
}
