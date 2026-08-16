using JadeCapital.Trading.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Persistence;

// ============================================================================
//  AlertRepository — slice 3b (Trader Strategies + Alerts + Planner).
//
//  EF Core implementation of IAlertRepository. Cross-user scope: every
//  List/Get filters WHERE user_id = @userId. The dedup boundary is
//  ux_alerts_user_rule_day (UNIQUE on (user_id, rule_id, UTC-date));
//  AddAsync catches the unique-violation exception and returns false
//  instead of bubbling it up to the BackgroundService (which would
//  crash the loop).
// ============================================================================

public sealed class AlertRepository : IAlertRepository
{
    private readonly TradingDbContext _db;

    public AlertRepository(TradingDbContext db) { _db = db; }

    public async Task<IReadOnlyList<Domain.Alerts.Alert>> ListByUserAsync(
        Guid userId, bool activeOnly, DateTimeOffset now, CancellationToken ct)
    {
        IQueryable<Domain.Alerts.Alert> query = _db.Alerts
            .AsNoTracking()
            .Where(a => a.UserId == userId);

        if (activeOnly)
        {
            query = query.Where(a => a.AcknowledgedAt == null
                && (a.ExpiresAt == null || a.ExpiresAt > now));
        }

        return await query
            .OrderByDescending(a => a.Severity)
            .ThenByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<Domain.Alerts.Alert?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct)
    {
        return await _db.Alerts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
    }

    public async Task<bool> AddAsync(Domain.Alerts.Alert alert, CancellationToken ct)
    {
        try
        {
            await _db.Alerts.AddAsync(alert, ct);
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // The partial UNIQUE INDEX ux_alerts_user_rule_day rejected
            // the insert (a row for (user, rule, UTC-date) already exists).
            // Detach the staged entity so the next AddAsync can re-stage.
            _db.Entry(alert).State = EntityState.Detached;
            return false;
        }
    }

    public async Task UpdateAsync(Domain.Alerts.Alert alert, CancellationToken ct)
    {
        var entry = _db.Entry(alert);
        if (entry.State == EntityState.Detached)
        {
            _db.Alerts.Update(alert);
        }
        await Task.CompletedTask;
    }

    /// <summary>True when the SQL error code corresponds to a UNIQUE violation (Postgres 23505).</summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner is null) return false;
        // Npgsql.PostgresException exposes SqlState directly.
        var sqlState = (inner as Npgsql.PostgresException)?.SqlState;
        return sqlState == "23505";
    }
}