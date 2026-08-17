using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Features.Ai.GetAiHealth;
using JadeCapital.Trading.Application.Features.AiRiskAdvisor.GetCachedRiskAdvice;
using JadeCapital.Trading.Application.Features.AiRiskAdvisor.GetPreTradeAdvice;
using JadeCapital.Trading.Contracts.AiRiskAdvisor;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JadeCapital.Trading.Api.Endpoints;

// ============================================================================
//  AiEndpoints — slice 5b.1 (GET /api/ai/health) + slice 5c.1 (risk-advice).
//
//  Endpoints:
//   - GET  /api/ai/health                                     — slice 5b.1
//   - POST /api/ai/risk-advice                                — slice 5c.1
//   - GET  /api/ai/risk-advice/{tradeId:guid}                 — slice 5c.1
//
//  Auth: RequireAuthorization + api-general rate limit (same as 5b.1).
//  The risk-advice endpoints are SLIGHTLY heavier than general (60/hour
//  per user) — the spec calls for "api-general" but in practice the FE
//  fires POST on each checklist dirt, so the limit is enforced per-IP
//  via the same policy. The trade-throttle is enforced by the FE
//  (debounced 800ms before submit).
//
//  The 5c.1 GET endpoint serves the CACHED advisory persisted at OpenTrade
//  time. If the trade was opened before Wave 5 (no advisory persisted),
//  the endpoint returns 404 with ai_risk.not_found.
// ============================================================================

public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai")
            .WithTags("AI")
            .RequireAuthorization()
            .RequireRateLimiting("api-general");

        // GET /api/ai/health (slice 5b.1)
        group.MapGet("/health", GetHealthAsync)
            .WithName("GetAiHealth")
            .WithSummary(
                "Returns the current AI provider's reachability. 200 with `{status:'ok', model:'...'}` " +
                "when Ollama (or the configured Wave 6 provider) answers /api/tags with 2xx; " +
                "503 with `{status:'down'}` otherwise. Used by the FE to render the " +
                "'AI: connected' / 'AI: offline' badge in the trader shell.")
            .Produces<AiHealthDto>(StatusCodes.Status200OK)
            .Produces<AiHealthDto>(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // POST /api/ai/risk-advice (slice 5c.1)
        group.MapPost("/risk-advice", PostRiskAdviceAsync)
            .WithName("PostRiskAdvice")
            .WithSummary(
                "Run a manual AI risk-advisor pass over the supplied trade parameters. " +
                "Returns the parsed advisory (action, reason) + the persisted advice id. " +
                "Returns 503 with ai.unavailable when Ollama is down.")
            .Produces<AIRiskAdviceDto>(StatusCodes.Status200OK)
            .Produces<AIRiskAdviceDto>(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // GET /api/ai/risk-advice/{tradeId:guid} (slice 5c.1)
        group.MapGet("/risk-advice/{tradeId:guid}", GetCachedRiskAdviceAsync)
            .WithName("GetCachedRiskAdvice")
            .WithSummary(
                "Returns the cached AI risk advisory attached to the given trade at OpenTrade " +
                "time. Returns 404 with ai_risk.not_found when the trade was opened before Wave 5c.1, " +
                "or when no advisory was attached (advisor unavailable / legacy path).")
            .Produces<AIRiskAdviceDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    // ===== Existing GET /api/ai/health =====

    private static async Task<IResult> GetHealthAsync(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetAiHealthQuery(), ct);
        if (result.IsFailure)
            return ResultToHttp(result.Error);

        return result.Value.Status == "ok"
            ? Results.Ok(result.Value)
            : Results.Json(result.Value, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    // ===== POST /api/ai/risk-advice =====

    private static async Task<IResult> PostRiskAdviceAsync(
        HttpContext http,
        [FromServices] ISender sender,
        [FromBody] AiRiskAdvisorRequestDto body,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        if (body is null)
        {
            return Results.Problem(
                type: "https://jadecapital/errors/validation.ai_risk_advice.body_required",
                title: "Invalid request",
                detail: "Request body is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "validation.ai_risk_advice.body_required" });
        }

        var req = new JadeCapital.Trading.Application.Ai.AIRiskAdviceRequest(
            UserId: userId,
            TradeId: null,
            TradeSymbol: body.Symbol,
            Direction: body.Direction,
            Volume: body.Volume,
            VolumeCurrency: body.VolumeCurrency,
            EntryPrice: body.EntryPrice,
            StopLoss: body.StopLoss,
            RiskRewardAtEntry: body.RiskRewardAtEntry,
            SetupQuality: body.SetupQuality);

        var result = await sender.Send(new GetPreTradeAdviceQuery(req), ct);

        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        // Provider unavailable → 503 with ai.unavailable code so the FE can
        // distinguish "AI down" from validation errors.
        var errCode = result.Error.Code ?? string.Empty;
        if (errCode.Contains("ai.unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(
                new { code = errCode, detail = result.Error.Message },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return ResultToHttp(result.Error);
    }

    // ===== GET /api/ai/risk-advice/{tradeId} =====

    private static async Task<IResult> GetCachedRiskAdviceAsync(
        HttpContext http,
        [FromServices] ISender sender,
        [FromRoute] Guid tradeId,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var result = await sender.Send(
            new GetCachedRiskAdviceQuery(userId, tradeId), ct);

        if (result.IsSuccess)
            return Results.Ok(result.Value);

        return ResultToHttp(result.Error);
    }

    // ===== Helpers =====

    private static Guid GetUserId(HttpContext http)
    {
        var claim = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (claim is null || !Guid.TryParse(claim, out var id))
            throw new UnauthorizedAccessException("Invalid user claim.");
        return id;
    }

    private static IResult ResultToHttp(Error error)
    {
        var code = error.Code ?? "error";
        return code switch
        {
            var c when c.StartsWith("notfound.", StringComparison.Ordinal) =>
                Results.NotFound(new { code, detail = error.Message }),
            var c when c.StartsWith("conflict.", StringComparison.Ordinal) =>
                Results.Conflict(new { code, detail = error.Message }),
            var c when c.StartsWith("validation.", StringComparison.Ordinal) =>
                Results.UnprocessableEntity(new { code, detail = error.Message }),
            _ => Results.BadRequest(new { code, detail = error.Message }),
        };
    }
}
