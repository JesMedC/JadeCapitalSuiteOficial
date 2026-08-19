using JadeCapital.Admin.Application.Features.Audit;

namespace JadeCapital.Admin.Application.Abstractions;

/// <summary>
/// Read-only query surface for the <c>audit.events</c> table (Wave 9,
/// slice 9b.1 — admin query API, sub-scope B).
///
/// <para>
/// Lives in <c>Admin.Application</c> (admin-side abstraction) so the Admin
/// module is decoupled from <c>Identity.Infrastructure</c>'s internal
/// <see cref="JadeCapital.Identity.Infrastructure.Persistence.AuditDbContext"/>
/// type. The EF implementation in <c>Admin.Infrastructure</c> resolves
/// <see cref="JadeCapital.Identity.Infrastructure.Persistence.AuditDbContext"/>
/// via the new cross-module DI edge
/// (<c>Admin.Infrastructure → Identity.Infrastructure</c>).
/// </para>
///
/// <para>
/// The contract is the read-side of the audit log: filters + cursor
/// pagination. Writes stay exclusively on the
/// <see cref="IAuditLogger"/> path (Wave 6 6d.2).
/// </para>
/// </summary>
public interface IAuditEventQueryStore
{
    /// <summary>
    /// Lists audit events matching the supplied filters, paged via an
    /// opaque base64 cursor (keyset on <c>(occurred_at DESC, id DESC)</c>).
    /// </summary>
    /// <param name="query">
    /// Filter parameter object: <c>EntityType</c>, <c>Action</c>,
    /// <c>UserId</c>, <c>TenantId</c>, <c>From</c> / <c>To</c>
    /// (half-open), <c>Cursor</c> (opaque base64 of last item's
    /// <c>(occurred_at_ticks, id_guid)</c>), <c>Limit</c> (clamped
    /// upstream to <c>[1, 200]</c>).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="PagedAuditEventsDto"/> carrying the paged items, the
    /// next cursor (or <c>null</c> when <c>has_more == false</c>), and
    /// the <c>has_more</c> flag.
    /// </returns>
    Task<PagedAuditEventsDto> ListAsync(ListAuditEventsQuery query, CancellationToken ct);
}