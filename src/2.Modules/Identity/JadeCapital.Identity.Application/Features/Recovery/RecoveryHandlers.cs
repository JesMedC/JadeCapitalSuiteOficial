using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application._Common;
using JadeCapital.Identity.Application.Behaviors;
using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Shared.Infrastructure.Email;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JadeCapital.Identity.Application.Features.Recovery;

public sealed record ForgotPasswordCommand(string Email) : IRequest<Result>;
public sealed record LoginWithTemporaryCommand(string Email, string TemporaryPassword, string? IpAddress, string? UserAgent) : IRequest<Result<LoginResult>>;
public sealed record LoginResult(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string? RefreshToken, DateTimeOffset? RefreshTokenExpiresAt, Guid UserId, string Email, string DisplayName, string Role, bool RequiresPasswordChange, string? GrantJti);
public sealed record ChangePasswordWithGrantCommand(Guid UserId, string GrantJti, int ExpectedSessionVersion, string NewPassword, string? IpAddress, string? UserAgent) : IRequest<Result<ChangePasswordResult>>;
public sealed record ChangePasswordVoluntaryCommand(Guid UserId, int ExpectedSessionVersion, string CurrentPassword, string NewPassword, string? IpAddress, string? UserAgent) : IRequest<Result<ChangePasswordResult>>;
public sealed record ChangePasswordResult(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt, Guid UserId, bool RequiresPasswordChange);

