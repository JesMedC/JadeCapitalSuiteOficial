using System.Security.Claims;
using JadeCapital.Identity.Application.Features.Auth.Consent;
using JadeCapital.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JadeCapital.Identity.Api.Endpoints;

// ============================================================================
//  ConsentEndpoint — Wave 11 slice 11.4
//
//  `POST /api/auth/consent` — GDPR ePrivacy cookie consent capture.
//
//  <para>
//  Auth: `RequireAuthorization()` — the JWT-derived userId is the only
//  wire-supplied input. There is no admin / impersonation path; the
//  endpoint operates on the caller's own row.
//  </para>
//
//  <para>
//  Wire shape:
//  <code>
//    POST /api/auth/consent
//    Authorization: Bearer <jwt>
//    Body: { "choice": "all" | "essential" }
//  </code>
//  Response: 200 with `{ "choice": ..., "acceptedAt": ... }` per
//  <see cref="ConsentResult"/>.
//  </para>
//
//  <para>
//  Status mapping (via <see cref="ProblemFromResult"/>):
//  <list type="bullet">
//  <item>200 on success.</item>
//  <item>401 if the JWT is missing / unparseable.</item>
//  <item>404 if the user row vanished between token issuance and request.</item>
//  <item>422 if the choice is unrecognised (not 'all' or 'essential').</item>
//  </list>
//  </para>
// ============================================================================

public static class ConsentEndpoint
{
    public static IEndpointRouteBuilder MapConsentEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/consent", ConsentAsync)
            .WithName("RecordCookieConsent")
            .WithSummary("GDPR ePrivacy Directive: record the cookie-banner decision.")
            .Produces<ConsentResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> ConsentAsync(
        [FromBody] ConsentRequest req,
        ClaimsPrincipal user,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ConsentCommand(userId, req.Choice ?? string.Empty), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
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

    /// <summary>Wire shape for <c>POST /api/auth/consent</c>. The
    /// <see cref="ConsentHandler"/> normalises + validates the value
    /// against the canonical tier set.</summary>
    public sealed record ConsentRequest(string? Choice);
}
