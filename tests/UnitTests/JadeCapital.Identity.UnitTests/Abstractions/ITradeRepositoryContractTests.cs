using System.Reflection;
using FluentAssertions;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Trades;
using JadeCapital.Trading.Infrastructure.Persistence;

namespace JadeCapital.Identity.UnitTests.Abstractions;

/// <summary>
/// Wave 7 slice 7b.1 — <see cref="ITradeRepository"/> BREAKING rename surgery.
///
/// <para>
/// Slice 7b.1 renames <c>ITradeRepository.RemoveAsync(Trade, ct)</c> to
/// <c>ITradeRepository.DeleteAsync(Trade, ct)</c> — the canonical
/// <c>IRepository&lt;T&gt;.DeleteAsync(T, ct)</c> surface from
/// <c>Shared.Kernel/Repository/IRepository.cs</c>. The rename is BREAKING:
/// </para>
/// <list type="bullet">
///   <item>NO <c>[Obsolete]</c> attribute — the surface is gone, not deprecated.</item>
///   <item>NO overload — the old method name is removed, not duplicated.</item>
///   <item>NO deprecation period — all 1 known handler call site
///         (<c>DeleteTradeHandler.cs</c>) + 1 test call site
///         (<c>DeleteTradeHandlerTests.cs</c>) are updated atomically in
///         the same PR. Any missed call site surfaces as <c>CS1061</c> on
///         <c>dotnet build</c>.</item>
/// </list>
/// <para>
/// The concrete <see cref="TradeRepository.DeleteAsync(Trade, CancellationToken)"/>
/// impl is the same body as the old <c>RemoveAsync</c> — just renamed.
/// The decorator (<c>TradeAuditDecorator</c>) emits
/// <see cref="Audit.AuditAction.Deleted"/> on the new signature.
/// </para>
///
/// Three RED scenarios pinned here:
/// <list type="number">
///   <item><c>ITradeRepository.DeleteAsync(Trade, CancellationToken)</c> method
///         exists on the interface (reflection check).</item>
///   <item><c>ITradeRepository.RemoveAsync</c> no longer exists on the interface
///         (reflection negative check) — the rename removed the old name.</item>
///   <item><c>ITradeRepository.FindByIdAsync</c> + <c>ITradeRepository.UpdateAsync</c>
///         signatures are unchanged (regression guard — slice 7b.1 must not
///         touch the read-only methods).</item>
/// </list>
/// </summary>
public class ITradeRepositoryContractTests
{
    [Fact]
    public void ITradeRepository_Exposes_DeleteAsyncMethod()
    {
        // Phase 1 #1: DeleteAsync(Trade, CancellationToken) exists on the
        // interface (slice 7b.1 rename target). Reflection walks the
        // interface hierarchy because Type.GetMethod with explicit
        // parameter types does NOT flatten inherited interface members
        // (matches the 7a.1 IUserRepositoryContractTests pattern).
        var method = typeof(ITradeRepository).GetMethod(
            "DeleteAsync",
            new[] { typeof(Trade), typeof(CancellationToken) });

        method.Should().NotBeNull(
            "ITradeRepository must expose DeleteAsync(Trade, CancellationToken) " +
            "after the slice 7b.1 RemoveAsync → DeleteAsync rename.");
        method!.ReturnType.Should().Be<Task>(
            "DeleteAsync is a Task-returning async method (no result payload).");
    }

    [Fact]
    public void ITradeRepository_DoesNotExpose_RemoveAsyncMethod()
    {
        // Phase 1 #2: RemoveAsync(Trade, CancellationToken) is GONE.
        // The rename is BREAKING — no [Obsolete], no overload, no deprecation
        // period. The old method name must NOT appear on the interface.
        var method = typeof(ITradeRepository).GetMethod(
            "RemoveAsync",
            new[] { typeof(Trade), typeof(CancellationToken) });

        method.Should().BeNull(
            "ITradeRepository.RemoveAsync(Trade, ct) was renamed to " +
            "DeleteAsync(Trade, ct) in slice 7b.1 — the old name MUST NOT " +
            "remain on the interface (no [Obsolete], no overload).");
    }

    [Fact]
    public void ITradeRepository_OtherMethods_AreUnchanged()
    {
        // Phase 1 #3: regression guard — slice 7b.1 must not touch the
        // read-only methods or UpdateAsync. Trade has no IRepository<Trade>
        // base (bespoke interface, like User pre-7a.1); the method shapes
        // are explicit declarations.
        var findById = typeof(ITradeRepository).GetMethod(
            "FindByIdAsync",
            new[] { typeof(Guid), typeof(CancellationToken) });
        findById.Should().NotBeNull(
            "FindByIdAsync(Guid, ct) is the canonical read-only lookup " +
            "and MUST remain unchanged by slice 7b.1.");
        findById!.ReturnType.Should().Be<Task<Trade?>>(
            "FindByIdAsync returns Task<Trade?> — null when the row is missing.");

        var update = typeof(ITradeRepository).GetMethod(
            "UpdateAsync",
            new[] { typeof(Trade), typeof(CancellationToken) });
        update.Should().NotBeNull(
            "UpdateAsync(Trade, ct) is the canonical Update surface " +
            "and MUST remain unchanged by slice 7b.1.");
        update!.ReturnType.Should().Be<Task>(
            "UpdateAsync is a Task-returning async method (no result payload).");
    }
}