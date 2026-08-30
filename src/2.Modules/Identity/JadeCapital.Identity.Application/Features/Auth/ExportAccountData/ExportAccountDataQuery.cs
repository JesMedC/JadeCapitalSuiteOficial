using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Identity.Application.Features.Auth.ExportAccountData;

// ============================================================================
//  ExportAccountDataQuery — Wave 11 slice 11.3
//
//  GDPR Art. 20 (data portability) query. The handler yields the
//  authenticated user's data as a stream of structured, machine-readable
//  sections (JSON wire format).
//
//  <para>
//  <b>Architectural scope (intentional, slice 11.3)</b>: the handler
//  is bound to Identity-owned data only — user profile, refresh
//  tokens metadata, password history metadata, risk profile, and
//  GDPR consent. It does NOT include trades / accounts / journal
//  entries (Trading-owned data) or subscriptions / billing data. A
//  future slice will add per-module export providers via the
//  Contracts projection pattern (<c>IEnumerable&lt;IUserDataExportProvider&gt;</c>),
//  mirroring the Wave 10.5 <c>IUserCascadeDeletor</c> aggregation
//  precedent.
//  </para>
//
//  <para>
//  <b>Why an <see cref="IAsyncEnumerable{T}"/> stream</b>: GDPR Art. 20
//  requires "structured, commonly used and machine-readable format".
//  Yielding one section at a time avoids buffering the entire export
//  in memory — for a user with thousands of audit-loggable events, an
//  in-memory list would blow up the heap. The endpoint serializes on
//  the fly into a JSON object with named properties (so callers can
//  index by name, e.g. <c>export.trades</c>).
//  </para>
// ============================================================================

public sealed record ExportAccountDataQuery(Guid UserId)
    : IRequest<Result<ExportAccountDataResult>>;

/// <summary>
/// Response envelope for <c>GET /api/users/me/export</c>. The
/// <see cref="Sections"/> stream is awaited by the endpoint, which
/// serializes each yielded <see cref="ExportSection"/> into a JSON
/// property keyed by <see cref="ExportSection.SectionType"/>.
/// </summary>
public sealed record ExportAccountDataResult(
    Guid UserId,
    string FormatVersion,
    DateTimeOffset ExportedAt,
    IAsyncEnumerable<ExportSection> Sections);

/// <summary>
/// One named section of the GDPR export. The wire format pairs a
/// stable <see cref="SectionType"/> key (e.g. <c>"user_profile"</c>)
/// with an arbitrary <see cref="Data"/> payload. Concrete section
/// types (defined below) carry semantically-meaningful names so the
/// FE / compliance tooling can pattern-match.
/// </summary>
public abstract record ExportSection(string SectionType, object Data);

/// <summary>The authenticated user's account profile snapshot.</summary>
public sealed record UserProfileSection(object Data)
    : ExportSection("user_profile", Data);

/// <summary>Refresh-token summary (issuance + revocation timestamps,
/// created-by IP, user-agent). Token hashes only — NEVER plaintext
/// token values.</summary>
public sealed record RefreshTokensSection(object Data)
    : ExportSection("refresh_tokens", Data);

/// <summary>Active risk-profile snapshot (capital, risk-per-trade,
/// risk-reward target, max drawdown). NULL for users who never
/// invoked <c>PUT /api/risk-profile</c>.</summary>
public sealed record RiskProfileSection(object Data)
    : ExportSection("risk_profile", Data);

/// <summary>Password history metadata (changed_at + which hash was
/// superseded). Hashes only — the user can already see their
/// passwords are hash-protected by the registration flow.</summary>
public sealed record PasswordHistorySection(object Data)
    : ExportSection("password_history", Data);

/// <summary>GDPR-consent record: ToS version, Privacy version, accepted
/// timestamp. NULL fields for legacy accounts pre-Wave 10.5.</summary>
public sealed record GdprConsentSection(object Data)
    : ExportSection("gdpr_consent", Data);
