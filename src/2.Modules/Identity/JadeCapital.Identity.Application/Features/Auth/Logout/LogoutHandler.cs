using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Shared.Kernel.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Identity.Application.Features.Auth.Logout;

/// <summary>
/// Logout: revoca el refresh token presentado Y todos los demas tokens activos del user
/// (opcional — para logout-all-devices). En v1, revoca solo el presentado.
/// </summary>
public sealed class LogoutHandler : IRequestHandler<LogoutCommand, Result>
{
    private readonly IRefreshTokenRepository _refresh;
    private readonly ITokenService _tokens;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<LogoutHandler> _logger;

    public LogoutHandler(
        IRefreshTokenRepository refresh,
        ITokenService tokens,
        IUnitOfWork uow,
        ILogger<LogoutHandler> logger)
    {
        _refresh = refresh;
        _tokens = tokens;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result> Handle(LogoutCommand req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
        {
            // Logout sin token: no-op silencioso (idempotente).
            return Result.Success();
        }

        var hash = _tokens.HashToken(req.RefreshToken);
        var token = await _refresh.FindByHashAsync(hash, ct);

        if (token is null || token.RevokedAt is not null || token.UserId != req.UserId)
        {
            // Token desconocido o ya revocado. Idempotente.
            return Result.Success();
        }

        token.Revoke(DateTimeOffset.UtcNow, Guid.Empty); // sin reemplazo
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("User {UserId} logged out.", req.UserId);
        return Result.Success();
    }
}