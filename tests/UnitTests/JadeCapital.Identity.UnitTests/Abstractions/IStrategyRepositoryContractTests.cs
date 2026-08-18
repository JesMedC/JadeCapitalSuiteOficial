using System.Reflection;
using FluentAssertions;
using JadeCapital.Shared.Kernel.Repository;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Strategies;
using JadeCapital.Trading.Infrastructure.Persistence;

namespace JadeCapital.Identity.UnitTests.Abstractions;

/// <summary>
/// Wave 7 slice 7b.1 — <see cref="IStrategyRepository"/> contract surgery.
///
/// <para>
/// Slice 7b.1 extends <see cref="IStrategyRepository"/> to
/// <see cref="IRepository{T}"/> + adds a
/// <c>DeleteAsync(Strategy, CancellationToken)</c> method. The contract is:
/// </para>
/// <list type="bullet">
///   <item>Canonical Strategy mutation surface is
///   <c>Strategy.Update(...)</c> + <c>Strategy.Deactivate(IClock)</c> +
///   <c>Strategy.Activate(IClock)</c>. There is NO domain op that
///   hard-deletes a Strategy (the deactivation IS the soft-delete — flips
///   <c>IsActive</c> from <c>true</c> to <c>false</c>).</item>
///   <item>The decorator (<see cref="JadeCapital.Trading.Infrastructure.Audit.StrategyAuditDecorator"/>)
///   emits <see cref="Audit.AuditAction.Failed"/> audit row + re-throws
///   <see cref="NotSupportedException"/> BEFORE the inner is reached.</item>
///   <item>The inner (<see cref="StrategyRepository"/>) is a defensive
///   STUB that throws <see cref="NotSupportedException"/> with the
///   canonical message <c>"Strategy deletion happens via Deactivation,
///   not direct delete"</c> — so the failure mode is identical whether
///   the caller accidentally bypasses the decorator OR the decorator is
///   misconfigured.</item>
/// </list>
///
/// Two RED scenarios pinned here:
/// <list type="number">
///   <item><c>IStrategyRepository.DeleteAsync(Strategy, CancellationToken)</c>
///         method exists on the interface (reflection check; walks
///         <see cref="IRepository{T}"/> base if needed).</item>
///   <item>The concrete <see cref="StrategyRepository.DeleteAsync(Strategy, CancellationToken)"/>
///         STUB throws <see cref="NotSupportedException"/> with the
///         canonical message that mentions <c>"Deactivate"</c>.</item>
/// </list>
/// </summary>
public class IStrategyRepositoryContractTests
{
    [Fact]
    public void IStrategyRepository_Exposes_DeleteAsyncMethod()
    {
        // Phase 2 #1: DeleteAsync(Strategy, CancellationToken) exists on the
        // interface (or its IRepository<Strategy> base). Reflection walks the
        // interface chain because Type.GetMethod with explicit parameter
        // types does NOT flatten inherited interface members (same pattern
        // as 7a.1 IUserRepositoryContractTests).
        var method = FindMethodOnInterfaceOrBases(
            typeof(IStrategyRepository),
            "DeleteAsync",
            typeof(Strategy), typeof(CancellationToken));

        method.Should().NotBeNull(
            "IStrategyRepository (or its IRepository<Strategy> base) must expose " +
            "DeleteAsync(Strategy, CancellationToken) — the slice 7b.1 surgery " +
            "satisfies the interface contract via inheritance or direct " +
            "declaration.");
        method!.ReturnType.Should().Be<Task>(
            "DeleteAsync is a Task-returning async method (no result payload).");
    }

    private static System.Reflection.MethodInfo? FindMethodOnInterfaceOrBases(
        Type iface, string name, params Type[] parameterTypes)
    {
        var current = iface;
        while (current is not null)
        {
            var m = current.GetMethod(name, parameterTypes);
            if (m is not null) return m;
            current = current.GetInterfaces()
                .FirstOrDefault(i => i.Name == "IRepository`1");
        }
        return null;
    }

    [Fact]
    public async Task StrategyRepository_DeleteAsync_ThrowsNotSupportedException_WithCanonicalMessage()
    {
        // Phase 2 #2: the concrete StrategyRepository stub throws with a
        // message containing "Deactivate" so the StrategyAuditDecorator
        // captures the canonical reason verbatim. The decorator short-circuits
        // BEFORE this stub is reached (matches the 7a.1 UserAuditDecorator +
        // RiskProfileAuditDecorator pattern), but the inner must also throw
        // — defense-in-depth.
        //
        // DeleteAsync is a stub that throws immediately; the DbContext is
        // never touched. Passing a null TradingDbContext is safe and keeps
        // the contract test focused on the throw semantics (the SQLite-in-memory
        // wiring lives in StrategyRepositoryIntegrationTests).
        var strategyRepo = new StrategyRepository(db: null!);

        var strategy = Strategy.Create(
            userId: Guid.NewGuid(),
            name: "Test Strategy",
            description: null,
            symbol: "EUR/USD",
            timeframe: null,
            rules: null,
            clock: new FixedClockForTest()).Value;

        var act = async () => await strategyRepo.DeleteAsync(strategy, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Deactivat*");
    }

    private sealed class FixedClockForTest : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
    }
}