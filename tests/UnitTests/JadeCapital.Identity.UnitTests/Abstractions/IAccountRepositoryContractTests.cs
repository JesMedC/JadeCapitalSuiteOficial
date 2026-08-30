using System.Reflection;
using FluentAssertions;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Accounts;

namespace JadeCapital.Identity.UnitTests.Abstractions;

/// <summary>
/// Wave 8 slice 8a.1 — <see cref="IAccountRepository"/> BREAKING rename surgery
/// + <c>IRepository&lt;Account&gt;</c> extension.
/// <para>
/// Slice 8a.1 makes two atomic surface changes to <see cref="IAccountRepository"/>:
/// </para>
/// <list type="number">
///   <item>RENAME <c>IAccountRepository.RemoveAsync(Account, ct)</c> →
///         <c>IAccountRepository.DeleteAsync(Account, ct)</c>. The new name
///         matches the canonical
///         <c>IRepository&lt;T&gt;.DeleteAsync(T, ct)</c> from
///         <c>Shared.Kernel/Repository/IRepository.cs</c>. The rename is
///         BREAKING: NO <c>[Obsolete]</c>, NO overload, NO deprecation
///         period. The single handler call site
///         (<c>DeleteAccountHandler.cs</c>) is updated atomically in the
///         same slice.</item>
///   <item>EXTEND <see cref="IAccountRepository"/> to
///         <c>IRepository&lt;Account&gt;</c>. The base interface contributes
///         <c>GetByIdAsync(Guid, ct)</c> + <c>AddAsync</c> +
///         <c>UpdateAsync</c> + <c>DeleteAsync(T, ct)</c>. The bespoke
///         <c>FindByIdAsync(Guid, ct)</c> + <c>ListByUserIdAsync(Guid, ct)</c>
///         methods stay on the interface for backwards compatibility with
///         the existing handler call sites (mirrors the Wave 7 7b.1 ITradeRepository
///         precedent — bespoke reads stay, generic CRUD is gained).</item>
/// </list>
/// <para>
/// The concrete <see cref="JadeCapital.Trading.Infrastructure.Persistence.AccountRepository"/>
/// gains the missing <c>UpdateAsync</c> impl (the rename mirrors the same
/// body as the old <c>RemoveAsync</c> — just renamed). The decorator
/// (<c>AccountAuditDecorator</c>) emits <see cref="JadeCapital.Shared.Kernel.Audit.AuditAction.Deleted"/>
/// on the new signature.
/// </para>
/// <para>
/// Three RED scenarios pinned here:
/// <list type="number">
///   <item><c>IAccountRepository.DeleteAsync(Account, CancellationToken)</c>
///         method exists on the interface (reflection check).</item>
///   <item><c>IAccountRepository.RemoveAsync</c> no longer exists on the
///         interface (reflection negative check) — the rename removed the
///         old name.</item>
///   <item><c>IAccountRepository.UpdateAsync(Account, CancellationToken)</c>
///         exists (the new method contributed by the
///         <c>IRepository&lt;Account&gt;</c> extension). The bespoke
///         <c>FindByIdAsync</c> + <c>ListByUserIdAsync</c> signatures are
///         unchanged (regression guard — slice 8a.1 must not touch the
///         read-only methods).</item>
/// </list>
/// </para>
/// </summary>
public class IAccountRepositoryContractTests
{
    [Fact]
    public void IAccountRepository_Exposes_DeleteAsyncMethod()
    {
        // Phase 1 #1: DeleteAsync(Account, CancellationToken) exists on the
        // interface (slice 8a.1 rename target). The DeleteAsync method is
        // inherited from IRepository<Account>; the Type.GetMethod(name, types)
        // overload does NOT walk the interface hierarchy, so we walk
        // GetInterfaces() explicitly.
        var method = new[] { typeof(IAccountRepository) }
            .Concat(typeof(IAccountRepository).GetInterfaces())
            .SelectMany(t => t.GetMethods())
            .FirstOrDefault(m =>
                m.Name == "DeleteAsync"
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == typeof(Account)
                && m.GetParameters()[1].ParameterType == typeof(CancellationToken));

        method.Should().NotBeNull(
            "IAccountRepository must expose DeleteAsync(Account, CancellationToken) " +
            "after the slice 8a.1 RemoveAsync → DeleteAsync rename (inherited " +
            "from IRepository<Account>, walked via GetInterfaces()).");
        method!.ReturnType.Should().Be<Task>(
            "DeleteAsync is a Task-returning async method (no result payload).");
    }

