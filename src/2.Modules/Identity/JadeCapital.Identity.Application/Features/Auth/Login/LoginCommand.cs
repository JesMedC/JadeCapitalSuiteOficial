using FluentValidation;
using JadeCapital.Identity.Application._Common;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Application.Features.Auth.Login;

public sealed record LoginCommand(
    string Email,
    string Password,
    string? IpAddress,
    string? UserAgent) : MediatR.IRequest<Result<LoginResult>>;

public sealed record LoginResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid UserId,
    string Email,
    string DisplayName,
    string Role);

public sealed class LoginValidator : FluentValidation.AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}