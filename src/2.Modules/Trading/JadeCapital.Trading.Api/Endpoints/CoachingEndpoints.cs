using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Features.Coaching.GetPrompts;
using JadeCapital.Trading.Contracts.Coaching;
using MediatR;

namespace JadeCapital.Trading.Api.Endpoints;

// ============================================================================
//  CoachingEndpoints — slice 2d.1 (Trader Journal Core).
//
//  GET /api/coaching/prompts?period=7d|30d|90d|all
//
//  Follows the same minimal-API conventions as the rest of the Trading
//  module:
//   - RequireAuthorization + api-general rate limit.
//   - UserId comes from the NameIdentifier claim, never from the body / query.
//   - MediatR ISender injected via [FromServices]; no business logic in
//     the delegate.
//   - ProblemFromResult maps Result.Error → RFC 7807 ProblemDetails.
//
//  The period defaults to 30d (spec). We accept lowercase values only.
// ============================================================================

public static class CoachingEndpoints
{
    public static IEndpointRouteBuilder MapCoachingPromptsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/coaching")
            .WithTags("Trading")
            .RequireAuthorization();

        // GET /api/coaching/prompts?period=...
        group.MapGet("/prompts", GetPromptsAsync)
            .WithName("GetCoachingPrompts")
            .WithSummary(
                "Devuelve los prompts de coaching para el trader autenticado en el periodo dado (7d/30d/90d/all). " +
                "Incluye hasta 5 reglas (revenge, overtrading, tilt, long break, premarket plan miss). " +
                "Ordenado por severity descendente y occurredAt descendente.")
            .Produces<CoachingPromptsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireRateLimiting("api-general");

        return app;
    }

    // ===== Helpers =====

    private static Guid GetUserId(HttpContext http)
    {
        var claim = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (claim is null || !Guid.TryParse(claim, out var id))
            throw new UnauthorizedAccessException("Invalid user claim.");
        return id;
    }

    private static CoachingPeriod? TryParsePeriod(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return CoachingPeriod.Days30;

        return raw.Trim().ToLowerInvariant() switch
        {
            "7d"  => CoachingPeriod.Days7,
            "30d" => CoachingPeriod.Days30,
            "90d" => CoachingPeriod.Days90,
            "all" => CoachingPeriod.All,
            _     => null,
        };
    }

    private static IResult ProblemFromResult(Error error)
    {
        var status = error.Code switch
        {
            var c when c.StartsWith("validation", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status400BadRequest,
            var c when c.StartsWith("notfound", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status404NotFound,
            var c when c.StartsWith("conflict", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status409Conflict,
            var c when c.StartsWith("unauthorized", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status401Unauthorized,
            var c when c.StartsWith("forbidden", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status422UnprocessableEntity,
        };

        return Results.Problem(
            type: $"https://jadecapital/errors/{error.Code}",
            title: "Request failed",
            detail: error.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    // ===== Handlers =====

    private static async Task<IResult> GetPromptsAsync(
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        string? period = null,
        CancellationToken ct = default)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var parsed = TryParsePeriod(period);
        if (parsed is null)
        {
            return Results.Problem(
                type: "https://jadecapital/errors/validation.coaching.invalid_period",
                title: "Invalid period",
                detail: $"Period must be one of: 7d, 30d, 90d, all. Got: '{period ?? "<null>"}'.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "validation.coaching.invalid_period" });
        }

        var result = await sender.Send(new GetCoachingPromptsQuery(userId, parsed.Value), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }
}
