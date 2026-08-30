using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Features.Coaching.GetAiCoachingPrompts;
using JadeCapital.Trading.Application.Features.Coaching.GetPrompts;
using JadeCapital.Trading.Application.Features.Coaching.GenerateCoachingPrompt;
using JadeCapital.Trading.Contracts.Coaching;
using MediatR;

namespace JadeCapital.Trading.Api.Endpoints;

// ============================================================================
//  CoachingEndpoints — slice 2d.1 (Trader Journal Core) + slice 5b.2 (AI).
//
//  Endpoints:
//   - GET /api/coaching/prompts?period=...            (Wave 3b — rule-based)
//   - GET /api/coaching/ai-prompts?period=...         (slice 5b.2 — AI prompts)
//   - POST /api/coaching/prompts/generate             (slice 5b.2 — manual trigger)
//
//  Follows the same minimal-API conventions as the rest of the Trading module:
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

        // GET /api/coaching/prompts?period=... (rule-based — Wave 3b).
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

        // GET /api/coaching/ai-prompts?period=... (slice 5b.2 — AI prompts).
        group.MapGet("/ai-prompts", GetAiPromptsAsync)
            .WithName("GetAiCoachingPrompts")
            .WithSummary(
                "Devuelve los prompts de coaching AI generados por el CoachingPromptService " +
                "BackgroundService para el trader autenticado en el periodo dado. " +
                "Sort: createdAt DESC. Empty state: prompts = [].")
            .Produces<AiCoachingPromptsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireRateLimiting("api-general");

        // POST /api/coaching/prompts/generate (slice 5b.2 — manual trigger).
        group.MapPost("/prompts/generate", GeneratePromptAsync)
            .WithName("PostGenerateCoachingPrompt")
            .WithSummary(
                "Trigger manual del handler GenerateCoachingPromptHandler para el usuario autenticado. " +
                "Idempotente: una segunda invocacion en el mismo UTC-day devuelve count=0. " +
                "Si el usuario tiene 0 trades cerradas en 7d, devuelve count=0 sin llamar al provider.")
            .Produces<GenerateCoachingPromptResponseDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
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

    private static async Task<IResult> GetAiPromptsAsync(
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

        var result = await sender.Send(new GetAiCoachingPromptsQuery(userId, parsed.Value), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> GeneratePromptAsync(
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct = default)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var result = await sender.Send(new GenerateCoachingPromptCommand(userId), ct);
        return result.IsSuccess
            ? Results.Accepted(uri: null, value: new GenerateCoachingPromptResponseDto(
                UserId: userId,
                Count: result.Value))
            : ProblemFromResult(result.Error);
    }

    /// <summary>
    /// Response payload for <c>POST /api/coaching/prompts/generate</c>. The
    /// <c>count</c> is the number of prompts CREATED in this invocation
    /// (0 on idempotency short-circuit or 0-trade user, 1 on happy path).
    /// </summary>
    public sealed record GenerateCoachingPromptResponseDto(Guid UserId, int Count);
}
