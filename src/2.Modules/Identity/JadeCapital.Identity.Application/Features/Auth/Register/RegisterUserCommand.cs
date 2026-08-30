using FluentValidation;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application.Behaviors;
using JadeCapital.Identity.Application._Common;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Application.Features.Auth.Register;

// ============================================================================
//  RegisterUserCommand — Wave 11 slice 11.4
//
//  Extended with GDPR Art. 7 consent capture. The ToS + Privacy Policy
//  acceptance booleans are required (the validator rejects
//  AcceptTerms=false / AcceptPrivacy=false with 422). The optional
//  `AcceptedTermsVersion` / `AcceptedPrivacyVersion` strings feed the
//  10.5 user aggregate (so an audit row records which version was
//  accepted). `ConsentIp` is the wire-recorded client IP for the DSAR
//  audit trail.
//
//  <para>
//  <b>Why the booleans + version strings are both carried</b>:
//    * The booleans enforce consent WAS given (validator) and fold into
//      a single "yes" / "no" answer for compliance attestation.
//    * The version strings persist the exact version the user clicked on
//      so a future version bump ("ToS v2.0 → v2.1") can ask existing
//      users to re-accept.
//    * Without the booleans, the FE could send `"acceptedTermsVersion": "v2.0"`
//      without any indication the user actually clicked; the booleans
//      close that gap.
//  </para>
//
//  <para>
//  <b>Backwards compat</b>: the wire contract for clients that omit the
//  consent fields results in 422 (validator rejects). A future migration
//  could add a `consent_required_for_login` feature flag and relax the
//  validator for legacy accounts, but slice 11.4 keeps the consent gate
//  unconditional — registration without consent is no longer a valid path.
//  </para>
// ============================================================================

public sealed record RegisterUserCommand(
    string Email,
    string DisplayName,
    string Password,
    bool AcceptTerms,
    bool AcceptPrivacy,
    string? ConsentIp,
    string? AcceptedTermsVersion = "v1.0",
    string? AcceptedPrivacyVersion = "v1.0") : MediatR.IRequest<Result<RegisterUserResult>>;

public sealed record RegisterUserResult(
    Guid UserId,
    string Email,
    string DisplayName,
    string Role,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed class RegisterUserValidator : FluentValidation.AbstractValidator<RegisterUserCommand>
{
    public RegisterUserValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email is not a valid address.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().MinimumLength(2).MaximumLength(80);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(PasswordPolicy.MinLength)
                .WithMessage(IdentityApplicationErrors.Auth.PasswordTooShort.Message)
            .MaximumLength(PasswordPolicy.MaxLength)
                .WithMessage(IdentityApplicationErrors.Auth.PasswordTooLong.Message)
            .Must(PasswordPolicy.MeetsComplexity)
                .WithMessage(IdentityApplicationErrors.Auth.PasswordRequiresComplexity.Message);

        // Wave 11 slice 11.4 — GDPR Art. 7 consent ledger. Both
        // toggles MUST be true at registration time. The wire shape
        // exposes them as nullable booleans to make the consent
        // capture explicit (a `null` here would silently default to
        // `false` and slip past the validator — we don't want that
        // hazard on a compliance gate).
        RuleFor(x => x.AcceptTerms)
            .Equal(true)
                .WithMessage("Terms of Service must be accepted to register.")
                .WithErrorCode(IdentityApplicationErrors.Auth.TermsNotAccepted.Code);

        RuleFor(x => x.AcceptPrivacy)
            .Equal(true)
                .WithMessage("Privacy Policy must be accepted to register.")
                .WithErrorCode(IdentityApplicationErrors.Auth.PrivacyNotAccepted.Code);

        RuleFor(x => x.ConsentIp)
            .NotEmpty()
                .WithMessage("Consent IP is required (GDPR Art. 7 audit trail).")
                .MaximumLength(45)
                .WithMessage("Consent IP exceeds the IPv6 canonical-form length (45).")
                .When(x => x.AcceptTerms || x.AcceptPrivacy);
    }
}
