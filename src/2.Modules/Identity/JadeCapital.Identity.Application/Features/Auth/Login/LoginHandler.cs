using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application._Common;
using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Validation;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JadeCapital.Identity.Application.Features.Auth.Login;

/// <summary>
/// Login con timing-safe password verification y manejo de lockout.
/// Genera nuevo par de tokens al exito.
/// </summary>
public sealed class LoginHandler : IRequestHandler<LoginCommand, Result<LoginResult>>
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refresh;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<LoginHandler> _logger;

    public LoginHandler(
        IUserRepository users,
        IRefreshTokenRepository refresh,
        IPasswordHasher hasher,
        ITokenService tokens,
        IUnitOfWork uow,
        IClock clock,
        IOptions<JwtOptions> jwtOptions,
        ILogger<LoginHandler> logger)
    {
        _users = users;
        _refresh = refresh;
        _hasher = hasher;
        _tokens = tokens;
        _uow = uow;
        _clock = clock;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    public async Task<Result<LoginResult>> Handle(LoginCommand req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _users.FindByEmailAsync(email, ct);

        // Timing-safe: si user es null, hashea un dummy para igualar costo.
        var hashToCheck = user?.PasswordHash ?? "100000.dummy.dummy==";
        var passwordOk = _hasher.Verify(req.Password, hashToCheck);

        if (user is null)
        {
            _logger.LogWarning("Login attempt with non-existent email.");
            return Result.Failure<LoginResult>(Error.Unauthorized("auth.invalid_credentials", "Invalid email or password."));
        }

        if (!passwordOk)
        {
            var failed = user.RecordFailedLogin();
            // failed puede fallar si el user no estaba activo; en ese caso no incrementamos.
            if (failed.IsSuccess)
            {
                await _uow.SaveChangesAsync(ct);
                if (user.IsLockedOut(_clock.UtcNow))
                    return Result.Failure<LoginResult>(IdentityApplicationErrors.Auth.AccountLockedOut);
            }
            _logger.LogWarning("Failed login for {UserId}.", user.Id);
            return Result.Failure<LoginResult>(Error.Unauthorized("auth.invalid_credentials", "Invalid email or password."));
        }

        var success = user.RecordSuccessfulLogin();
        if (success.IsFailure)
        {
            // Cuenta bloqueada, suspendida, cancelada, etc. Mapear al codigo correcto.
            return Result.Failure<LoginResult>(
                success.Error.Code.StartsWith("forbidden", StringComparison.Ordinal)
                    ? success.Error
                    : IdentityApplicationErrors.Auth.AccessDenied);
        }

        var access = _tokens.CreateAccessToken(user.Id, user.Email, user.Role.ToString());
        var refreshOpaque = _tokens.CreateOpaqueRefreshToken();
        var refreshHash = _tokens.HashToken(refreshOpaque);
        var refreshExpiry = _clock.UtcNow.AddDays(_jwtOptions.RefreshTokenTtlDays);

        var rtResult = RefreshToken.Issue(
            Guid.NewGuid(), user.Id, refreshHash, _clock.UtcNow, refreshExpiry,
            req.IpAddress, req.UserAgent);
        DomainGuard.EnsureSuccess(rtResult);

        await _refresh.AddAsync(rtResult.Value, ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("User {UserId} logged in.", user.Id);

        return Result.Success(new LoginResult(
            access.Token, access.ExpiresAt,
            refreshOpaque, refreshExpiry,
            user.Id, user.Email, user.DisplayName,
            user.Role.ToString()));
    }
}