using JadeCapital.Shared.Kernel.Audit;

namespace JadeCapital.Admin.Application.Features.Audit;

/// <summary>
/// Admin-visible projection of an <c>audit.events</c> row (Wave 9,
/// slice 9b.1 — admin query API, sub-scope B).
///
/// <para>
/// Exposes ALL fields by design — admins see <c>changes</c> JSONB,
/// <c>user_id</c> + <c>tenant_id</c> internal Guids, and the full
/// <c>entity_id</c>. The compliance contract is enforced at the
/// endpoint boundary (<c>RequireAuthorization("AdminOnly")</c>) BEFORE
/// the DTO mapping — no trader or anonymous access ever reaches this
/// shape. <c>user_id</c> + <c>tenant_id</c> are internal Guids (not
/// PII like email/name); the <c>changes</c> JSONB may contain
/// user-owned entity data (e.g. trade price, journal text).
/// </para>
/// </summary>
/// <param name="Id">Audit row id (Guid).</param>
/// <param name="EntityType">Entity type short string (e.g. <c>"TradeAttachment"</c>).</param>
/// <param name="EntityId">Guid of the audited entity.</param>
/// <param name="Action">Action enum name (serialized as string by System.Text.Json).</param>
/// <param name="TenantId">Tenant scope (nullable for cross-tenant admin ops).</param>
/// <param name="UserId">Actor (nullable for system actors).</param>
/// <param name="Changes">JSONB diff payload (preserved verbatim; no redaction).</param>
/// <param name="OccurredAt">UTC timestamp pinned from <see cref="IClock.UtcNow"/> at construction.</param>
public sealed record AuditEventDto(
    Guid Id,
    string EntityType,
    Guid EntityId,
    AuditAction Action,
    Guid? TenantId,
    Guid? UserId,
    string? Changes,
    DateTimeOffset OccurredAt);

/// <summary>
/// Paginated response shape for the admin query API (Wave 9, slice 9b.1).
///
/// <para>
/// Mirrors the spec's wire shape: <c>{ "items": [...], "next_cursor":
/// "...", "has_more": &lt;bool&gt; }</c>. The handler emits this DTO
/// directly from <c>JsonResult</c>; no further mapping happens.
/// </para>
/// </summary>
/// <param name="Items">Up to <c>limit</c> audit event DTOs, newest first.</param>
/// <param name="NextCursor">
/// Opaque base64 cursor for the next page (last item's
/// <c>(occurred_at_ticks, id_guid)</c>). <c>null</c> when
/// <see cref="HasMore"/> is <c>false</c>.
/// </param>
/// <param name="HasMore">
/// <c>true</c> when the store returned <c>limit + 1</c> rows (more
/// pages available); <c>false</c> otherwise.
/// </param>
public sealed record PagedAuditEventsDto(
    IReadOnlyList<AuditEventDto> Items,
    string? NextCursor,
    bool HasMore);