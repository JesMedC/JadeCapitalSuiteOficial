using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Features.Alerts.AcknowledgeAlert;
using JadeCapital.Trading.Application.Features.Alerts.GetAlertById;
using JadeCapital.Trading.Application.Features.Alerts.GetAlerts;
using JadeCapital.Trading.Contracts.Alerts;
using MediatR;

namespace JadeCapital.Trading.Api.Endpoints;

// ============================================================================
//  AlertEndpoints — slice 3b (Trader Strategies + Alerts + Planner).
//
//  URLs:
//   - GET    /api/alerts?activeOnly=      → AlertDto[]
//   - GET    /api/alerts/{id}             → AlertDto | 404
//   - PATCH  /api/alerts/{id}/ack         → AlertDto
//   - POST   /api/alerts/_internal/run-now → 200 (DEV-ONLY)
//
//  Conventions (same as JournalEndpoints / StrategyEndpoints):
//   - UserId viene SIEMPRE del NameIdentifier claim (nunca del body).
//   - MediatR ISender inyectado via [FromServices]; sin logica en endpoint.
//   - RequireAuthorization + api-general rate limit.
//   - ProblemFromResult mapea Result.Error → RFC 7807 ProblemDetails.
//
//  Cross-user isolation (per spec requirement "Cross-user isolation"):
//   - GET /{id} returns 404 (not 403) when the alert doesn't belong to
//     the calling user — no leak of existence.
//   - PATCH /{id}/ack returns 404 in the same scenario.
// ============================================================================

public static class AlertEndpoints
{
    public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/alerts").WithTags("Trading").RequireAuthorization();

        group.MapGet("/", ListAsync)
            .WithName("ListAlerts")
            .WithSummary("Lista las alertas del user. activeOnly=true filtra acked/expired (default false).")
            .Produces<AlertDto[]>(StatusCodes.Status200OK)
            .RequireRateLimiting("api-general");

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetAlertById")
            .WithSummary("Detalle de una alerta del user. 404 si no existe o pertenece a otro user.")
            .Produces<AlertDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting("api-general");

        group.MapPatch("/{id:guid}/ack", AcknowledgeAsync)
            .WithName("AcknowledgeAlert")
            .WithSummary("Marca la alerta como acknowledged. Idempotente. 404 si no existe.")
            .Produces<AlertDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
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

    private static async Task<IResult> ListAsync(
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        [Microsoft.AspNetCore.Mvc.FromQuery] bool? activeOnly,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var query = new GetAlertsQuery(userId, ActiveOnly: activeOnly ?? false);
        var result = await sender.Send(query, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var query = new GetAlertByIdQuery(userId, id);
        var result = await sender.Send(query, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> AcknowledgeAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new AcknowledgeAlertCommand(userId, id);
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }
}