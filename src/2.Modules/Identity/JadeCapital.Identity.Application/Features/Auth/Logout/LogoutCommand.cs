using FluentValidation;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Identity.Application.Features.Auth.Logout;

public sealed record LogoutCommand(
    Guid UserId,
    string RefreshToken) : MediatR.IRequest<Result>;

// Sin validator: el command puede llegar con refresh token vacio (logout total).
// El handler decide que hacer segun el caso.