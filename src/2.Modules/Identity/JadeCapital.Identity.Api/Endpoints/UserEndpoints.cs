using System.Security.Claims;
using JadeCapital.Identity.Application.Features.Auth.DeleteAccount;
using JadeCapital.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JadeCapital.Identity.Api.Endpoints;

// ============================================================================
//  UserEndpoints — Wave 11 slice 11.2b
//
//  Endpoints mounted under /api/users/me — the JWT-derived "current user"
//  surface. Slice 11.2b ships `DELETE /api/users/me/account`, the GDPR
//  Art. 17 right-to-be-forgotten entry point.
//
//  Auth: any authenticated user can call DELETE on their own /me/account.
//  The handler resolves the userId from the JWT's NameIdentifier claim
//  and dispatches the DeleteAccountCommand. There is no role-based
//  authorization at the endpoint — the command operates on the caller's
//  own user row, never on anyone else's.
// ============================================================================

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users");

        group.MapDelete("/me/account", DeleteMyAccountAsync)
            .WithName("DeleteMyAccount")
            .WithSummary("GDPR Art. 17: delete the authenticated user's account.")
            .Produces<DeleteAccountResult>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> DeleteMyAccountAsync(
        ClaimsPrincipal user,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new DeleteAccountCommand(userId), ct);
        return result.IsSuccess
            ? Results.Accepted(value: result.Value)
            : ProblemFromResult(result.Error);
    }

    private static IResult ProblemFromResult(Error error)
    {
        var status = error.Code switch
        {
            var c when c.StartsWith("unauthorized", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status401Unauthorized,
            var c when c.StartsWith("notfound", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status404NotFound,
            var c when c.StartsWith("conflict", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status409Conflict,
            var c when c.StartsWith("forbidden", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status403Forbidden,
            var c when c.StartsWith("validation", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return Results.Problem(
            type: $"https://jadecapital/errors/{error.Code}",
            title: "Request failed",
            detail: error.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}
