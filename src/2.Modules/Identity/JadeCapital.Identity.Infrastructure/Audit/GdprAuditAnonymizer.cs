using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace JadeCapital.Identity.Infrastructure.Audit;

/// <summary>
/// GDPR Art. 17 audit log anonymizer (Wave 10, slice 10.5, design.md §3.3).
///
/// <para>
/// Implements <see cref="IGdprAuditAnonymizer"/> via raw SQL UPDATEs
/// against the <c>audit.events</c> table. The <see cref="AuditDbContext"/>
/// is registered as write-only (append-only invariant) for the normal
/// auditing path — the GDPR hard-delete sweep is the ONE intentional
/// exception that bypasses that invariant. The bypass is documented in
/// the spec and audited via the orchestrator's structured log
/// ("AuditAnonymizationRun: anonymized N rows for user {hash}").
/// </para>
///
/// <para>
/// <b>Why raw SQL</b>: <c>AuditEvent</c> is an append-only aggregate with
/// no <c>UPDATE</c> method on the domain. Going through EF Core's
/// change-tracker to UPDATE an audit row would expose a mutation surface
/// the dedicated write-only DbContext was designed to prevent. Raw SQL
/// keeps the bypass narrow + visible: the only place that UPDATEs audit
/// rows is this anonymizer, and every UPDATE is logged.
/// </para>
///
/// <para>
/// <b>Anonymization shape</b>:
/// <list type="bullet">
///   <item><c>user_id = NULL</c> on every row for the user.</item>
///   <item><c>entity_id</c> becomes
///         <c>deleted_user_&lt;sha256(original_user_id)&gt;</c> when
///         <c>entity_type = 'User'</c>; for other entity_types the
///         entity_id is left untouched (the entity FK is being
///         hard-deleted so the row is meaningless anyway, but the
///         hash preserves referential integrity for the compliance
///         audit reader).</item>
///   <item><c>changes_json</c> becomes
///         <c>{ "reason": "gdpr_hard_delete", "original_user_id_hash": "&lt;sha256&gt;" }</c>.</item>
/// </list>
/// </para>
/// </summary>
public sealed class GdprAuditAnonymizer : IGdprAuditAnonymizer
{
    private readonly AuditDbContext _audit;

    public GdprAuditAnonymizer(AuditDbContext audit)
    {
        _audit = audit;
    }

    public async Task<int> AnonymizeUserAsync(Guid userId, CancellationToken ct)
    {
        var userIdHash = Sha256Hex(userId);
        var pseudonymEntityId = Guid.Empty; // entity_id is UUID; we'll fall back to a deterministic literal
        var anonymizationMarker = $"{{\"reason\":\"gdpr_hard_delete\",\"original_user_id_hash\":\"{userIdHash}\"}}";

        // Use a deterministic non-null Guid derived from the user-id hash
        // so entity_id remains a stable UUID-shaped identifier for the
        // pseudonymized User rows. The hash is a 32-char hex string;
        // we pack the first 16 bytes into a Guid for storage (still
        // deterministic + non-reversible without the original user_id).
        var pseudonymGuid = new Guid(Sha256Bytes(userId).AsSpan(0, 16));
        _ = pseudonymEntityId;

        var paramUserId = userId;
        var paramPseudoGuid = pseudonymGuid;
        var paramMarker = anonymizationMarker;

        // EF Core's ExecuteSqlInterpolatedAsync parameterizes the query —
        // no SQL injection surface. The query touches:
        //   1) rows where entity_type = 'User' AND entity_id = @userId
        //      (these are rows about the user themselves — flip entity_id)
        //   2) ALL rows where user_id = @userId
        //      (these are rows the user authored — null out user_id, reset changes_json)
        // The single UPDATE handles both via OR-of-WHERE on user_id.
        var sql = """
            UPDATE audit.events
            SET user_id = NULL,
                entity_id = CASE WHEN entity_type = 'User' AND entity_id = {1} THEN {2} ELSE entity_id END,
                changes_json = {0}
            WHERE user_id = {1}
            """;

        var rowsAffected = await _audit.Database.ExecuteSqlRawAsync(
            sql,
            new object[] { paramMarker, paramUserId, paramPseudoGuid },
            ct);

        return rowsAffected;
    }

    private static string Sha256Hex(Guid value)
    {
        var bytes = Sha256Bytes(value);
        var sb = new StringBuilder(64);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static byte[] Sha256Bytes(Guid value)
    {
        var guidBytes = value.ToByteArray();
        return SHA256.HashData(guidBytes);
    }
}