public sealed class ForgotPasswordHandler : IRequestHandler<ForgotPasswordCommand, Result>
{
    private readonly IUserRepository _users; private readonly ITemporaryCredentialRepository _temps;
    private readonly IPasswordHasher _hasher; private readonly IClock _clock; private readonly IEmailSender _email;
    private readonly IUnitOfWork _uow; private readonly ILogger<ForgotPasswordHandler> _logger;
    public ForgotPasswordHandler(IUserRepository users, ITemporaryCredentialRepository temps, IPasswordHasher hasher, IClock clock, IEmailSender email, IUnitOfWork uow, ILogger<ForgotPasswordHandler> logger)
    { _users = users; _temps = temps; _hasher = hasher; _clock = clock; _email = email; _uow = uow; _logger = logger; }
    public async Task<Result> Handle(ForgotPasswordCommand req, CancellationToken ct)
    {
        var user = await _users.FindByEmailAsync(req.Email.Trim().ToLowerInvariant(), ct);
        if (user is null) { _logger.LogInformation("Password recovery requested for unknown email."); return Result.Success(); }
        var now = _clock.UtcNow;
        var plaintext = CrockfordCredential.Generate();
        var hash = _hasher.Hash(plaintext);
        // Atomic supersession: a concurrent reset for the same user must leave
        // exactly one Activated row. The unique partial index
        // ux_temporary_credentials_user_active is the persistence-level guarantee;
        // this is the application-level guarantee the handler relies on.
        await _temps.SupersedeActiveAsync(user.Id, ct);
        var gen = (await _temps.LatestGenerationAsync(user.Id, ct)) + 1;
        var reserved = await _temps.ReserveAsync(Guid.NewGuid(), user.Id, gen, hash, ct);
        if (reserved.IsFailure) { _logger.LogWarning("Failed to reserve temp credential for {UserId}.", user.Id); return Result.Failure(reserved.Error); }
        try
        {
            await _email.SendRecoveryEmailAsync(new RecoveryEmailMessage(req.Email, user.DisplayName, plaintext, now.AddHours(TemporaryCredential.LifetimeHours)), ct);
        }
        catch (Exception ex)
        {
            // SMTP failure must NOT propagate — the spec requires uniform 200 generic.
            // The reservation stays Pending; no ActivateAsync call is made, so no
            // Activated row is ever persisted. The pending row will be superseded
            // by the next legitimate request (or expire).
            _logger.LogWarning("Recovery email send failed for {UserId}: {Error}", user.Id, ex.GetType().Name);
            return Result.Success();
        }
        var activated = await _temps.ActivateAsync(reserved.Value.Id, gen, ct);
        if (activated.IsFailure) { _logger.LogWarning("Temp credential superseded before activation for {UserId}.", user.Id); return Result.Failure(activated.Error); }
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
public sealed class LoginWithTemporaryHandler : IRequestHandler<LoginWithTemporaryCommand, Result<LoginResult>>
{
    private readonly IUserRepository _users; private readonly ITemporaryCredentialRepository _temps;
    private readonly IPasswordHasher _hasher; private readonly ITokenService _tokens; private readonly IUnitOfWork _uow;
    private readonly IClock _clock; private readonly ILogger<LoginWithTemporaryHandler> _logger;
    public LoginWithTemporaryHandler(IUserRepository users, ITemporaryCredentialRepository temps, IPasswordHasher hasher, ITokenService tokens, IUnitOfWork uow, IClock clock, ILogger<LoginWithTemporaryHandler> logger)
    { _users = users; _temps = temps; _hasher = hasher; _tokens = tokens; _uow = uow; _clock = clock; _logger = logger; }
    public async Task<Result<LoginResult>> Handle(LoginWithTemporaryCommand req, CancellationToken ct)
    {
        var user = await _users.FindByEmailAsync(req.Email.Trim().ToLowerInvariant(), ct);
        var credential = user is null ? null : await _temps.FindLatestActivatedAsync(user.Id, ct);
        var hash = credential?.Hash ?? "100000.dummy.dummy==";
        var passwordOk = _hasher.Verify(req.TemporaryPassword, hash);
        if (user is null || credential is null) { _logger.LogWarning("Temp login for unknown email or no active credential."); return Result.Failure<LoginResult>(IdentityApplicationErrors.Auth.RecoveryInvalid); }
        if (!passwordOk || !credential.IsUsable(_clock.UtcNow))
        {
            var failed = user.RecordFailedLogin();
            if (failed.IsSuccess) { await _uow.SaveChangesAsync(ct); if (user.IsLockedOut(_clock.UtcNow)) return Result.Failure<LoginResult>(IdentityApplicationErrors.Auth.AccountLockedOut); }
            _logger.LogWarning("Failed temp login for {UserId}.", user.Id);
            return Result.Failure<LoginResult>(IdentityApplicationErrors.Auth.RecoveryInvalid);
        }
        var success = user.RecordSuccessfulLogin();
        if (success.IsFailure) return Result.Failure<LoginResult>(success.Error.Code.StartsWith("forbidden", StringComparison.Ordinal) ? success.Error : IdentityApplicationErrors.Auth.AccessDenied);
        var grantJti = Guid.NewGuid().ToString("N");
        var consumed = await _temps.ConsumeAsync(credential.Id, grantJti, _clock.UtcNow, ct);
        if (consumed.IsFailure) { _logger.LogWarning("Concurrent consumption of temp credential for {UserId}.", user.Id); return Result.Failure<LoginResult>(IdentityApplicationErrors.Auth.RecoveryInvalid); }
        var claims = new[] { "scope=password_change", $"grant_jti={grantJti}", $"generation={credential.Generation}", $"session_version={user.SessionVersion}" };
        var access = _tokens.CreateAccessToken(user.Id, user.Email, user.Role.ToString(), claims);
        await _uow.SaveChangesAsync(ct);
        return Result.Success(new LoginResult(access.Token, access.ExpiresAt, null, null, user.Id, user.Email, user.DisplayName, user.Role.ToString(), true, grantJti));
    }
}
public sealed class ChangePasswordWithGrantHandler : IRequestHandler<ChangePasswordWithGrantCommand, Result<ChangePasswordResult>>
{
    private readonly IUserRepository _users; private readonly ITemporaryCredentialRepository _temps;
    private readonly IRefreshTokenRevoker _revoker; private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHistoryRepository _history; private readonly IDistributedLock _locks;
    private readonly IPasswordChangeReuseChecker _reuse; private readonly IPasswordHasher _hasher; private readonly ITokenService _tokens;
    private readonly IUnitOfWork _uow; private readonly IClock _clock; private readonly JwtOptions _jwtOptions;
    private readonly ILogger<ChangePasswordWithGrantHandler> _logger;
    public ChangePasswordWithGrantHandler(IUserRepository users, ITemporaryCredentialRepository temps, IRefreshTokenRevoker revoker, IRefreshTokenRepository refreshTokens, IPasswordHistoryRepository history, IDistributedLock locks, IPasswordChangeReuseChecker reuse, IPasswordHasher hasher, ITokenService tokens, IUnitOfWork uow, IClock clock, IOptions<JwtOptions> jwtOptions, ILogger<ChangePasswordWithGrantHandler> logger)
    { _users = users; _temps = temps; _revoker = revoker; _refreshTokens = refreshTokens; _history = history; _locks = locks; _reuse = reuse; _hasher = hasher; _tokens = tokens; _uow = uow; _clock = clock; _jwtOptions = jwtOptions.Value; _logger = logger; }
    public async Task<Result<ChangePasswordResult>> Handle(ChangePasswordWithGrantCommand req, CancellationToken ct)
    {
        if (!PasswordPolicy.MeetsComplexity(req.NewPassword) || req.NewPassword.Length < PasswordPolicy.MinLength || req.NewPassword.Length > PasswordPolicy.MaxLength)
            return Result.Failure<ChangePasswordResult>(IdentityApplicationErrors.Auth.PasswordRequiresComplexity);
        var credential = await _temps.FindByGrantJtiAsync(req.GrantJti, ct);
        if (credential is null || credential.ExpiresAt <= _clock.UtcNow)
            return Result.Failure<ChangePasswordResult>(IdentityApplicationErrors.Auth.RecoveryInvalid);
        await using var handle = await _locks.AcquireAsync($"user:{req.UserId}", ct);
        try
        {
            var user = await _users.FindByIdAsync(req.UserId, ct);
            if (user is null || user.SessionVersion != req.ExpectedSessionVersion)
                return Result.Failure<ChangePasswordResult>(user is null ? IdentityApplicationErrors.Auth.RecoveryInvalid : IdentityApplicationErrors.Auth.ConcurrentUpdate);
            if (_reuse.IsReused(user, req.NewPassword))
                return Result.Failure<ChangePasswordResult>(Error.Conflict("auth.password_reused", "New password must differ from current and previous five."));
            var confirm = await _temps.ConfirmConsumedAsync(credential.Id, _clock.UtcNow, ct);
            if (confirm.IsFailure) return Result.Failure<ChangePasswordResult>(IdentityApplicationErrors.Auth.RecoveryInvalid);
            var utcNow = _clock.UtcNow; var newHash = _hasher.Hash(req.NewPassword); var displaced = user.PasswordHash;
            var change = user.ChangePasswordPreservingHistory(newHash);
            if (change.IsFailure) return Result.Failure<ChangePasswordResult>(change.Error);
            var append = await _history.AppendAsync(user.Id, displaced, utcNow, ct);
            if (append.IsFailure) return Result.Failure<ChangePasswordResult>(append.Error);
            await _revoker.RevokeAllAsync(user.Id, ct);
            var access = _tokens.CreateAccessToken(user.Id, user.Email, user.Role.ToString());
            var refreshOpaque = _tokens.CreateOpaqueRefreshToken();
            var refreshHash = _tokens.HashToken(refreshOpaque);
            var refreshExpiry = utcNow.AddDays(_jwtOptions.RefreshTokenTtlDays);
            var rtResult = RefreshToken.Issue(Guid.NewGuid(), user.Id, refreshHash, utcNow, refreshExpiry, req.IpAddress, req.UserAgent);
            if (rtResult.IsFailure) return Result.Failure<ChangePasswordResult>(rtResult.Error);
            await _refreshTokens.AddAsync(rtResult.Value, ct);
            var saved = await _uow.SaveChangesAsync(ct);
            if (saved.IsFailure) return Result.Failure<ChangePasswordResult>(saved.Error);
            _logger.LogInformation("Forced password change completed for {UserId}.", user.Id);
            return Result.Success(new ChangePasswordResult(access.Token, access.ExpiresAt, refreshOpaque, refreshExpiry, user.Id, false));
        }
        finally { await handle.DisposeAsync(); }
    }
}
public sealed class ChangePasswordVoluntaryHandler : IRequestHandler<ChangePasswordVoluntaryCommand, Result<ChangePasswordResult>>
{
    private readonly IUserRepository _users; private readonly IRefreshTokenRevoker _revoker; private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IDistributedLock _locks; private readonly IPasswordChangeReuseChecker _reuse; private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens; private readonly IUnitOfWork _uow; private readonly IClock _clock; private readonly JwtOptions _jwtOptions;
    private readonly ILogger<ChangePasswordVoluntaryHandler> _logger;
    public ChangePasswordVoluntaryHandler(IUserRepository users, IRefreshTokenRevoker revoker, IRefreshTokenRepository refreshTokens, IDistributedLock locks, IPasswordChangeReuseChecker reuse, IPasswordHasher hasher, ITokenService tokens, IUnitOfWork uow, IClock clock, IOptions<JwtOptions> jwtOptions, ILogger<ChangePasswordVoluntaryHandler> logger)
    { _users = users; _revoker = revoker; _refreshTokens = refreshTokens; _locks = locks; _reuse = reuse; _hasher = hasher; _tokens = tokens; _uow = uow; _clock = clock; _jwtOptions = jwtOptions.Value; _logger = logger; }
    public async Task<Result<ChangePasswordResult>> Handle(ChangePasswordVoluntaryCommand req, CancellationToken ct)
    {
        if (!PasswordPolicy.MeetsComplexity(req.NewPassword) || req.NewPassword.Length < PasswordPolicy.MinLength || req.NewPassword.Length > PasswordPolicy.MaxLength)
            return Result.Failure<ChangePasswordResult>(IdentityApplicationErrors.Auth.PasswordRequiresComplexity);
        await using var handle = await _locks.AcquireAsync($"user:{req.UserId}", ct);
        try
        {
            var user = await _users.FindByIdAsync(req.UserId, ct);
            if (user is null) return Result.Failure<ChangePasswordResult>(Error.NotFound("user.not_found", "User was not found."));
            if (user.SessionVersion != req.ExpectedSessionVersion) { _logger.LogWarning("Concurrent voluntary password change for {UserId}.", user.Id); return Result.Failure<ChangePasswordResult>(IdentityApplicationErrors.Auth.ConcurrentUpdate); }
            if (!_hasher.Verify(req.CurrentPassword, user.PasswordHash))
                return Result.Failure<ChangePasswordResult>(Error.Unauthorized("auth.invalid_current_password", "Current password is incorrect."));
            if (_reuse.IsReused(user, req.NewPassword))
                return Result.Failure<ChangePasswordResult>(Error.Conflict("auth.password_reused", "New password must differ from current and previous five."));
            var utcNow = _clock.UtcNow; var newHash = _hasher.Hash(req.NewPassword);
            var change = user.ChangePasswordPreservingHistory(newHash);
            if (change.IsFailure) return Result.Failure<ChangePasswordResult>(change.Error);
            await _revoker.RevokeAllAsync(user.Id, ct);
            var access = _tokens.CreateAccessToken(user.Id, user.Email, user.Role.ToString());
            var refreshOpaque = _tokens.CreateOpaqueRefreshToken();
            var refreshHash = _tokens.HashToken(refreshOpaque);
            var refreshExpiry = utcNow.AddDays(_jwtOptions.RefreshTokenTtlDays);
            var rtResult = RefreshToken.Issue(Guid.NewGuid(), user.Id, refreshHash, utcNow, refreshExpiry, req.IpAddress, req.UserAgent);
            if (rtResult.IsFailure) return Result.Failure<ChangePasswordResult>(rtResult.Error);
            await _refreshTokens.AddAsync(rtResult.Value, ct);
            var saved = await _uow.SaveChangesAsync(ct);
            if (saved.IsFailure) return Result.Failure<ChangePasswordResult>(saved.Error);
            return Result.Success(new ChangePasswordResult(access.Token, access.ExpiresAt, refreshOpaque, refreshExpiry, user.Id, false));
        }
        finally { await handle.DisposeAsync(); }
    }
}
