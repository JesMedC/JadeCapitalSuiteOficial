using System.Text.Json;
using JadeCapital.Shared.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.MultiTenancy;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.TradeAttachments;
using JadeCapital.Trading.Domain.TradeReviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace JadeCapital.Trading.Infrastructure.Audit;

/// <summary>
/// Per-aggregate audit decorator for <see cref="ITradeReviewRepository"/>
/// (Wave 8, slice 8a.2).
///
/// <para>
/// <b>Why this decorator is bespoke (does NOT use the generic
/// <c>DecoratedRepository&lt;T&gt;</c> helper)</b>: <see cref="ITradeReviewRepository"/>
/// is bespoke (does NOT extend <c>IRepository&lt;TradeReview&gt;</c>). It
/// exposes first-class attachment ops
/// (<see cref="ITradeReviewRepository.AddAttachmentAsync"/>,
/// <see cref="ITradeReviewRepository.UpdateAttachmentAsync"/>,
/// <see cref="ITradeReviewRepository.RemoveAttachmentAsync"/>) for the
/// <see cref="TradeAttachment"/> child entity + user-scoped read methods
/// (<see cref="ITradeReviewRepository.FindByIdAsync"/>,
/// <see cref="ITradeReviewRepository.FindByTradeIdAsync"/>). Extending
/// <c>IRepository&lt;TradeReview&gt;</c> would force a
/// <c>GetByIdAsync(Guid, ct)</c> shape that ignores cross-user scope AND
/// would lose the attachment op signatures. Following the 7a.1
/// <c>RiskProfileAuditDecorator</c> + 7b.1 <c>TradeAuditDecorator</c> +
/// 7b.2 <c>JournalEntryAuditDecorator</c> + 8a.1
/// <c>AccountAuditDecorator</c> + 8a.2 <c>AlertAuditDecorator</c> precedent
/// for bespoke repositories, this decorator is bespoke too.
/// </para>
///
/// <para>
/// <b>CRITICAL — slice 8a.2 attachment ops deviation (orchestrator
/// preflight decision 7)</b>: <see cref="AddAttachmentAsync"/>,
/// <see cref="UpdateAttachmentAsync"/>, and <see cref="RemoveAttachmentAsync"/>
/// are forwarded to the inner WITHOUT emitting audit rows.
/// Rationale: <see cref="TradeAttachment"/> is a child entity of the
/// review, not a separately-audited aggregate. The review's Update
/// events + handler-side MinIO cleanup log already capture attachment
/// lifecycle. Adding per-attachment audit rows would be noisy without
/// proportional compliance value. This is documented in the class
/// summary above + the design.md §4.
/// </para>
///
/// <list type="bullet">
///   <item>Forwarding <see cref="IAlertRepository.ListByUserAsync"/>-
///         style reads (<see cref="FindByTradeIdAsync"/>,
///         <see cref="FindByIdAsync"/>,
///         <see cref="ListAttachmentsByReviewIdAsync"/>,
///         <see cref="CountAttachmentsByReviewIdAsync"/>,
///         <see cref="FindAttachmentByIdAsync"/>,
///         <see cref="GetTradeIdByAttachmentIdAsync"/>) to the inner
///         without audit logging. Reads are not audited.</item>
///   <item>Wrapping <see cref="AddAsync(TradeReview, CancellationToken)"/>
///         with audit logging — emits <see cref="AuditAction.Created"/>
///         on success.</item>
///   <item>Wrapping <see cref="UpdateAsync(TradeReview, CancellationToken)"/>
///         with the <b>cross-tenant <c>IsOwner</c> check</b> on
///         <see cref="TradeReview.UserId"/> + audit logging with the
///         before/after diff (covers <c>setupUsed</c>, <c>lessons</c>,
///         <c>rating</c> transitions). Cross-tenant rejection emits
///         <see cref="AuditAction.Denied"/> + throws
///         <see cref="UnauthorizedAccessException"/>. The inner
///         <c>UpdateAsync</c> is NEVER reached on rejection.</item>
///   <item>Forwarding <see cref="AddAttachmentAsync"/>,
///         <see cref="UpdateAttachmentAsync"/>,
///         <see cref="RemoveAttachmentAsync"/> to the inner WITHOUT
///         audit logging. Documented design decision (above).</item>
/// </list>
///
/// <para>
/// <b>DbContext parameter type</b>: the decorator accepts
/// <see cref="DbContext"/> (base type) rather than the concrete
/// <see cref="Persistence.TradingDbContext"/> so the unit-test fixture
/// can register a SQLite-compatible helper DbContext. In production DI,
/// the registered <c>TradingDbContext</c> is resolved into the
/// <see cref="DbContext"/> parameter.
/// </para>
///
/// <para>
/// Registered via Scrutor:
/// <c>services.Decorate&lt;ITradeReviewRepository, TradeReviewAuditDecorator&gt;()</c>
/// in <see cref="DependencyInjection.TradingModuleRegistration"/>.
/// </para>
/// </summary>
public sealed class TradeReviewAuditDecorator : ITradeReviewRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ITradeReviewRepository _inner;
    private readonly IAuditLogger _audit;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IDiff _diff;
    private readonly DbContext? _db;

    public TradeReviewAuditDecorator(
        ITradeReviewRepository inner,
        IAuditLogger audit,
        ITenantContext tenant,
        IClock clock,
        IDiff? diff = null,
        DbContext? db = null)
    {
        _inner = inner;
        _audit = audit;
        _tenant = tenant;
        _clock = clock;
        _diff = diff ?? new JsonDiff();
        _db = db;
    }

    // ===== Review read methods — no audit logging =====

    public Task<TradeReview?> FindByTradeIdAsync(
        Guid tradeId, Guid userId, CancellationToken ct)
        => _inner.FindByTradeIdAsync(tradeId, userId, ct);

    public Task<TradeReview?> FindByIdAsync(
        Guid reviewId, Guid userId, CancellationToken ct)
        => _inner.FindByIdAsync(reviewId, userId, ct);

    // ===== Attachment read methods — no audit logging =====

    public Task<IReadOnlyList<TradeAttachment>> ListAttachmentsByReviewIdAsync(
        Guid reviewId, Guid userId, CancellationToken ct)
        => _inner.ListAttachmentsByReviewIdAsync(reviewId, userId, ct);

    public Task<int> CountAttachmentsByReviewIdAsync(
        Guid reviewId, CancellationToken ct)
        => _inner.CountAttachmentsByReviewIdAsync(reviewId, ct);

    public Task<TradeAttachment?> FindAttachmentByIdAsync(
        Guid attachmentId, Guid userId, CancellationToken ct)
        => _inner.FindAttachmentByIdAsync(attachmentId, userId, ct);

    public Task<Guid?> GetTradeIdByAttachmentIdAsync(
        Guid attachmentId, CancellationToken ct)
        => _inner.GetTradeIdByAttachmentIdAsync(attachmentId, ct);

    // ===== Review mutation methods with audit logging =====

    public async Task AddAsync(TradeReview review, CancellationToken ct)
    {
        await _inner.AddAsync(review, ct);
        await TryAuditAsync(BuildEntry(review, AuditAction.Created, changesJson: null), ct);
    }

    public async Task UpdateAsync(TradeReview review, CancellationToken ct)
    {
        if (!IsOwner(review))
        {
            // Slice 8a.2 deviation mirrors the Wave 7 7b.1 TradeAuditDecorator +
            // 7b.2 JournalEntryAuditDecorator + 8a.1 AccountAuditDecorator +
            // 8a.2 AlertAuditDecorator: the audit row's Action is
            // AuditAction.Denied (not the would-have-been action) so
            // compliance officers can filter cross-tenant attempts
            // separately from legitimate state changes.
            await LogDeniedAsync(review, ct);
            throw new UnauthorizedAccessException(
                $"TradeReview {review.Id} does not belong to current user (cross-tenant attempt).");
        }

        // Snapshot before — use EF's ChangeTracker.OriginalValues (when the
        // inner repository is EF-backed) to capture the pre-mutation
        // entity. Falls back to a full post-mutation JSON snapshot when
        // no DbContext is available or the entity isn't tracked.
        var before = ResolveBefore(review);
        await _inner.UpdateAsync(review, ct);

        var changesJson = SafeDiff(before, review);
        await TryAuditAsync(BuildEntry(review, AuditAction.Updated, changesJson), ct);
    }

    // ===== Attachment mutation methods — FORWARDED WITHOUT AUDIT =====
    //
    // Per orchestrator preflight decision 7: TradeAttachment is a child
    // entity of the review, not a separately-audited aggregate. The
    // review's Update events + handler-side MinIO cleanup log already
    // capture attachment lifecycle. Adding per-attachment audit rows
    // would be noisy without proportional compliance value. This is the
    // documented design decision (see class summary + design.md §4).

    public Task AddAttachmentAsync(TradeAttachment attachment, CancellationToken ct)
        => _inner.AddAttachmentAsync(attachment, ct);

    public Task UpdateAttachmentAsync(TradeAttachment attachment, CancellationToken ct)
        => _inner.UpdateAttachmentAsync(attachment, ct);

    public Task<string?> RemoveAttachmentAsync(
        Guid attachmentId, Guid userId, CancellationToken ct)
        => _inner.RemoveAttachmentAsync(attachmentId, userId, ct);

    // ===== Helpers =====

    /// <summary>
    /// Returns true iff the review belongs to the current user. When no
    /// user is resolved (anonymous / service context), the decorator
    /// allows the operation to proceed — system actors bypass the
    /// user-scope check (matches the Wave 6 + 7 + 8a.1 + 8a.2 precedent).
    /// </summary>
    private bool IsOwner(TradeReview review)
        => !_tenant.CurrentUserId.HasValue
            || review.UserId == _tenant.CurrentUserId.Value;

    /// <summary>
    /// Audit-log a denied cross-tenant attempt. The entry carries the
    /// calling user's id (NOT the entity's user id) so the security trail
    /// shows WHO attempted the cross-tenant access. Fire-and-forget:
    /// never throws.
    /// </summary>
    private Task LogDeniedAsync(TradeReview review, CancellationToken ct)
    {
        var entry_row = new AuditEventEntry(
            EntityType: nameof(TradeReview),
            EntityId: review.Id,
            Action: AuditAction.Denied,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: null,
            OccurredAt: _clock.UtcNow);
        return _audit.LogAsync(entry_row, ct);
    }

    /// <summary>
    /// Defense-in-depth: the <see cref="IAuditLogger"/> contract is to
    /// never throw, but a buggy implementation could. The decorator
    /// swallows any exception here so the main mutation is NEVER rolled
    /// back by a misbehaving audit.
    /// </summary>
    private async Task TryAuditAsync(AuditEventEntry auditEntry, CancellationToken ct)
    {
        try
        {
            await _audit.LogAsync(auditEntry, ct);
        }
        catch
        {
            // Swallow. The decorator's contract is "main mutation must
            // succeed even if audit fails".
        }
    }

    /// <summary>
    /// Builds the <see cref="AuditEventEntry"/> for the given review +
    /// action. <c>EntityId</c> is the review's primary key;
    /// <c>EntityType</c> is the review type's name. <c>TenantId</c> +
    /// <c>UserId</c> come from the <see cref="ITenantContext"/>.
    /// </summary>
    private AuditEventEntry BuildEntry(TradeReview review, AuditAction action, string? changesJson)
        => new(
            EntityType: nameof(TradeReview),
            EntityId: review.Id,
            Action: action,
            TenantId: _tenant.Current?.Value,
            UserId: _tenant.CurrentUserId,
            ChangesJson: changesJson,
            OccurredAt: _clock.UtcNow);

    /// <summary>
    /// Resolves the pre-mutation snapshot. When the decorator is wired
    /// with a <see cref="DbContext"/> (the production path), we use EF's
    /// <see cref="EntityEntry.OriginalValues"/> — the change tracker holds
    /// the pre-modification values even after the caller mutates the
    /// in-memory entity. Without the DbContext (legacy / in-memory test
    /// path), we return null and the diff helper falls back to a full
    /// post-mutation snapshot.
    /// </summary>
    private TradeReview? ResolveBefore(TradeReview review)
    {
        if (_db is not null)
        {
            var tracked = _db.Entry(review);
            if (tracked.State != EntityState.Detached)
            {
                return tracked.OriginalValues.ToObject() as TradeReview;
            }
        }
        return null;
    }

    /// <summary>
    /// Computes the per-field {before, after} diff between the pre-update
    /// snapshot and the post-update review. Falls back to a JSON snapshot
    /// of the post-update entity when the diff helper throws or returns
    /// empty. Mirrors the 7b.1 <c>TradeAuditDecorator.SafeDiff</c> +
    /// 7b.2 <c>JournalEntryAuditDecorator.SafeDiff</c> + 8a.1
    /// <c>AccountAuditDecorator</c> + 8a.2 <c>AlertAuditDecorator</c>
    /// pattern.
    /// </summary>
    private string? SafeDiff(TradeReview? before, TradeReview after)
    {
        if (before is null)
            return JsonSerializer.Serialize(after, JsonOptions);
        try
        {
            var diff = _diff.Compute(before, after);
            return string.IsNullOrEmpty(diff) ? null : diff;
        }
        catch
        {
            return JsonSerializer.Serialize(after, JsonOptions);
        }
    }
}
