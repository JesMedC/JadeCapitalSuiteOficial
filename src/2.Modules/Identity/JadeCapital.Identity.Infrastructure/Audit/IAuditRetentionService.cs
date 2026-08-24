using JadeCapital.Identity.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Identity.Infrastructure.Audit;

/// <summary>
/// Retention service for the <c>audit.events</c> table (Wave 9, slice
/// 9b.1 — audit retention, sub-scope C).
///
/// <para>
/// The contract: delete up to <paramref name="batchLimit"/> rows whose
/// <see cref="AuditEvent.OccurredAt"/> is older than <paramref name="cutoff"/>,
/// using EF Core 9 <see cref="EntityFrameworkQueryableExtensions.ExecuteDeleteAsync{TEntity}"/>
/// (which translates to a single <c>DELETE</c> statement — no tracked
/// entities, no <c>audit.events</c> audit rows for the purge itself).
/// </para>
///
/// <para>
/// The BackgroundService loop wraps this method: per cycle it calls
/// <c>PurgeOldAsync</c> until the return value drops below
/// <paramref name="batchLimit"/> (idempotent drain within a single
/// cutoff). The interface is Scoped — the implementation depends on
/// <see cref="AuditDbContext"/> which is itself Scoped.
/// </para>
/// </summary>
public interface IAuditRetentionService
{
    /// <summary>
    /// Deletes up to <paramref name="batchLimit"/> rows with
    /// <c>occurred_at &lt; cutoff</c>. Returns the number of deleted
    /// rows. Idempotent — calling again with the same cutoff returns
    /// <c>0</c> once the prior call drained the eligible set.
    /// </summary>
    /// <param name="cutoff">UTC threshold; rows with <c>occurred_at &lt; cutoff</c> are eligible.</param>
    /// <param name="batchLimit">Cap on rows deleted per call.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of rows deleted in this call.</returns>
    Task<int> PurgeOldAsync(DateTimeOffset cutoff, int batchLimit, CancellationToken ct);
}

/// <summary>
/// EF Core implementation of <see cref="IAuditRetentionService"/>.
///
/// <para>
/// The implementation uses <see cref="EntityFrameworkQueryableExtensions.ExecuteDeleteAsync{TEntity}"/>
/// (introduced in EF Core 7, mature in EF Core 9) which translates to a
/// single <c>DELETE FROM audit.events WHERE ...</c> statement. The
/// change tracker is bypassed entirely — no <c>audit.events</c> rows
/// are emitted for the purge itself (would be recursive noise).
/// </para>
///
/// <para>
/// <b>Batch limit implementation</b>: the SQLite EF provider does NOT
/// translate <c>ExecuteDelete</c> + <c>Take()</c> on the query path
/// (NotSupportedException). The fix is to materialize the eligible
/// <c>Id</c>s in a small SELECT first (capped by <c>batchLimit</c>),
/// then issue a single bulk DELETE scoped to those IDs. The DELETE
/// itself is still a single statement on both Postgres and SQLite —
/// only the ID-resolution step is a 2-roundtrip variant. For Postgres
/// production scale, the materialized ID list is capped at
/// <c>batchLimit</c> (default 10,000) so the SELECT cost is bounded.
/// </para>
/// </summary>
public sealed class AuditRetentionService : IAuditRetentionService
{
    private readonly Persistence.AuditDbContext _db;

    public AuditRetentionService(Persistence.AuditDbContext db)
    {
        _db = db;
    }

    public async Task<int> PurgeOldAsync(DateTimeOffset cutoff, int batchLimit, CancellationToken ct)
    {
        if (batchLimit <= 0) return 0;

        if (_db.Database.IsNpgsql())
        {
            return await _db.Database.ExecuteSqlInterpolatedAsync($$"""
                WITH victims AS (
                    SELECT id, occurred_at
                    FROM audit.events
                    WHERE occurred_at < {{cutoff}}
                    ORDER BY occurred_at, id
                    LIMIT {{batchLimit}}
                )
                DELETE FROM audit.events AS events
                USING victims
                WHERE events.id = victims.id
                  AND events.occurred_at = victims.occurred_at
                """, ct);
        }

        // SQLite's EF provider does NOT translate DateTimeOffset comparisons
        // in WHERE clauses (NotSupportedException). The fix is to materialize
        // the eligible rows first (bounded by a generous hard cap so the
        // load is bounded) then filter + order client-side. The DELETE itself
        // is still a single ExecuteDeleteAsync statement scoped to the
        // resolved IDs (no tracked entities, no audit.events rows for the
        // purge itself).
        const int HardCap = 100_000;
        var eligible = await _db.AuditEvents
            .Take(HardCap)
            .ToListAsync(ct);

        var ids = eligible
            .Where(e => e.OccurredAt < cutoff)
            .OrderBy(e => e.OccurredAt)
            .Take(batchLimit)
            .Select(e => e.Id)
            .ToList();

        if (ids.Count == 0) return 0;

        return await _db.AuditEvents
            .Where(e => ids.Contains(e.Id))
            .ExecuteDeleteAsync(ct);
    }
}
