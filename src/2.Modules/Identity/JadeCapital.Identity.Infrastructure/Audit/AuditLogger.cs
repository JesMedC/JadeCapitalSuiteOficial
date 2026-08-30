using JadeCapital.Identity.Domain.Audit;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Identity.Infrastructure.Audit;

/// <summary>
/// Real <see cref="IAuditLogger"/> EF Core implementation (Wave 6, slice 6d.2).
///
/// <para>
/// The 6d.1 placeholder (<see cref="NoOpAuditLogger"/>) accepted every call
/// without persisting; this impl writes the entry to the dedicated
/// <see cref="AuditDbContext"/> (separate schema, separate migration history
/// — see <see class="AuditDbContext"/> for the defense-in-depth rationale).
/// </para>
/// <list type="bullet">
///   <item><b>Enrichment</b>: <see cref="ITenantContext.Current"/> fills in
///         <c>TenantId</c> when the entry has null; <see cref="ITenantContext.CurrentUserId"/>
///         fills in <c>UserId</c> when null. Caller-supplied values win.</item>
///   <item><b>No-throw</b>: every exception inside the try block is caught
///         and logged as a warning. The audit log is a defense layer; a
///         failed audit write MUST NOT roll back a successful business
///         mutation.</item>
///   <item><b>Clock</b>: <see cref="IClock.UtcNow"/> stamps <c>OccurredAt</c>
///         — the aggregate's <c>Create</c> owns the timestamp, so the
///         logger just forwards the entry verbatim (enrichment aside).</item>
/// </list>
/// </summary>
public sealed class AuditLogger : IAuditLogger
{
    private readonly AuditDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(
        AuditDbContext db,
        ITenantContext tenant,
        IClock clock,
        ILogger<AuditLogger> logger)
    {
        _db = db;
        _tenant = tenant;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Persists the <paramref name="entry"/> to <c>audit.events</c>.
    ///
    /// <para>
    /// Contract: NEVER throws. Catches every exception, logs a warning,
    /// returns. The caller awaits only for back-pressure.
    /// </para>
    /// </summary>
    public async Task LogAsync(AuditEventEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            // Enrichment: caller-supplied values win, ITenantContext fills the gaps.
            var enriched = entry with
            {
                TenantId = entry.TenantId ?? _tenant.Current?.Value,
                UserId = entry.UserId ?? _tenant.CurrentUserId,
            };

            var createResult = AuditEvent.Create(enriched, _clock);
            if (createResult.IsFailure)
            {
                _logger.LogWarning(
                    "Audit event validation failed: {Code} — {Message}",
                    createResult.Error.Code,
                    createResult.Error.Message);
                return;
            }

            await _db.AuditEvents.AddAsync(createResult.Value, ct);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Fire-and-forget — never throw. The main mutation has already
            // committed; failing the audit is observable but non-blocking.
            _logger.LogWarning(
                ex,
                "Audit log write failed for {EntityType}/{EntityId}. Main mutation was committed.",
                entry.EntityType,
                entry.EntityId);
        }
    }
}