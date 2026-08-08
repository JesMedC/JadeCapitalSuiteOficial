using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Validation;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JadeCapital.Identity.Application.Features.Auth.Register;

/// <summary>
/// Registra un nuevo usuario:
/// 1) Valida reglas de aplicacion (password, formato).
/// 2) Verifica unicidad de email (Infrastructure).
/// 3) Hashea contrasena.
/// 4) Crea agregado User + emite tokens.
/// 5) Persiste via UnitOfWork.
/// </summary>
public sealed class RegisterUserHandler : IRequestHandler<RegisterUserCommand, Result<RegisterUserResult>>
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<RegisterUserHandler> _logger;

    public RegisterUserHandler(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher hasher,
        ITokenService tokens,
        IUnitOfWork uow,
        IClock clock,
        IOptions<JwtOptions> jwtOptions,
        ILogger<RegisterUserHandler> logger)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _hasher = hasher;
        _tokens = tokens;
        _uow = uow;
        _clock = clock;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    public async Task<Result<RegisterUserResult>> Handle(RegisterUserCommand req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();

        var existing = await _users.FindByEmailAsync(email, ct);
        if (existing is not null)
        {
            _logger.LogWarning("Registration attempt with existing email.");
            return Result.Failure<RegisterUserResult>(Error.Conflict("auth.email_already_registered", "Email is already registered."));
        }

        var hash = _hasher.Hash(req.Password);
        var userId = Guid.NewGuid();

        var userResult = User.Register(userId, email, req.DisplayName, hash, UserRole.Trader);
        DomainGuard.EnsureSuccess(userResult);
        var user = userResult.Value;

        await _users.AddAsync(user, ct);

        var access = _tokens.CreateAccessToken(user.Id, user.Email, user.Role.ToString());
        var refreshOpaque = _tokens.CreateOpaqueRefreshToken();
        var refreshHash = _tokens.HashToken(refreshOpaque);
        var refreshExpiry = _clock.UtcNow.AddDays(_jwtOptions.RefreshTokenTtlDays);

        var refreshEntityResult = RefreshToken.Issue(
            Guid.NewGuid(), user.Id, refreshHash, _clock.UtcNow, refreshExpiry);
        DomainGuard.EnsureSuccess(refreshEntityResult);

        await _refreshTokens.AddAsync(refreshEntityResult.Value, ct);

        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation("User {UserId} registered.", user.Id);

        return Result.Success(new RegisterUserResult(
            user.Id, user.Email, user.DisplayName,
            access.Token, refreshOpaque,
            access.ExpiresAt, refreshExpiry));
    }
}