    [Fact]
    public void IAccountRepository_DoesNotExpose_RemoveAsyncMethod()
    {
        // Phase 1 #2: RemoveAsync(Account, CancellationToken) is GONE.
        // The rename is BREAKING — no [Obsolete], no overload, no deprecation
        // period. The old method name must NOT appear on the interface.
        // Walk GetInterfaces() so we catch RemoveAsync even if it were
        // declared on a base interface.
        var method = new[] { typeof(IAccountRepository) }
            .Concat(typeof(IAccountRepository).GetInterfaces())
            .SelectMany(t => t.GetMethods())
            .FirstOrDefault(m =>
                m.Name == "RemoveAsync"
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == typeof(Account)
                && m.GetParameters()[1].ParameterType == typeof(CancellationToken));

        method.Should().BeNull(
            "IAccountRepository.RemoveAsync(Account, ct) was renamed to " +
            "DeleteAsync(Account, ct) in slice 8a.1 — the old name MUST NOT " +
            "remain on the interface (no [Obsolete], no overload).");
    }

    [Fact]
    public void IAccountRepository_Exposes_UpdateAsyncMethod_AndBespokeReadsUnchanged()
    {
        // Phase 1 #3: the IRepository<Account> extension contributes
        // UpdateAsync(Account, CancellationToken). The bespoke FindByIdAsync
        // + ListByUserIdAsync signatures are unchanged (regression guard).
        // UpdateAsync is inherited from IRepository<Account>; the
        // Type.GetMethod(name, types) overload does NOT walk the interface
        // hierarchy, so we walk GetInterfaces() explicitly.
        var inherited = new[] { typeof(IAccountRepository) }
            .Concat(typeof(IAccountRepository).GetInterfaces())
            .SelectMany(t => t.GetMethods())
            .FirstOrDefault(m =>
                m.Name == "UpdateAsync"
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == typeof(Account)
                && m.GetParameters()[1].ParameterType == typeof(CancellationToken));

        inherited.Should().NotBeNull(
            "UpdateAsync(Account, ct) is the canonical Update surface " +
            "contributed by the IRepository<Account> extension in slice 8a.1 " +
            "(walks the interface hierarchy via GetInterfaces()).");
        inherited!.ReturnType.Should().Be<Task>(
            "UpdateAsync is a Task-returning async method (no result payload).");

        // Bespoke reads — regression guard.
        var findById = typeof(IAccountRepository).GetMethod(
            "FindByIdAsync",
            new[] { typeof(Guid), typeof(CancellationToken) });
        findById.Should().NotBeNull(
            "FindByIdAsync(Guid, ct) is the bespoke read-only lookup and " +
            "MUST remain unchanged by slice 8a.1.");
        findById!.ReturnType.Should().Be<Task<Account?>>(
            "FindByIdAsync returns Task<Account?> — null when the row is missing.");

        var listByUser = typeof(IAccountRepository).GetMethod(
            "ListByUserIdAsync",
            new[] { typeof(Guid), typeof(CancellationToken) });
        listByUser.Should().NotBeNull(
            "ListByUserIdAsync(Guid, ct) is the bespoke list-by-user lookup " +
            "MUST remain unchanged by slice 8a.1.");
        listByUser!.ReturnType.Should().Be<Task<IReadOnlyList<Account>>>(
            "ListByUserIdAsync returns Task<IReadOnlyList<Account>> — the " +
            "user's accounts ordered by CreatedAt descending.");
    }
}