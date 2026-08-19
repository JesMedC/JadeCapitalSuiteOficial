using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Identity.Application.Features.Auth.Consent;

// ============================================================================
//  ConsentHandler — Wave 11 slice 11.4.
//
//  Persists the ePrivacy cookie-consent decision (GDPR Art. 6(1)(a)).
//
//  <para>
//  <b>Algorithm</b>:
//  <list type="number">
//  <item>Load the user by JWT-derived id. 404 if the row is gone.</item>
//  <item>Normalise the choice to the canonical tier set via
//  <see cref="CookieConsentChoices.TryNormalize"/>; 422 if unrecognised.</item>
//  <item>Apply <see cref="User.RecordCookieConsent"/> on the aggregate.
//  The method preserves the original timestamp when the choice matches
//  (idempotency), and bumps the timestamp + writes the new choice on a
//  change.</item>
//  <item>Persist via <see cref="IUnitOfWork"/>.</item>
//  </list>
//  </para>
//
//  <para>
//  <b>Auth</b>: the endpoint requires a JWT (Authorization); the handler
//  trusts the JWT-resolved <c>UserId</c> and does NOT cross-check against
//  any tenant scope (consent is per-user, not per-tenant). The
//  cross-tenant attack vector closes at the endpoint boundary — there is
//  no way for an actor to specify a different userId via wire input.
//  </para>
// ============================================================================

public sealed class ConsentHandler : IRequestHandler<ConsentCommand, Result<ConsentResult>>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<ConsentHandler> _logger;

    public ConsentHandler(
        IUserRepository users,
        IUnitOfWork uow,
        IClock clock,
        ILogger<ConsentHandler> logger)
    {
        _users = users;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<ConsentResult>> Handle(ConsentCommand req, CancellationToken ct)
    {
        if (!CookieConsentChoices.TryNormalize(req.Choice, out var normalized))
        {
            return Result.Failure<ConsentResult>(
                JadeCapital.Identity.Application._Common.IdentityApplicationErrors.Auth.CookieConsentChoiceInvalid);
        }

        var user = await _users.FindByIdAsync(req.UserId, ct);
        if (user is null)
        {
            _logger.LogWarning("Consent write attempted for unknown user {UserId}.", req.UserId);
            return Result.Failure<ConsentResult>(
                JadeCapital.Shared.Kernel.Results.Error.NotFound("identity.user_not_found", "User not found."));
        }

        var now = _clock.UtcNow;
        var recordResult = user.RecordCookieConsent(now, normalized);
        if (recordResult.IsFailure)
        {
            _logger.LogWarning(
                "User {UserId} cookie-consent write rejected: {Error}",
                req.UserId, recordResult.Error.Code);
            return Result.Failure<ConsentResult>(recordResult.Error);
        }

        await _users.UpdateAsync(user, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
        {
            return Result.Failure<ConsentResult>(saved.Error);
        }

        // RecordCookieConsent is idempotent for matching choice — the
        // timestamp we read back from the aggregate is the SOURCE OF
        // TRUTH (the original one for an idempotent re-call, the new
        // one for a tier change). This avoids a clock-skew race where
        // the FE expects "now" but the BE preserves the original.
        var acceptedAt = user.CookieConsentAcceptedAt ?? now;
        _logger.LogInformation(
            "User {UserId} cookie-consent set to {Choice} at {At}.",
            req.UserId, user.CookieConsentChoice, acceptedAt);

        return Result.Success(new ConsentResult(user.CookieConsentChoice!, acceptedAt));
    }
}
