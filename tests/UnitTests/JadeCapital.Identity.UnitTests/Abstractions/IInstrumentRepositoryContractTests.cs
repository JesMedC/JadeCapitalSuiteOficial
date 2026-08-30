using System.Reflection;
using FluentAssertions;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Instruments;

namespace JadeCapital.Identity.UnitTests.Abstractions;

/// <summary>
/// Wave 8 slice 8a.1 — <see cref="IInstrumentRepository"/> BREAKING rename surgery
/// + <c>IRepository&lt;Instrument&gt;</c> extension.
/// <para>
/// Slice 8a.1 makes two atomic surface changes to <see cref="IInstrumentRepository"/>:
/// </para>
/// <list type="number">
///   <item>RENAME <c>IInstrumentRepository.RemoveAsync(Instrument, ct)</c> →
///         <c>IInstrumentRepository.DeleteAsync(Instrument, ct)</c>. The new name
///         matches the canonical
///         <c>IRepository&lt;T&gt;.DeleteAsync(T, ct)</c> from
///         <c>Shared.Kernel/Repository/IRepository.cs</c>. The rename is
///         BREAKING: NO <c>[Obsolete]</c>, NO overload, NO deprecation
///         period. The single handler call site
///         (<c>DeleteInstrumentHandler.cs</c>) is updated atomically in the
///         same slice.</item>
///   <item>EXTEND <see cref="IInstrumentRepository"/> to
///         <c>IRepository&lt;Instrument&gt;</c>. The base interface contributes
///         <c>GetByIdAsync(Guid, ct)</c> + <c>AddAsync</c> +
///         <c>UpdateAsync</c> + <c>DeleteAsync(T, ct)</c>. The bespoke
///         <c>FindByIdAsync(Guid, ct)</c> + <c>FindBySymbolAsync(string, ct)</c>
///         + <c>ListActiveAsync(ct)</c> + <c>ListAllAsync(ct)</c> methods stay
///         on the interface for backwards compatibility with the existing
///         handler call sites (mirrors the Wave 7 7b.1 ITradeRepository
///         precedent — bespoke reads stay, generic CRUD is gained).</item>
/// </list>
/// <para>
/// The concrete <see cref="JadeCapital.Trading.Infrastructure.Persistence.InstrumentRepository"/>
/// gains the missing <c>UpdateAsync</c> impl (the rename mirrors the same
/// body as the old <c>RemoveAsync</c> — just renamed). The decorator
/// (<c>InstrumentAuditDecorator</c>) emits <see cref="JadeCapital.Shared.Kernel.Audit.AuditAction.Deleted"/>
/// on the new signature.
/// </para>
/// <para>
/// Three RED scenarios pinned here:
/// <list type="number">
///   <item><c>IInstrumentRepository.DeleteAsync(Instrument, CancellationToken)</c>
///         method exists on the interface (reflection check).</item>
///   <item><c>IInstrumentRepository.RemoveAsync</c> no longer exists on the
///         interface (reflection negative check) — the rename removed the
///         old name.</item>
///   <item><c>IInstrumentRepository.UpdateAsync(Instrument, CancellationToken)</c>
///         exists (the new method contributed by the
///         <c>IRepository&lt;Instrument&gt;</c> extension). The bespoke
///         read-only signatures are unchanged (regression guard — slice
///         8a.1 must not touch the read-only methods).</item>
/// </list>
/// </para>
/// </summary>
public class IInstrumentRepositoryContractTests
{
    [Fact]
    public void IInstrumentRepository_Exposes_DeleteAsyncMethod()
    {
        // Phase 2 #1: DeleteAsync(Instrument, CancellationToken) exists on
        // the interface (slice 8a.1 rename target). Walk the interface
        // hierarchy via GetInterfaces() because the DeleteAsync method is
        // inherited from IRepository<Instrument>; the Type.GetMethod(name,
        // types) overload does NOT flatten inherited interface members.
        var method = new[] { typeof(IInstrumentRepository) }
            .Concat(typeof(IInstrumentRepository).GetInterfaces())
            .SelectMany(t => t.GetMethods())
            .FirstOrDefault(m =>
                m.Name == "DeleteAsync"
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == typeof(Instrument)
                && m.GetParameters()[1].ParameterType == typeof(CancellationToken));

        method.Should().NotBeNull(
            "IInstrumentRepository must expose DeleteAsync(Instrument, CancellationToken) " +
            "after the slice 8a.1 RemoveAsync → DeleteAsync rename (inherited " +
            "from IRepository<Instrument>, walked via GetInterfaces()).");
        method!.ReturnType.Should().Be<Task>(
            "DeleteAsync is a Task-returning async method (no result payload).");
    }

