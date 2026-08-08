using FluentValidation;
using JadeCapital.Identity.Application._Common;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Application.Features.Auth.Refresh;

public sealed record RefreshTokenCommand(
    string RefreshToken,
    string? IpAddress,
    string? UserAgent) : MediatR.IRequest<Result<RefreshTokenResult>>;

public sealed record RefreshTokenResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed class RefreshTokenValidator : FluentValidation.AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MinimumLength(20).MaximumLength(512);
    }
}