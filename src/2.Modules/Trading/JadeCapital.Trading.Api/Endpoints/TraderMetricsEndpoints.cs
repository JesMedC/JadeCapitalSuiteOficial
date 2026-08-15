using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Features.Metrics.GetTradingMetrics;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Metrics;
using MediatR;

namespace JadeCapital.Trading.Api.Endpoints;

/// <summary>
/// GET /api/trades/metrics?period=7d|30d|90d|all
///
/// Calcula KPIs y curvas del usuario autenticado sobre la ventana solicitada.
/// Toda la fuente de verdad vive en trading.trades — el cliente nunca
/// re-computa (reemplaza los mocks initialBalance/dailyYield del analytics.page.ts).
///
/// RequireAuthorization + api-general rate limit (mismo patron que
/// GetTrades / GetDashboardSummary).
/// </summary>
public static class TraderMetricsEndpoints
{
    public static IEndpointRouteBuilder MapTraderMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/trades").WithTags("Trading").RequireAuthorization();

        group.MapGet("/metrics", GetTradingMetricsAsync)
            .WithName("GetTradingMetrics")
            .WithSummary("Metricas server-side (expectancy, profit factor, SQN, drawdown, equity curve y stats por simbolo).")
            .Produces<MetricsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem()
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

    private static IResult ProblemFromResult(Error error)
    {
        var status = error.Code switch
        {
            var c when c.StartsWith("validation", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status400BadRequest,
            var c when c.StartsWith("notfound", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status404NotFound,
            var c when c.StartsWith("conflict", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status409Conflict,
            var c when c.StartsWith("unauthorized", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status401Unauthorized,
            var c when c.StartsWith("forbidden", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return Results.Problem(
            type: $"https://jadecapital/errors/{error.Code}",
            title: "Request failed",
            detail: error.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    // ===== Handlers =====

    private static async Task<IResult> GetTradingMetricsAsync(
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        string? period = null,
        CancellationToken ct = default)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var metricsPeriod = MetricsPeriodExtensions.FromKey(period);
        var query = new GetTradingMetricsQuery(userId, metricsPeriod);

        var result = await sender.Send(query, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }
}
