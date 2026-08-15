using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Identity.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ITemporaryCredentialRepository"/>.
///
/// Slice 0b introduced the application abstraction (handlers + tests) but the
/// persistence wiring was deferred to slice 0c, alongside the supersession
/// addendum. The unique partial index
/// <c>ux_temporary_credentials_user_active</c> provides the
/// latest-only persistence guarantee; this class is the application boundary
/// that the ForgotPasswordHandler uses.
/// </summary>
public sealed class TemporaryCredentialRepository : ITemporaryCredentialRepository
{
    private readonly IdentityDbContext _db;

    public TemporaryCredentialRepository(IdentityDbContext db) { _db = db; }

    public async Task<Result<TemporaryCredential>> ReserveAsync(Guid id, Guid userId, int generation, string hash, CancellationToken ct = default)
    {
        var utcNow = DateTimeOffset.UtcNow;
        var reserved = TemporaryCredential.Reserve(id, userId, generation, CredentialHash.From(hash), utcNow);
        if (reserved.IsFailure) return Result.Failure<TemporaryCredential>(reserved.Error);
        await _db.TemporaryCredentials.AddAsync(reserved.Value, ct);
        return Result.Success(reserved.Value);
    }

    public async Task<int> LatestGenerationAsync(Guid userId, CancellationToken ct = default)
        => await _db.TemporaryCredentials
            .Where(t => t.UserId == userId)
            .Select(t => (int?)t.Generation)
            .MaxAsync(ct) ?? 0;

    public async Task<Result> ActivateAsync(Guid id, int expectedGeneration, CancellationToken ct = default)
    {
        var row = await _db.TemporaryCredentials.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (row is null) return Result.Failure(IdentityDomainErrorsForTemp.NotFound);
        var utcNow = DateTimeOffset.UtcNow;
        var activate = row.Activate(utcNow, expectedGeneration);
        if (activate.IsFailure) return activate;
        // SaveChanges commits the status transition in the caller's transaction.
        return Result.Success();
    }

    public Task<TemporaryCredential?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => _db.TemporaryCredentials.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<TemporaryCredential?> FindLatestActivatedAsync(Guid userId, CancellationToken ct = default)
        => _db.TemporaryCredentials.FirstOrDefaultAsync(
            t => t.UserId == userId && t.Status == TemporaryCredentialStatus.Activated, ct);

    public Task<TemporaryCredential?> FindByGrantJtiAsync(string grantJti, CancellationToken ct = default)
        => _db.TemporaryCredentials.FirstOrDefaultAsync(t => t.GrantJti == grantJti, ct);

    public async Task<Result> ConsumeAsync(Guid id, string grantJti, DateTimeOffset utcNow, CancellationToken ct = default)
    {
        var row = await _db.TemporaryCredentials.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (row is null) return Result.Failure(IdentityDomainErrorsForTemp.NotFound);
        return row.MarkConsumed(utcNow, grantJti);
    }

    public async Task<Result> ConfirmConsumedAsync(Guid id, DateTimeOffset utcNow, CancellationToken ct = default)
    {
        var row = await _db.TemporaryCredentials.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (row is null) return Result.Failure(IdentityDomainErrorsForTemp.NotFound);
        // ConfirmConsumed is the post-change-password commit signal; once the
        // credential is in Consumed state, the atomic supersession must leave it
        // alone (idempotency contract from MarkSuperseded). Returning Success here
        // mirrors the slice-0b semantics: the change transaction has already
        // committed, so the consumed state is final.
        return row.Status == TemporaryCredentialStatus.Consumed
            ? Result.Success()
            : Result.Failure(IdentityDomainErrorsForTemp.NotActivated);
    }

    public async Task<int> SupersedeActiveAsync(Guid userId, CancellationToken ct = default)
    {
        var utcNow = DateTimeOffset.UtcNow;
        var activeRows = await _db.TemporaryCredentials
            .Where(t => t.UserId == userId && t.Status == TemporaryCredentialStatus.Activated)
            .ToListAsync(ct);
        foreach (var row in activeRows)
        {
            row.GetType(); // suppress unused warning under no-EF-tracker edge cases
            // Domain transition is idempotent: Activated -> Superseded once;
            // re-calls become no-ops. We intentionally go through the entity so
            // the invariant logic stays in the domain.
            var r = row.MarkSuperseded(utcNow);
            if (r.IsFailure) { /* consumed/missing/etc. — skip; sweeper is safe */ }
        }
        return activeRows.Count;
    }

    private static class IdentityDomainErrorsForTemp
    {
        public static readonly Error NotFound = Error.NotFound("temporary_credential.not_found", "Temporary credential not found.");
        public static readonly Error NotActivated = Error.Conflict("temporary_credential.not_activated", "Temporary credential is not activated.");
    }
}

public sealed class PasswordHistoryRepository : IPasswordHistoryRepository
{
    private readonly IdentityDbContext _db;
    public PasswordHistoryRepository(IdentityDbContext db) { _db = db; }

    public async Task<Result> AppendAsync(Guid userId, string displacedHash, DateTimeOffset changedAt, CancellationToken ct = default)
    {
        var entry = PasswordHistoryEntry.Create(Guid.NewGuid(), userId, displacedHash, changedAt);
        await _db.PasswordHistory.AddAsync(entry, ct);
        return Result.Success();
    }
}

/// <summary>
/// Refresh-token revocation adapter. Forwards to the existing
/// <see cref="RefreshTokenRepository"/> so callers that already depend on
/// <see cref="IRefreshTokenRepository"/> (Login/Refresh handlers) and the
/// recovery handlers (<see cref="IRefreshTokenRevoker"/>) share the same
/// persistence path.
/// </summary>
public sealed class RefreshTokenRevoker : IRefreshTokenRevoker
{
    private readonly IRefreshTokenRepository _tokens;
    public RefreshTokenRevoker(IRefreshTokenRepository tokens) { _tokens = tokens; }
    public Task RevokeAllAsync(Guid userId, CancellationToken ct = default)
        => _tokens.RevokeAllForUserAsync(userId, ct);
}