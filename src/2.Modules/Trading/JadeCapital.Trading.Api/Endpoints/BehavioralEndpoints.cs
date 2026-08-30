using JadeCapital.Trading.Application.Features.BehavioralAnalytics.GetAnalysis;
using JadeCapital.Trading.Contracts.Behavioral;
using MediatR;

namespace JadeCapital.Trading.Api.Endpoints;

// ============================================================================
//  BehavioralEndpoints — slice 2b.1 (Trader Journal Core).
//
//  GET /api/trades/behavioral?period=7d|30d|90d|all
//
//  Returns the user's behavioral analytics for the given window:
//  - detected events (revenge, overtrading, tilt, overconfidence)
//  - emotionality aggregation buckets (1-2 / 3 / 4-5)
//
//  Follows the same minimal-API conventions as JournalEndpoints:
//   - Each handler is a delegate receiving ISender (no business logic).
//   - RequireAuthorization + api-general rate limit.
//   - ProblemFromResult maps Result.Error to RFC 7807 ProblemDetails.
//   - The userId comes from the NameIdentifier claim (never from the body).
//
//  The period query parameter defaults to 30d (the spec default).
// ============================================================================

public static class BehavioralEndpoints
{
    public static IEndpointRouteBuilder MapBehavioralEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/trades/behavioral")
            .WithTags("Trading")
            .RequireAuthorization();

        // GET /api/trades/behavioral?period=...
        // Period defaults to 30d (spec) when the query string omits it.
        group.MapGet("/", GetAnalysisAsync)
            .WithName("GetBehavioralAnalysis")
            .WithSummary(
                "Devuelve el analisis conductual del trader autenticado para el periodo dado (7d/30d/90d/all). " +
                "Incluye eventos detectados (revenge, overtrading, tilt, overconfidence) y agregados de PnL por bucket de emocionalidad.")
            .Produces<BehavioralAnalysisDto>(StatusCodes.Status200OK)
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

    /// <summary>
    /// Parses the <c>period</c> query string into a
    /// <see cref="BehavioralPeriod"/>. Returns 400 with ProblemDetails if
    /// the value is unrecognized (the FE always sends a valid string, but
    /// direct API consumers might typo).
    /// </summary>
    private static BehavioralPeriod? TryParsePeriod(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return BehavioralPeriod.Days30;

        return raw.Trim().ToLowerInvariant() switch
        {
            "7d"    => BehavioralPeriod.Days7,
            "30d"   => BehavioralPeriod.Days30,
            "90d"   => BehavioralPeriod.Days90,
            "all"   => BehavioralPeriod.All,
            _       => null,
        };
    }

    // ===== Handlers =====

    private static async Task<IResult> GetAnalysisAsync(
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
                type: "https://jadecapital/errors/validation.behavioral.invalid_period",
                title: "Invalid period",
                detail: $"Period must be one of: 7d, 30d, 90d, all. Got: '{period ?? "<null>"}'.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "validation.behavioral.invalid_period" });
        }

        var result = await sender.Send(new GetBehavioralAnalyticsQuery(userId, parsed.Value), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static IResult ProblemFromResult(JadeCapital.Shared.Kernel.Results.Error error)
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
}