    [Fact]
    public void IInstrumentRepository_DoesNotExpose_RemoveAsyncMethod()
    {
        // Phase 2 #2: RemoveAsync(Instrument, CancellationToken) is GONE.
        // The rename is BREAKING — no [Obsolete], no overload, no deprecation
        // period. The old method name must NOT appear on the interface.
        // Walk GetInterfaces() so we catch RemoveAsync even if it were
        // declared on a base interface.
        var method = new[] { typeof(IInstrumentRepository) }
            .Concat(typeof(IInstrumentRepository).GetInterfaces())
            .SelectMany(t => t.GetMethods())
            .FirstOrDefault(m =>
                m.Name == "RemoveAsync"
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == typeof(Instrument)
                && m.GetParameters()[1].ParameterType == typeof(CancellationToken));

        method.Should().BeNull(
            "IInstrumentRepository.RemoveAsync(Instrument, ct) was renamed to " +
            "DeleteAsync(Instrument, ct) in slice 8a.1 — the old name MUST NOT " +
            "remain on the interface (no [Obsolete], no overload).");
    }

    [Fact]
    public void IInstrumentRepository_Exposes_UpdateAsyncMethod_AndBespokeReadsUnchanged()
    {
        // Phase 2 #3: the IRepository<Instrument> extension contributes
        // UpdateAsync(Instrument, CancellationToken). The bespoke
        // FindByIdAsync + FindBySymbolAsync + ListActiveAsync + ListAllAsync
        // signatures are unchanged (regression guard).
        var inherited = new[] { typeof(IInstrumentRepository) }
            .Concat(typeof(IInstrumentRepository).GetInterfaces())
            .SelectMany(t => t.GetMethods())
            .FirstOrDefault(m =>
                m.Name == "UpdateAsync"
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == typeof(Instrument)
                && m.GetParameters()[1].ParameterType == typeof(CancellationToken));

        inherited.Should().NotBeNull(
            "UpdateAsync(Instrument, ct) is the canonical Update surface " +
            "contributed by the IRepository<Instrument> extension in slice 8a.1 " +
            "(walks the interface hierarchy via GetInterfaces()).");
        inherited!.ReturnType.Should().Be<Task>(
            "UpdateAsync is a Task-returning async method (no result payload).");

        // Bespoke reads — regression guard.
        var findById = typeof(IInstrumentRepository).GetMethod(
            "FindByIdAsync",
            new[] { typeof(Guid), typeof(CancellationToken) });
        findById.Should().NotBeNull(
            "FindByIdAsync(Guid, ct) is the bespoke read-only lookup and " +
            "MUST remain unchanged by slice 8a.1.");
        findById!.ReturnType.Should().Be<Task<Instrument?>>(
            "FindByIdAsync returns Task<Instrument?> — null when the row is missing.");

        var findBySymbol = typeof(IInstrumentRepository).GetMethod(
            "FindBySymbolAsync",
            new[] { typeof(string), typeof(CancellationToken) });
        findBySymbol.Should().NotBeNull(
            "FindBySymbolAsync(string, ct) is the bespoke symbol-uniqueness " +
            "lookup and MUST remain unchanged by slice 8a.1.");
        findBySymbol!.ReturnType.Should().Be<Task<Instrument?>>(
            "FindBySymbolAsync returns Task<Instrument?> — null when no row matches.");

        var listActive = typeof(IInstrumentRepository).GetMethod(
            "ListActiveAsync",
            new[] { typeof(CancellationToken) });
        listActive.Should().NotBeNull(
            "ListActiveAsync(ct) is the bespoke active-instruments lookup and " +
            "MUST remain unchanged by slice 8a.1.");
        listActive!.ReturnType.Should().Be<Task<IReadOnlyList<Instrument>>>(
            "ListActiveAsync returns Task<IReadOnlyList<Instrument>> — the " +
            "active catalog ordered by Symbol ascending.");

        var listAll = typeof(IInstrumentRepository).GetMethod(
            "ListAllAsync",
            new[] { typeof(CancellationToken) });
        listAll.Should().NotBeNull(
            "ListAllAsync(ct) is the bespoke admin/debug catalog lookup and " +
            "MUST remain unchanged by slice 8a.1.");
        listAll!.ReturnType.Should().Be<Task<IReadOnlyList<Instrument>>>(
            "ListAllAsync returns Task<IReadOnlyList<Instrument>> — the full " +
            "catalog (active + inactive) ordered by Symbol ascending.");
    }
}