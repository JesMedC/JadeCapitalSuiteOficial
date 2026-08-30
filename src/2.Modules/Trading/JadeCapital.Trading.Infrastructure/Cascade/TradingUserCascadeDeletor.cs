using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Trading.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Trading.Infrastructure.Cascade;

/// <summary>
/// Trading-side GDPR Art. 17 cascade deletor (Wave 10, slice 10.5).
///
/// <para>
/// Purges every row in <c>trading.*</c> that references the target user.
/// The 13 aggregates covered are: <c>accounts</c>, <c>trades</c>,
/// <c>pre_trade_checklists</c>, <c>trade_reviews</c>,
/// <c>trade_attachments</c>, <c>journal_entries</c>, <c>strategies</c>,
/// <c>alerts</c>, <c>planner_sessions</c>, <c>scanner_filters</c>,
/// <c>coaching_prompts</c>, <c>ai_risk_advices</c>,
/// <c>import_jobs</c>.
/// </para>
///
/// <para>
/// <b>Why soft-delete is a no-op</b>: most trading aggregates have no
/// <c>is_deleted</c> column (the design relies on
/// <c>account_status</c>, <c>alert_status</c>, etc. — domain-driven
/// termination surfaces, NOT a generic soft-delete flag). The
/// user-record's <c>is_deleted</c> flag is the authoritative cascade
/// marker: query handlers check the user's status before returning any
/// aggregate. Once the user is anonymized + marked deleted, no API
/// endpoint returns their trades or journals. <see cref="CascadeSoftDeleteAsync"/>
/// therefore returns 0 (no work to do at the aggregate level); the
/// soft-delete sweep is "complete" at the user-record level.
/// </para>
///
/// <para>
/// <b>Hard-delete path</b>: physically DELETEs every row the user owns.
/// The order of deletes matters — child rows BEFORE parent rows
/// because the FKs are configured as RESTRICT (not CASCADE) by
/// default for trading tables (per Wave 7 slice 7b.1 + 8a.1 design
/// rationale).
/// </para>
/// </summary>
public sealed class TradingUserCascadeDeletor : IUserCascadeDeletor
{
    private readonly TradingDbContext _db;

    public TradingUserCascadeDeletor(TradingDbContext db)
    {
        _db = db;
    }

    public Task<int> CascadeSoftDeleteAsync(Guid userId, CancellationToken ct)
    {
        // No-op: the user-record's is_deleted + status flag is the
        // authoritative cascade marker. Query handlers gate on the user
        // status before returning trades/journals/etc. See the type doc
        // for the full rationale.
        _ = userId;
        _ = ct;
        return Task.FromResult(0);
    }

    public async Task<int> CascadeHardDeleteAsync(Guid userId, CancellationToken ct)
    {
        var touched = 0;

        // Order: child rows first, then parent rows. All WHERE user_id = @userId.
        // The ExecuteDeleteAsync translates to a single DELETE ... WHERE per call.
        touched += await _db.PreTradeChecklists
            .Where(c => c.UserId == userId)
            .ExecuteDeleteAsync(ct);

        touched += await _db.TradeAttachments
            .Where(a => a.UserId == userId)
            .ExecuteDeleteAsync(ct);

        touched += await _db.TradeReviews
            .Where(r => r.UserId == userId)
            .ExecuteDeleteAsync(ct);

        touched += await _db.JournalEntries
            .Where(j => j.UserId == userId)
            .ExecuteDeleteAsync(ct);

        touched += await _db.Alerts
            .Where(a => a.UserId == userId)
            .ExecuteDeleteAsync(ct);

        touched += await _db.PlannerSessions
            .Where(p => p.UserId == userId)
            .ExecuteDeleteAsync(ct);

        touched += await _db.ScannerFilters
            .Where(s => s.UserId == userId)
            .ExecuteDeleteAsync(ct);

        touched += await _db.Strategies
            .Where(s => s.UserId == userId)
            .ExecuteDeleteAsync(ct);

        touched += await _db.Trades
            .Where(t => t.UserId == userId)
            .ExecuteDeleteAsync(ct);

        touched += await _db.Accounts
            .Where(a => a.UserId == userId)
            .ExecuteDeleteAsync(ct);

        touched += await _db.CoachingPrompts
            .Where(c => c.UserId == userId)
            .ExecuteDeleteAsync(ct);

        touched += await _db.AIRiskAdvices
            .Where(a => a.UserId == userId)
            .ExecuteDeleteAsync(ct);

        touched += await _db.ImportJobs
            .Where(i => i.UserId == userId)
            .ExecuteDeleteAsync(ct);

        // AttachmentQuotaAudits do NOT have a user_id column (it's a
        // sweep log keyed by attachment_id). Hard-delete orphan
        // attachment audit rows that reference now-purged attachments.
        // The cascade above removed trade_attachments; their audit rows
        // are unreachable via FK after that, but the row remains until
        // a separate cleanup pass. We do NOT touch them here — they
        // carry no PII and are within the 90-day audit retention
        // policy (Wave 9 slice 9b.1).

        return touched;
    }
}