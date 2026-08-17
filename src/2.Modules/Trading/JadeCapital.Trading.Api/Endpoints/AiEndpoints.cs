using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Features.Ai.GetAiHealth;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JadeCapital.Trading.Api.Endpoints;

/// <summary>
/// AI endpoints (Wave 5, slice 5b.1):
/// <list type="bullet">
///   <item><c>GET /api/ai/health</c> — probes the configured
///         <c>IAIProvider</c> (today: Ollama; Wave 6: OpenAI / Claude).
///         Returns 200 <c>{ status: "ok", model: "..." }</c> on success or
///         503 <c>{ status: "down" }</c> on any failure.</item>
/// </list>
///
/// <para>
/// Slice 5b.1 ships only the health probe. <c>POST /api/ai/risk-advice</c>
/// and <c>GET /api/ai/risk-advice/{tradeId}</c> land in slice 5c.1 — the
/// endpoint class is designed to grow without changing its public surface.
/// </para>
///
/// <para>
/// Auth: <c>RequireAuthorization()</c> + the <c>api-general</c> rate limit
/// (100 req/min/IP — same precedent as the other Trading endpoints). The
/// FE polls this endpoint every 60s from <c>ollama-health.interval.ts</c>
/// (slice 5c.2), so a generous limit is appropriate.
/// </para>
/// </summary>
public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai")
            .WithTags("AI")
            .RequireAuthorization()
            .RequireRateLimiting("api-general");

        // GET /api/ai/health
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

        return app;
    }

    private static async Task<IResult> GetHealthAsync(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetAiHealthQuery(), ct);
        // The handler always returns Success (down is a valid state, not a
        // failure); we map status -> HTTP code here so the handler stays
        // transport-agnostic.
        if (result.IsFailure)
            return ResultToHttp(result.Error);

        return result.Value.Status == "ok"
            ? Results.Ok(result.Value)
            : Results.Json(result.Value, statusCode: StatusCodes.Status503ServiceUnavailable);
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
