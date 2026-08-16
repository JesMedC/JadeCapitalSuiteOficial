using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Features.Strategies.CreateStrategy;
using JadeCapital.Trading.Application.Features.Strategies.DeactivateStrategy;
using JadeCapital.Trading.Application.Features.Strategies.GetStrategyAnalytics;
using JadeCapital.Trading.Application.Features.Strategies.ListStrategies;
using JadeCapital.Trading.Application.Features.Strategies.SetTradeStrategy;
using JadeCapital.Trading.Application.Features.Strategies.UpdateStrategy;
using JadeCapital.Trading.Contracts.Strategies;
using MediatR;

namespace JadeCapital.Trading.Api.Endpoints;

// ============================================================================
//  StrategyEndpoints — slice 3a (Trader Strategies + Alerts + Planner).
//
//  Conventions (same as JournalEndpoints / TradeMfeMaeEndpoints):
//   - Cada handler MediatR se expone via delegate que recibe ISender; no
//     hay logica de negocio en el endpoint.
//   - Validacion corre en el ValidationBehavior pipeline; errores de
//     dominio/application se mapean a ProblemDetails via ProblemFromResult.
//   - RequireAuthorization + api-general rate limit para todas.
//   - UserId viene del NameIdentifier claim (nunca del body).
//
//  URLs:
//   - GET    /api/strategies?activeOnly=      → StrategyDto[]
//   - GET    /api/strategies/{id}             → StrategyDto | 404
//   - POST   /api/strategies                  → 200 StrategyDto (upsert create)
//   - PATCH  /api/strategies/{id}             → 200 StrategyDto (update)
//   - DELETE /api/strategies/{id}             → 204 (soft-delete)
//   - GET    /api/strategies/{id}/analytics   → StrategyAnalyticsDto
//   - PUT    /api/trades/{tradeId}/strategy   → StrategyDto (tag/untag trade)
// ============================================================================

public static class StrategyEndpoints
{
    public static IEndpointRouteBuilder MapStrategyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/strategies").WithTags("Trading").RequireAuthorization();

        group.MapGet("/", ListAsync)
            .WithName("ListStrategies")
            .WithSummary("Lista las strategies del user. activeOnly=true (default) filtra soft-deleted.")
            .Produces<StrategyDto[]>(StatusCodes.Status200OK)
            .RequireRateLimiting("api-general");

        group.MapPost("/", CreateAsync)
            .WithName("CreateStrategy")
            .WithSummary("Crea una nueva strategy del user. 409 si ya hay una active con el mismo name (case-insensitive).")
            .Produces<StrategyDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting("api-general");

        group.MapPatch("/{id:guid}", UpdateAsync)
            .WithName("UpdateStrategy")
            .WithSummary("Actualiza una strategy del user. 404 si no existe o no pertenece al user. 409 si cambia el name a uno ya activo.")
            .Produces<StrategyDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting("api-general");

        group.MapDelete("/{id:guid}", DeactivateAsync)
            .WithName("DeactivateStrategy")
            .WithSummary("Soft-delete: flipea is_active=false. Idempotente.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting("api-general");

        group.MapGet("/{id:guid}/analytics", GetAnalyticsAsync)
            .WithName("GetStrategyAnalytics")
            .WithSummary("Aggregate metrics sobre los trades cerrados del user con strategy_id = id. 404 si no existe o no pertenece al user.")
            .Produces<StrategyAnalyticsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting("api-general");

        // PATCH /api/trades/{tradeId}/strategy — co-localizado porque el recurso
        // afectado es el trade (la strategy es metadata del trade).
        var tradesGroup = app.MapGroup("/api/trades").WithTags("Trading").RequireAuthorization();

        tradesGroup.MapPut("/{tradeId:guid}/strategy", SetTradeStrategyAsync)
            .WithName("SetTradeStrategy")
            .WithSummary("Asigna/desasigna una strategy al trade del user. Body: { strategyId: Guid | null }. 404 si trade/strategy no pertenecen al user.")
            .Produces<StrategyDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
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

        var query = new ListStrategiesQuery(userId, ActiveOnly: activeOnly ?? true);
        var result = await sender.Send(query, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> CreateAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] UpsertStrategyRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new CreateStrategyCommand(
            userId, req.Name, req.Description, req.Symbol, req.Timeframe, req.Rules);

        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] UpsertStrategyRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new UpdateStrategyCommand(
            id, userId, req.Name, req.Description, req.Symbol, req.Timeframe, req.Rules);

        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> DeactivateAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var result = await sender.Send(new DeactivateStrategyCommand(id, userId), ct);
        return result.IsSuccess
            ? Results.NoContent()
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> GetAnalyticsAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var result = await sender.Send(new GetStrategyAnalyticsQuery(id, userId), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> SetTradeStrategyAsync(
        Guid tradeId,
        [Microsoft.AspNetCore.Mvc.FromBody] SetTradeStrategyRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new SetTradeStrategyCommand(tradeId, userId, req.StrategyId);
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }
}