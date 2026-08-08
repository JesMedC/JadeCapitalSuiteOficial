using JadeCapital.Identity.Domain.Common;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Domain.Authentication;

/// <summary>
/// Refresh Token — entidad interna del agregado User.
///
/// Caracteristicas:
/// - Token opaco (NO JWT). Solo guardamos su HASH SHA-256.
/// - Rotacion: cada refresh emite un nuevo par (revoca el viejo con link al nuevo).
/// - Reuse detection: si se presenta un refresh revocado, se considera compromiso
///   y se DEBEN revocar todos los tokens activos del usuario.
/// - Vida util fija (Ttl). Pasado eso, ya no sirve.
/// </summary>
public sealed class RefreshToken : Entity<Guid>
{
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public string? CreatedByIp { get; private set; }
    public string? UserAgent { get; private set; }

    private RefreshToken() { }

    private RefreshToken(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string? ip,
        string? userAgent) : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        CreatedByIp = ip;
        UserAgent = userAgent;
    }

    public static Result<RefreshToken> Issue(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string? ip = null,
        string? userAgent = null)
    {
        if (id == Guid.Empty)
            return Result.Failure<RefreshToken>(IdentityDomainErrors.RefreshToken.NotFound);

        if (userId == Guid.Empty)
            return Result.Failure<RefreshToken>(IdentityDomainErrors.User.IdRequired);

        if (string.IsNullOrWhiteSpace(tokenHash))
            return Result.Failure<RefreshToken>(IdentityDomainErrors.RefreshToken.NotFound);

        if (expiresAt <= issuedAt)
            return Result.Failure<RefreshToken>(IdentityDomainErrors.RefreshToken.Expired);

        return Result.Success(new RefreshToken(id, userId, tokenHash, issuedAt, expiresAt, ip, userAgent));
    }

    public bool IsActive(DateTimeOffset utcNow)
        => RevokedAt is null && ExpiresAt > utcNow;

    public bool IsExpired(DateTimeOffset utcNow)
        => ExpiresAt <= utcNow;

    public Result Revoke(DateTimeOffset utcNow, Guid replacedByTokenId)
    {
        if (RevokedAt.HasValue)
            return Result.Failure(IdentityDomainErrors.RefreshToken.AlreadyRevoked);

        RevokedAt = utcNow;
        ReplacedByTokenId = replacedByTokenId;
        Touch();
        return Result.Success();
    }
}