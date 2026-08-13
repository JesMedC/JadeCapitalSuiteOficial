using JadeCapital.Identity.Domain.Common;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Domain.Authentication;

/// <summary>
/// Lifecycle states for a temporary recovery credential.
/// </summary>
public enum TemporaryCredentialStatus
{
    Pending = 0,
    Activated = 1,
    Consumed = 2,
    Superseded = 3,
}

/// <summary>
/// Temporary recovery credential — single-use, 24h expiry, latest-only activation.
///
/// Lifecycle:
///   Reserve (Pending) ─► Activate (Activated, ExpiresAt = ActivatedAt + 24h)
///                                       │
///                                       ▼
///                                  MarkConsumed (Consumed)
///
/// "Latest-only activation" is enforced on <see cref="Activate"/>: the caller
/// passes the current latest generation observed for the user; if this row's
/// generation is older than that, activation fails (CAS lost). This is what
/// makes earlier temporary passwords stop working once a newer reset email
/// is in flight.
/// </summary>
public sealed class TemporaryCredential : Entity<Guid>
{
    public const int LifetimeHours = 24;

    public Guid UserId { get; private set; }
    public int Generation { get; private set; }
    public string Hash { get; private set; } = default!;
    public TemporaryCredentialStatus Status { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public DateTimeOffset? SupersededAt { get; private set; }
    public string? GrantJti { get; private set; }

    // EF Core.
    private TemporaryCredential() { }

    private TemporaryCredential(
        Guid id,
        Guid userId,
        int generation,
        string hash,
        DateTimeOffset createdAt,
        DateTimeOffset initialExpiresAt) : base(id)
    {
        UserId = userId;
        Generation = generation;
        Hash = hash;
        Status = TemporaryCredentialStatus.Pending;
        ActivatedAt = null;
        ExpiresAt = initialExpiresAt;
        ConsumedAt = null;
        SupersededAt = null;
        GrantJti = null;
    }

    /// <summary>
    /// Reserves a new pending credential. Caller is responsible for choosing
    /// a monotonically increasing generation per user. Activation sets the
    /// real 24h expiry from the activation moment; the initial ExpiresAt is
    /// a placeholder and is overwritten on Activate.
    /// </summary>
    public static Result<TemporaryCredential> Reserve(
        Guid id,
        Guid userId,
        int generation,
        CredentialHash hash,
        DateTimeOffset utcNow)
    {
        if (id == Guid.Empty)
            return Result.Failure<TemporaryCredential>(IdentityDomainErrors.TemporaryCredential.IdRequired);

        if (userId == Guid.Empty)
            return Result.Failure<TemporaryCredential>(IdentityDomainErrors.User.IdRequired);

        if (generation <= 0)
            return Result.Failure<TemporaryCredential>(IdentityDomainErrors.TemporaryCredential.GenerationInvalid);

        if (string.IsNullOrWhiteSpace(hash))
            return Result.Failure<TemporaryCredential>(IdentityDomainErrors.Credential.HashRequired);

        var initialExpiry = utcNow.AddHours(LifetimeHours);
        return Result.Success(new TemporaryCredential(id, userId, generation, hash.Value, utcNow, initialExpiry));
    }

    /// <summary>
    /// Activates this pending credential. Caller passes the current latest
    /// generation observed for the user (typically queried from the DB).
    /// Activation succeeds ONLY when this row's generation matches that
    /// observed value exactly. Any mismatch — older (CAS lost because a newer
    /// row was reserved) OR newer (the caller observed a stale value and
    /// this row is actually newer than expected) — fails as superseded.
    ///
    /// "Newer than latest" rejection is critical: it stops a caller that
    /// missed a concurrent reservation from activating a row that was meant
    /// to be superseded by an even-newer one in flight. Slice 0b owns the
    /// atomic DB transaction that supersedes older Activated rows when a new
    /// recovery email is sent.
    ///
    /// Sets ActivatedAt and ExpiresAt = ActivatedAt + 24h.
    /// </summary>
    public Result Activate(DateTimeOffset utcNow, int latestGeneration)
    {
        if (Status != TemporaryCredentialStatus.Pending)
            return Result.Failure(IdentityDomainErrors.TemporaryCredential.NotPending);

        if (Generation != latestGeneration)
            return Result.Failure(IdentityDomainErrors.TemporaryCredential.Superseded);

        Status = TemporaryCredentialStatus.Activated;
        ActivatedAt = utcNow;
        ExpiresAt = utcNow.AddHours(LifetimeHours);
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Transitions Activated → Consumed (single-use). The grantJti ties this
    /// consumption to the recovery grant issued to the caller — replays of the
    /// same temporary credential produce a different grant jti and are rejected.
    /// </summary>
    public Result MarkConsumed(DateTimeOffset utcNow, string grantJti)
    {
        if (Status != TemporaryCredentialStatus.Activated)
            return Result.Failure(IdentityDomainErrors.TemporaryCredential.NotActivated);

        if (string.IsNullOrWhiteSpace(grantJti))
            return Result.Failure(IdentityDomainErrors.TemporaryCredential.GrantJtiRequired);

        Status = TemporaryCredentialStatus.Consumed;
        ConsumedAt = utcNow;
        GrantJti = grantJti;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Transitions Activated → Superseded (atomic supersession). Called by the
    /// ForgotPasswordHandler BEFORE reserving a new Pending generation so that
    /// the prior active credential can never be used to log in once a newer
    /// recovery email has been sent.
    ///
    /// Idempotency rules:
    /// - From Activated: success, transitions to Superseded.
    /// - From Superseded: success, no-op (sweeper retries are safe).
    /// - From Consumed: success, no-op (Consumed is terminal; the audit trail wins).
    /// - From Pending: failure — only an already-Activated row may be superseded.
    /// </summary>
    public Result MarkSuperseded(DateTimeOffset utcNow)
    {
        switch (Status)
        {
            case TemporaryCredentialStatus.Activated:
                Status = TemporaryCredentialStatus.Superseded;
                SupersededAt = utcNow;
                Touch();
                return Result.Success();
            case TemporaryCredentialStatus.Superseded:
            case TemporaryCredentialStatus.Consumed:
                return Result.Success();
            default:
                return Result.Failure(IdentityDomainErrors.TemporaryCredential.NotActivated);
        }
    }

    /// <summary>
    /// Returns true iff the credential is in Activated state AND has not yet
    /// expired AND has not been consumed. Used by the login handler.
    /// </summary>
    public bool IsUsable(DateTimeOffset utcNow)
        => Status == TemporaryCredentialStatus.Activated
           && ExpiresAt > utcNow;
}
