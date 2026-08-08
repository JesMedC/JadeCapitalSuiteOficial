using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application._Common;
using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Shared.Kernel.Validation;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JadeCapital.Identity.Application.Features.Auth.Refresh;

/// <summary>
/// Refresh token rotativo con DETECCION DE REUSO.
///
/// Flujo normal:
///   1. Cliente presenta refresh T1.
///   2. Sistema valida que T1 no este revocado ni expirado.
///   3. Emite T2 nuevo, revoca T1 con link a T2.
///
/// Flujo de reuso (ataque o bug):
///   1. Cliente presenta T1 que ya esta revocado.
///   2. Sistema interpreta como compromiso: revoca TODOS los refresh activos del user.
///   3. Falla el refresh, forza al usuario a re-login.
///
/// Esto cumple OWASP ASVS V3 (Session Management).
/// </summary>
public sealed class RefreshTokenHandler : IRequestHandler<RefreshTokenCommand, Result<RefreshTokenResult>>
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refresh;
    private readonly ITokenService _tokens;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<RefreshTokenHandler> _logger;

    public RefreshTokenHandler(
        IUserRepository users,
        IRefreshTokenRepository refresh,
        ITokenService tokens,
        IUnitOfWork uow,
        IClock clock,
        IOptions<JwtOptions> jwtOptions,
        ILogger<RefreshTokenHandler> logger)
    {
        _users = users;
        _refresh = refresh;
        _tokens = tokens;
        _uow = uow;
        _clock = clock;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    public async Task<Result<RefreshTokenResult>> Handle(RefreshTokenCommand req, CancellationToken ct)
    {
        var presentedHash = _tokens.HashToken(req.RefreshToken);
        var presented = await _refresh.FindByHashAsync(presentedHash, ct);

        if (presented is null)
        {
            _logger.LogWarning("Refresh token not found.");
            return Result.Failure<RefreshTokenResult>(IdentityApplicationErrors.Auth.RefreshTokenInvalid);
        }

        // Si esta revocado, es reuso. Compromiso. Revocar todos los tokens del user.
        if (presented.RevokedAt is not null)
        {
            _logger.LogCritical("REUSE detected for user {UserId}. Revoking all sessions.", presented.UserId);
            await _refresh.RevokeAllForUserAsync(presented.UserId, ct);
            await _uow.SaveChangesAsync(ct);
            return Result.Failure<RefreshTokenResult>(
                Error.Unauthorized("auth.refresh_token_reuse_detected",
                    "Refresh token reuse detected. All sessions revoked for security."));
        }

        if (presented.ExpiresAt <= _clock.UtcNow)
        {
            return Result.Failure<RefreshTokenResult>(
                Error.Unauthorized("auth.refresh_token_expired", "Refresh token has expired."));
        }

        var user = await _users.FindByIdAsync(presented.UserId, ct);
        if (user is null || !user.CanAuthenticate())
            return Result.Failure<RefreshTokenResult>(IdentityApplicationErrors.Auth.AccessDenied);

        var newOpaque = _tokens.CreateOpaqueRefreshToken();
        var newHash = _tokens.HashToken(newOpaque);
        var newExpiry = _clock.UtcNow.AddDays(_jwtOptions.RefreshTokenTtlDays);

        var newRtResult = RefreshToken.Issue(
            Guid.NewGuid(), user.Id, newHash, _clock.UtcNow, newExpiry,
            req.IpAddress, req.UserAgent);
        DomainGuard.EnsureSuccess(newRtResult);

        var revokeResult = presented.Revoke(_clock.UtcNow, newRtResult.Value.Id);
        DomainGuard.EnsureSuccess(revokeResult);

        await _refresh.AddAsync(newRtResult.Value, ct);
        await _uow.SaveChangesAsync(ct);

        var access = _tokens.CreateAccessToken(user.Id, user.Email, user.Role.ToString());

        _logger.LogInformation("Refresh rotated for {UserId}.", user.Id);

        return Result.Success(new RefreshTokenResult(
            access.Token, access.ExpiresAt,
            newOpaque, newExpiry));
    }
}