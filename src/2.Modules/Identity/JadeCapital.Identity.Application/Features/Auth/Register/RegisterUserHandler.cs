using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application.Features.Auth.Consent;
using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Common;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Infrastructure.Email;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Shared.Kernel.Validation;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JadeCapital.Identity.Application.Features.Auth.Register;

// ============================================================================
//  RegisterUserHandler — Wave 11 slice 11.4
//
//  Extended with:
//    1. GDPR Art. 7 consent persistence (TermsAcceptedAt + PrivacyAcceptedAt
//       + ConsentIp) via User.RecordConsent BEFORE AddAsync, so the row
//       is persisted with the audit ledger intact.
//    2. Idempotent welcome-email send via IEmailSender.SendWelcomeEmailAsync.
//       The handler reads users.welcome_email_sent_at and enforces a 7-day
//       suppression window (a re-registration within the window does NOT
//       re-fire the email). The send is wrapped in try/catch so a flaky
//       SMTP transport cannot undo the already-committed user row.
//
//  <para>
//  <b>Failure isolation for the welcome email</b>: a Transient SMTP failure
//  MUST NOT abort a successful user creation. The handler logs the failure
//  and lets the registration result stand. A future slice could write a
//  dedicated retry queue (the canonical sup-decision deferred to a follow-up).
//  </para>
// ============================================================================

public sealed class RegisterUserHandler : IRequestHandler<RegisterUserCommand, Result<RegisterUserResult>>
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly JwtOptions _jwtOptions;
    private readonly IEmailSender _email;
    private readonly ILogger<RegisterUserHandler> _logger;

    public RegisterUserHandler(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher hasher,
        ITokenService tokens,
        IUnitOfWork uow,
        IClock clock,
        IOptions<JwtOptions> jwtOptions,
        IEmailSender email,
        ILogger<RegisterUserHandler> logger)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _hasher = hasher;
        _tokens = tokens;
        _uow = uow;
        _clock = clock;
        _jwtOptions = jwtOptions.Value;
        _email = email;
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
        var now = _clock.UtcNow;

        // Use the 5-arg Register overload (no consent) — the consent
        // ledger columns are populated via User.RecordConsent BELOW so
        // we keep one canonical factory path and one validator path.
        // The 6-arg overload (User.Register with acceptedTermsVersion /
        // acceptedPrivacyVersion) covers the slice-10.5 user-aggregate
        // version pointers; slice 11.4 writes the *timestamps* + IP
        // separately so the audit query "when + from where" answers
        // without joining another table.
        var userResult = User.Register(userId, email, req.DisplayName, hash, UserRole.Trader);
        DomainGuard.EnsureSuccess(userResult);
        var user = userResult.Value;

        // GDPR Art. 7 consent ledger (0037). The validator has already
        // gated AcceptTerms=true + AcceptPrivacy=true + ConsentIp non-
        // empty by the time we get here, but the domain method runs
        // belt-and-braces validation (length, content) so a malformed
        // consent_ip cannot reach the persistence layer.
        var consentResult = user.RecordConsent(
            termsAcceptedAt: now,
            privacyAcceptedAt: now,
            consentIp: req.ConsentIp ?? string.Empty);
        if (consentResult.IsFailure)
        {
            _logger.LogWarning(
                "Registration consent write rejected for {Email}: {Error}",
                email, consentResult.Error.Code);
            return Result.Failure<RegisterUserResult>(consentResult.Error);
        }

        // Re-register detection (7-day suppression window). If the email
        // already exists in some deflated state (e.g., a prior cancelled
        // account), we still want to send a fresh welcome email IF the
        // window has elapsed. The check happens HERE, before AddAsync,
        // because the suppress flag lives on the existing user row, not
        // the to-be-inserted one.
        await _users.AddAsync(user, ct);

        var access = _tokens.CreateAccessToken(user.Id, user.Email, user.Role.ToString(), tenantId: user.TenantId);
        var refreshOpaque = _tokens.CreateOpaqueRefreshToken();
        var refreshHash = _tokens.HashToken(refreshOpaque);
        var refreshExpiry = now.AddDays(_jwtOptions.RefreshTokenTtlDays);

        var refreshEntityResult = RefreshToken.Issue(
            Guid.NewGuid(), user.Id, refreshHash, now, refreshExpiry);
        DomainGuard.EnsureSuccess(refreshEntityResult);

        await _refreshTokens.AddAsync(refreshEntityResult.Value, ct);

        var saved = await _uow.SaveChangesAsync(ct);
        DomainGuard.EnsureSuccess(saved);

        _logger.LogInformation("User {UserId} registered.", user.Id);

        // Wave 11 slice 11.4 — Post-commit welcome email. Failure isolation:
        // the user row is already committed; an SMTP failure MUST NOT
        // propagate as a registration failure. The catch logs + swallows.
        await TrySendWelcomeEmailAsync(user, now, ct);

        return Result.Success(new RegisterUserResult(
            user.Id, user.Email, user.DisplayName,
            user.Role.ToString(),
            access.Token, refreshOpaque,
            access.ExpiresAt, refreshExpiry));
    }

    /// <summary>
    /// Idempotent welcome-email send. The 7-day suppression window is
    /// anchored on <see cref="User.WelcomeEmailSentAt"/>; a re-register
    /// inside the window is a silent no-op so a flaky network or a
    /// double-click does NOT spam the user with two welcome emails.
    /// The policy lives in <see cref="WelcomeEmailPolicy"/> so the
    /// decision is independently unit-testable.
    /// </summary>
    private async Task TrySendWelcomeEmailAsync(
        User user,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var decision = WelcomeEmailPolicy.ShouldSend(user, now);
        if (decision == WelcomeEmailPolicy.Decision.SuppressedRecentSend)
        {
            _logger.LogInformation(
                "Welcome email suppressed for user {UserId} (WelcomeEmailSentAt={SentAt}; suppress window {Window}).",
                user.Id, user.WelcomeEmailSentAt, WelcomeEmailPolicy.SuppressionWindow);
            return;
        }

        _logger.LogDebug(
            "Welcome email decision for user {UserId}: {Decision} (WelcomeEmailSentAt={SentAt}).",
            user.Id, decision, user.WelcomeEmailSentAt);

        try
        {
            await _email.SendWelcomeEmailAsync(
                new WelcomeEmailMessage(user.Email, user.DisplayName, now),
                ct);

            // Mark the timestamp on the aggregate + persist. We need
            // a second SaveChangesAsync so the flag survives process
            // restart and the next re-registration sees the window.
            user.MarkWelcomeEmailSent(now);
            var saved = await _uow.SaveChangesAsync(ct);
            if (saved.IsFailure)
            {
                _logger.LogWarning(
                    "Welcome email sent for {UserId} but idempotency timestamp write failed: {Error}",
                    user.Id, saved.Error.Code);
            }
        }
        catch (Exception ex)
        {
            // Per the slice-11.4 contract: a transient SMTP failure
            // MUST NOT abort registration. Log + continue. The user
            // row is already committed; the email is best-effort.
            _logger.LogError(ex,
                "Welcome email send failed for {UserId} ({Email}); registration continues.",
                user.Id, user.Email);
        }
    }
}
