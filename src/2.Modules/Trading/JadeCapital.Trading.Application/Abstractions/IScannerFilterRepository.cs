using JadeCapital.Shared.Kernel.Repository;
using JadeCapital.Trading.Domain.Scanner;

namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Persistence contract for the <see cref="ScannerFilter"/> aggregate
/// (Wave 4, slice 4a; extended in Wave 9, slice 9a.2).
///
/// <para>
/// <b>Wave 9 §9a.2 interface surgery</b>: this interface now extends
/// <see cref="IRepository{T}"/> (<c>IRepository&lt;ScannerFilter&gt;</c>),
/// which contributes a <c>DeleteAsync(ScannerFilter, ct)</c> method.
/// <see cref="ScannerFilter"/> is a <b>soft-delete-by-flag</b> aggregate —
/// its canonical mutation surface is
/// <c>ScannerFilter.Deactivate(IClock)</c> (flips <c>IsActive = false</c>),
/// NOT a hard delete. The <c>DeleteAsync</c> method is therefore wrapped
/// by the <c>ScannerFilterAuditDecorator</c> with a <b>defensive stub</b>
/// that emits an <see cref="JadeCapital.Shared.Kernel.Audit.AuditAction.Failed"/>
/// audit row BEFORE re-throwing
/// <see cref="NotSupportedException"/> — mirroring the Wave 7 7a.1
/// <c>UserAuditDecorator</c> + Wave 7 7b.1 <c>StrategyAuditDecorator</c>
/// precedent for non-deletable aggregates.
/// </para>
///
/// <para>
/// <b>Why extend <see cref="IRepository{T}"/> instead of keeping the
/// bespoke shape</b>: the canonical
/// <c>IRepository&lt;T&gt;.DeleteAsync(T, ct)</c> signature is the natural
/// fit for the defensive stub. The bespoke read methods
/// (<see cref="GetByUserAndNameAsync"/> + <see cref="ListByUserAsync"/>)
/// stay on the interface — they're user-scoped lookups that don't match
/// the canonical <c>IRepository&lt;T&gt;.GetByIdAsync(Guid, ct)</c> shape.
/// The decorator stays bespoke (implements
/// <c>IScannerFilterRepository</c> directly rather than delegating to
/// <c>DecoratedRepository&lt;ScannerFilter&gt;</c>) to preserve the
/// user-scoped read signatures + the bespoke
/// <c>UpdateAsync</c> diff JSON contract.
/// </para>
///
/// <para>
/// <b>Canonical CRUD methods not declared on the interface</b>:
/// <c>GetByIdAsync</c>, <c>AddAsync</c>, and <c>UpdateAsync</c> are
/// inherited from <see cref="IRepository{T}"/> automatically. The
/// bespoke-only declarations below are the <c>userId</c>-scoped lookups
/// (<c>GetByUserAndNameAsync</c> + <c>ListByUserAsync</c>) that don't
/// fit the generic shape. Mirrors the Wave 7 7a.1
/// <c>IUserRepository : IRepository&lt;User&gt;</c> precedent where the
/// inherited CRUD methods are implicit + the bespoke ones are explicit.
/// </para>
///
/// <remarks>
/// <b>Migration rationale</b> (Wave 9 §9a.2):
/// <list type="bullet">
///   <item>The <c>DeleteScannerFilterHandler</c> (slice 4a) ALREADY uses
///         <c>filter.Deactivate(_clock)</c> + <c>repo.UpdateAsync(...)</c>
///         instead of a hard delete — verified via <c>git grep</c>
///         before the interface extension (zero call sites for
///         <c>_scannerFilter.DeleteAsync</c>).</item>
///   <item>The added <c>DeleteAsync</c> is purely a defensive contract
///         surface — any future caller that tries to use it will hit the
///         <c>NotSupportedException</c> + an audit row.</item>
///   <item>No <c>Shared.Kernel</c> change required — the
///         <c>IRepository&lt;T&gt;</c> shape is already in place (Wave 6
///         6d.2).</item>
/// </list>
/// </remarks>
/// </summary>
public interface IScannerFilterRepository : IRepository<ScannerFilter>
{
    /// <summary>
    /// User-scoped name lookup — used by the application-layer
    /// <c>CreateOrUpdateScannerFilterHandler</c> to dedupe by
    /// <c>(userId, name)</c> before Create. Returns null if no row matches.
    /// </summary>
    Task<ScannerFilter?> GetByUserAndNameAsync(Guid userId, string name, CancellationToken ct);

    /// <summary>
    /// User-scoped list — used by the application-layer
    /// <c>ListScannerFiltersHandler</c> + <c>RunScannerHandler</c>.
    /// <paramref name="activeOnly"/> filters to <c>IsActive = true</c>
    /// when set; the soft-deleted-by-flag rows are excluded.
    /// </summary>
    Task<IReadOnlyList<ScannerFilter>> ListByUserAsync(Guid userId, bool activeOnly, CancellationToken ct);
}
