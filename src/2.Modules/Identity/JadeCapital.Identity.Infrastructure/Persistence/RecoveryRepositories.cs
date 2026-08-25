using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
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
    private readonly IClock _clock;

    public TemporaryCredentialRepository(IdentityDbContext db, IClock clock) { _db = db; _clock = clock; }

    public async Task<Result<TemporaryCredential?>> IssueActivatedAsync(Guid userId, string hash, CancellationToken ct = default)
    {
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            var user = await _db.Users
                .FromSqlInterpolated($"SELECT * FROM identity.users WHERE id = {userId} FOR UPDATE")
                .AsNoTracking()
                .SingleOrDefaultAsync(ct);
            var utcNow = _clock.UtcNow;
            if (user is null || !user.CanRecover(utcNow)) return Result.Success<TemporaryCredential?>(null);

            var generation = checked((await _db.TemporaryCredentials.Where(t => t.UserId == userId).Select(t => (int?)t.Generation).MaxAsync(ct) ?? 0) + 1);
            var active = await _db.TemporaryCredentials.Where(t => t.UserId == userId && t.Status == TemporaryCredentialStatus.Activated).ToListAsync(ct);
            foreach (var row in active)
                if (row.MarkSuperseded(utcNow) is { IsFailure: true } superseded)
                    return Result.Failure<TemporaryCredential?>(superseded.Error);
            if (active.Count > 0 && await _db.SaveChangesAsync(ct) < active.Count) return Result.Failure<TemporaryCredential?>(PersistenceFailed);

            var credentialHash = CredentialHash.Create(hash); if (credentialHash.IsFailure) return Result.Failure<TemporaryCredential?>(credentialHash.Error);
            var reserved = TemporaryCredential.Reserve(Guid.NewGuid(), userId, generation, credentialHash.Value, utcNow);
            if (reserved.IsFailure) return Result.Failure<TemporaryCredential?>(reserved.Error);
            var activated = reserved.Value.Activate(utcNow, generation);
            if (activated.IsFailure) return Result.Failure<TemporaryCredential?>(activated.Error);
            await _db.TemporaryCredentials.AddAsync(reserved.Value, ct);
            if (await _db.SaveChangesAsync(ct) < 1) return Result.Failure<TemporaryCredential?>(PersistenceFailed);
            await transaction.CommitAsync(ct);
            return Result.Success<TemporaryCredential?>(reserved.Value);
        }
        catch (DbUpdateException ex) when ((ex.InnerException as Npgsql.PostgresException)?.SqlState == "23505") { return Result.Failure<TemporaryCredential?>(ConcurrentIssuanceFailed); }
        catch (Exception) when (!ct.IsCancellationRequested) { return Result.Failure<TemporaryCredential?>(PersistenceFailed); }
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

    private static class IdentityDomainErrorsForTemp
    {
        public static readonly Error NotFound = Error.NotFound("temporary_credential.not_found", "Temporary credential not found.");
        public static readonly Error NotActivated = Error.Conflict("temporary_credential.not_activated", "Temporary credential is not activated.");
    }

    private static readonly Error PersistenceFailed = Error.Failure("temporary_credential.persistence_failed", "Temporary credential could not be committed.");
    private static readonly Error ConcurrentIssuanceFailed = Error.Failure("temporary_credential.concurrent_issuance_failed", "Temporary credential could not be committed.");
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
