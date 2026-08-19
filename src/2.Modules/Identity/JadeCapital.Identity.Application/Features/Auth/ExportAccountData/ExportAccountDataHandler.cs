using System.Runtime.CompilerServices;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Identity.Application.Features.Auth.ExportAccountData;

// ============================================================================
//  ExportAccountDataHandler — Wave 11 slice 11.3
//
//  Handles <see cref="ExportAccountDataQuery"/>. Returns a stream of
//  structured sections covering the user's Identity-owned data only.
//
//  <para>
//  GDPR Art. 20 (right to data portability) requires a
//  "structured, commonly used and machine-readable format". This
//  handler emits a per-section <see cref="IAsyncEnumerable{T}"/> so
//  the endpoint can stream the JSON without buffering all sections
//  in memory. For a user with thousands of audit-loggable events, an
//  in-memory list would blow up the heap.
//
//  <b>Sections emitted (Identity-owned only, slice 11.3 scope)</b>:
//  <list type="bullet">
//    <item><c>user_profile</c> — id, email, display name, role, status,
//          timezone, last login, attachment quota, tenant id.</item>
//    <item><c>password_history</c> — changed_at + hash-prefix per
//          retained entry. Hashes only (prefix is the first 8 chars
//          for the user's own visibility, not enough to brute-force).</item>
//    <item><c>gdpr_consent</c> — ToS version, Privacy version, accepted
//          timestamp. Legacy pre-Wave 10.5 accounts have NULL fields.</item>
//  </list>
//  </para>
//
//  <para>
//  <b>What's NOT included</b> (out of scope per tasks.md §11.3):
//  <list type="bullet">
//    <item>audit.events rows — for OPS, not user data portability.</item>
//    <item>Refresh tokens — would expose the inner workings of auth;
//          the user can revoke + observe sessions via the existing
//          DELETE /api/users/me flow.</item>
//    <item>Risk profile — observable via GET /api/risk-profile.</item>
//    <item>Cross-module data (trades, accounts, journal entries) —
//          each module will export its own data via a follow-up
//          Contracts-based reader pattern.</item>
//  </list>
//  </para>
//
//  <para>
//  <b>Audit row</b>: a single <see cref="AuditAction.Updated"/>
//  audit.events row is written with <c>"action":"gdpr_data_export"</c>
//  in <c>changes_json</c> so compliance can prove the user invoked
//  the endpoint on a given date.
//  </para>
// ============================================================================

public sealed class ExportAccountDataHandler
    : IRequestHandler<ExportAccountDataQuery, Result<ExportAccountDataResult>>
{
    private readonly IUserRepository _userRepo;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;
    private readonly ILogger<ExportAccountDataHandler> _logger;

    public ExportAccountDataHandler(
        IUserRepository userRepo,
        IAuditLogger audit,
        IClock clock,
        ILogger<ExportAccountDataHandler> logger)
    {
        _userRepo = userRepo;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Initial wire-format version. Bump on breaking JSON
    /// shape changes (added fields are non-breaking).</summary>
    private const string FormatVersionValue = "1.0";

    public async Task<Result<ExportAccountDataResult>> Handle(
        ExportAccountDataQuery req, CancellationToken ct)
    {
        var user = await _userRepo.GetByIdAsync(req.UserId, ct);
        if (user is null)
        {
            _logger.LogInformation(
                "ExportAccountData: user {UserId} not found; returning 404.",
                req.UserId);
            return Result.Failure<ExportAccountDataResult>(Error.NotFound(
                "identity.user_not_found",
                "User not found."));
        }

        // Compliance audit row — fire once for the whole export.
        // We do NOT per-section audit (would explode audit volume; the
        // export is one logical GDPR action). The string carries the
        // canonical `gdpr_data_export` token so compliance can grep
        // audit.events for export events.
        var exportedAt = _clock.UtcNow;
        await _audit.LogAsync(new AuditEventEntry(
            EntityType: "User",
            EntityId: req.UserId,
            Action: AuditAction.Updated,
            TenantId: user.TenantId?.Value,
            UserId: req.UserId,
            ChangesJson:
                "{\"action\":\"gdpr_data_export\",\"format_version\":\""
                + FormatVersionValue + "\"}",
            OccurredAt: exportedAt), ct);

        var result = new ExportAccountDataResult(
            UserId: req.UserId,
            FormatVersion: FormatVersionValue,
            ExportedAt: exportedAt,
            Sections: ExportSections(user, ct));

        _logger.LogInformation(
            "ExportAccountData: user {UserId} exported at {At}; format={Version}.",
            req.UserId, exportedAt, FormatVersionValue);

        return Result.Success(result);
    }

    /// <summary>
    /// Yields one <see cref="ExportSection"/> per GDPR-relevant
    /// Identity-owned surface. The first section is always the user
    /// profile (compliance + tooling convention).
    /// </summary>
    private static async IAsyncEnumerable<ExportSection> ExportSections(
        User user,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // 1. User profile.
        yield return new UserProfileSection(new
        {
            id = user.Id,
            email = user.Email,
            display_name = user.DisplayName,
            role = user.Role.ToString(),
            status = user.Status.ToString(),
            timezone = user.Timezone,
            email_confirmed_at = user.EmailConfirmedAt,
            last_login_at = user.LastLoginAt,
            created_at = user.CreatedAt,
            updated_at = user.UpdatedAt,
            tenant_id = user.TenantId?.Value,
            attachment_quota_bytes = user.AttachmentQuotaBytes,
            attachment_used_bytes = user.AttachmentUsedBytes,
        });

        // 2. Password history metadata. The User aggregate already
        //    exposes the history via the EF navigation —
        //    `user.PasswordHistory` (PasswordHistoryEntry).
        //    NEVER serialise the full hash; expose a prefix so the
        //    user can confirm "I recognise this hash from when I
        //    changed my password", without enough entropy to brute-
        //    force (a 12-char salt + 100k PBKDF2 iterations are the
        //    primary defenses, not the prefix).
        var historyEntries = new List<object>();
        foreach (var entry in user.PasswordHistory)
        {
            historyEntries.Add(new
            {
                changed_at = entry.ChangedAt,
                superseded_hash_prefix = SafeHashPrefix(entry.Hash),
            });
        }
        yield return new PasswordHistorySection(new
        {
            entries = historyEntries,
            total_entries = historyEntries.Count,
        });

        // 3. GDPR consent. NULL fields are valid for legacy pre-Wave 10.5 accounts.
        yield return new GdprConsentSection(new
        {
            accepted_terms_version = user.AcceptedTermsVersion,
            accepted_privacy_version = user.AcceptedPrivacyVersion,
            accepted_at = user.AcceptedAt,
            soft_deleted_at = user.SoftDeletedAt,
            scheduled_hard_delete_at = user.ScheduledHardDeleteAt,
        });

        // NOTE: audit.events rows are deliberately NOT included.
        // The export is for the user's own data per Art. 20; the
        // audit trail is internal OPS data and is excluded by
        // design (tasks.md §11.3 Phase 8).
        await Task.CompletedTask;
    }

    /// <summary>First 8 chars of the hash, or "<c>(none)</c>" for the
    /// pre-PBKDF2 legacy empty-string password slots. The prefix is
    /// enough for the user to recognise ("yeah that was my password
    /// from back then") without enough material to crack.</summary>
    private static string SafeHashPrefix(string hash)
    {
        if (string.IsNullOrEmpty(hash)) return "(none)";
        return hash.Length <= 8 ? hash : hash[..8];
    }
}
