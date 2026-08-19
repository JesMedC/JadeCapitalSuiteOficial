using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Admin.Application.Features.Audit;

/// <summary>
/// Query parameter object for <see cref="IAuditEventQueryStore.ListAsync"/>
/// (Wave 9, slice 9b.1 — admin query API, sub-scope B).
///
/// <para>
/// All filters are optional and AND-combined. The cursor is the opaque
/// base64 form of the previous page's last item's
/// <c>(occurred_at_ticks, id_guid)</c>. The handler clamps
/// <see cref="Limit"/> to <c>[1, 200]</c> (default <c>50</c>) before
/// dispatching to the store.
/// </para>
/// </summary>
/// <param name="EntityType">
/// Exact-match filter on <c>audit.events.entity_type</c> (e.g.
/// <c>"TradeAttachment"</c>). <c>null</c> means no filter.
/// </param>
/// <param name="Action">
/// Action enum (byte value). <c>null</c> means no filter. Maps to
/// <c>audit.events.action</c> via the EF <c>HasConversion&lt;byte&gt;()</c>
/// converter.
/// </param>
/// <param name="UserId">
/// Actor <c>Guid</c>. <c>null</c> means no filter. Maps to
/// <c>ix_audit_events_user</c>.
/// </param>
/// <param name="TenantId">
/// Tenant <c>Guid</c>. <c>null</c> means no filter. Maps to
/// <c>ix_audit_events_tenant_time</c> when paired with a date range.
/// </param>
/// <param name="From">
/// Inclusive lower bound on <c>occurred_at</c>. <c>null</c> means no
/// lower bound. Combined with <see cref="To"/> to form a half-open
/// interval <c>[From, To)</c>.
/// </param>
/// <param name="To">
/// Exclusive upper bound on <c>occurred_at</c>. <c>null</c> means no
/// upper bound.
/// </param>
/// <param name="Cursor">
/// Opaque base64 cursor from a prior page's <c>next_cursor</c>.
/// <c>null</c> starts at the newest row.
/// </param>
/// <param name="Limit">
/// Page size. The handler clamps to <c>[1, 200]</c>; the store applies
/// <c>LIMIT</c> + <c>1</c> to detect <c>has_more</c>.
/// </param>
public sealed record ListAuditEventsQuery(
    string? EntityType,
    AuditAction? Action,
    Guid? UserId,
    Guid? TenantId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int Limit) : IRequest<Result<PagedAuditEventsDto>>
{
    /// <summary>Default page size when the client omits <c>limit</c>.</summary>
    public const int DefaultLimit = 50;

    /// <summary>Hard upper bound on the page size (matches the spec's clamping rule).</summary>
    public const int MaxLimit = 200;

    /// <summary>Hard lower bound on the page size (per spec).</summary>
    public const int MinLimit = 1;
}