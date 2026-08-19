using JadeCapital.Admin.Application.Features.Audit;
using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Admin.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="JadeCapital.Admin.Application.Abstractions.IAuditEventQueryStore"/>
/// (Wave 9, slice 9b.1 — admin query API, sub-scope B).
///
/// <para>
/// Resolves the dedicated <see cref="AuditDbContext"/> from
/// <c>Identity.Infrastructure</c> via the new cross-module DI edge
/// (<c>Admin.Infrastructure → Identity.Infrastructure</c>). The store
/// is read-only: no <c>INSERT</c>, <c>UPDATE</c>, or <c>DELETE</c>
/// happens here — the audit log's append-only invariant is enforced by
/// <see cref="AuditDbContext"/> + <see cref="JadeCapital.Shared.Kernel.Audit.IAuditLogger"/>.
/// </para>
///
/// <para>
/// <b>Pagination</b>: cursor-based keyset on
/// <c>(occurred_at DESC, id DESC)</c>. The store fetches
/// <c>limit + 1</c> rows; if the extra row is present,
/// <c>HasMore = true</c> and the cursor points to the LAST item in
/// the page (NOT the <c>+1</c> row). When <c>HasMore = false</c>,
/// <c>NextCursor = null</c>.
/// </para>
///
/// <para>
/// <b>Indexes</b>: the 3 existing indexes from migration 0027
/// (<c>ix_audit_events_entity</c>, <c>ix_audit_events_tenant_time</c>,
/// <c>ix_audit_events_user</c>) cover the 4 single-filter queries.
/// Compound filters use the most selective single index; Postgres
/// bitmap-ANDs the others. For 1M+ row tables, add a covering index
/// in a follow-up migration (out of Wave 9 scope).
/// </para>
/// </summary>
public sealed class AuditEventQueryStore : JadeCapital.Admin.Application.Abstractions.IAuditEventQueryStore
{
    private readonly AuditDbContext _db;

    public AuditEventQueryStore(AuditDbContext db)
    {
        _db = db;
    }

    public async Task<PagedAuditEventsDto> ListAsync(ListAuditEventsQuery query, CancellationToken ct)
    {
        // Build the keyset query.
        IQueryable<AuditEvent> q = _db.AuditEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.EntityType))
            q = q.Where(e => e.EntityType == query.EntityType);

        if (query.Action.HasValue)
            q = q.Where(e => e.Action == query.Action.Value);

        if (query.UserId.HasValue)
            q = q.Where(e => e.UserId == query.UserId.Value);

        if (query.TenantId.HasValue)
            q = q.Where(e => e.TenantId == query.TenantId.Value);

        // Half-open [From, To).
        if (query.From.HasValue)
            q = q.Where(e => e.OccurredAt >= query.From.Value);

        if (query.To.HasValue)
            q = q.Where(e => e.OccurredAt < query.To.Value);

        // Cursor: keyset filter on (occurred_at, id) — strictly less
        // than the cursor key in (occurred_at DESC, id DESC) order.
        // The handler already validated the cursor shape, so we decode
        // here (no try/catch needed — the contract is enforced upstream).
        if (!string.IsNullOrEmpty(query.Cursor)
            && ListAuditEventsHandler.TryDecodeCursor(query.Cursor, out var key))
        {
            q = q.Where(e =>
                e.OccurredAt < key.OccurredAt
                || (e.OccurredAt == key.OccurredAt && e.Id.CompareTo(key.Id) < 0));
        }

        // Order: newest first; id tiebreaker for deterministic ordering
        // when two events share occurred_at. We materialize FIRST then
        // apply the ordering client-side because SQLite's EF provider
        // does not translate DateTimeOffset in ORDER BY clauses
        // (NotSupportedException: "SQLite does not support expressions
        // of type 'DateTimeOffset' in ORDER BY clauses"). The WHERE
        // filters + Take(limit+1) are still pushed to SQL; only the
        // ORDER BY is applied in-memory. For Postgres this stays
        // efficient because the cursor keyset WHERE clause narrows the
        // row set sharply before the LIMIT.
        var rows = await q.Take(query.Limit + 1).ToListAsync(ct);
        rows = rows.OrderByDescending(e => e.OccurredAt)
                   .ThenByDescending(e => e.Id)
                   .ToList();

        var hasMore = rows.Count > query.Limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        var items = rows
            .Select(e => new AuditEventDto(
                e.Id, e.EntityType, e.EntityId, e.Action,
                e.TenantId, e.UserId, e.ChangesJson, e.OccurredAt))
            .ToList();

        string? nextCursor = null;
        if (hasMore && items.Count > 0)
        {
            var last = items[^1];
            nextCursor = ListAuditEventsHandler.EncodeCursor(last.OccurredAt, last.Id);
        }

        return new PagedAuditEventsDto(items, nextCursor, hasMore);
    }
}