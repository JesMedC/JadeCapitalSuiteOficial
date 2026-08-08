using FluentValidation;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Application.Behaviors;
using JadeCapital.Identity.Application._Common;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Application.Features.Auth.Register;

public sealed record RegisterUserCommand(
    string Email,
    string DisplayName,
    string Password) : MediatR.IRequest<Result<RegisterUserResult>>;

public sealed record RegisterUserResult(
    Guid UserId,
    string Email,
    string DisplayName,
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
    }
}