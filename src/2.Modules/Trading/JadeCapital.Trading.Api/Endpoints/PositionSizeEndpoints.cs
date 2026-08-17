using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Features.PositionSize;
using MediatR;

namespace JadeCapital.Trading.Api.Endpoints;

// ============================================================================
//  PositionSizeEndpoints — slice 1b.
//
//  POST /api/trades/position-size/calculate
//  Body: { stopLossDistance, riskPerTradeOverride?, currency }
//  Auth: RequireAuthorization + rate limit "api-general".
//
//  El calculator es read-only / informational (spec): no persiste, no
//  enforce el volume calculado en server-side. El trader puede override
//  RiskPerTrade por trade (1c.1 ya lo soporta en OpenTradeCommand).
//
//  Status mapping (via ProblemFromResult local):
//    - validation.position_size.*  → 422 Unprocessable Entity (request bien
//      formado, regla de negocio falla — mismo patron que
//      validation.pre_trade_checklist.* en TradeEndpoints)
//    - notfound.risk_profile.*     → 404 Not Found
//    - otro                        → 422 (default, mismo fallback que
//      TradeEndpoints.ProblemFromResult)
//
//  Esto vive en un archivo separado de TradeEndpoints a proposito:
//  1. Mantiene el single-responsibility de TradeEndpoints (CRUD de trades).
//  2. Permite que un futuro slice (1c.1 ya hecho, 1d, 1e, etc.) agregue
//     helpers de ProblemFromResult sin tocar el resto.
// 3.  El helper ProblemFromResult esta duplicado en TradeEndpoints pero
//     con la misma forma — refactor compartido queda fuera del scope
//     de slice 1b.
// ============================================================================

public static class PositionSizeEndpoints
{
    public static IEndpointRouteBuilder MapPositionSizeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/trades/position-size")
            .WithTags("Trading")
            .RequireAuthorization();

        group.MapPost("/calculate", CalculatePositionSizeAsync)
            .WithName("CalculatePositionSize")
            .WithSummary("Calcula el tamano de posicion sugerido segun el perfil de riesgo del usuario.")
            .Produces<PositionSizeDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
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

    /// <summary>
    /// Mapea <see cref="Error"/> a <see cref="IResult"/> (RFC 7807 ProblemDetails).
    /// Misma forma que TradeEndpoints.ProblemFromResult, pero con la lista
    /// de codigos validation.position_size.* ya contemplada explicitamente.
    /// </summary>
    private static IResult ProblemFromResult(Error error)
    {
        var status = error.Code switch
        {
            // Slice 1b: position-size validation errors are 422 — same
            // semantic as pre_trade_checklist.* (well-formed request,
            // business-rule rejection).
            var c when c.StartsWith("validation.position_size", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status422UnprocessableEntity,
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

    private static async Task<IResult> CalculatePositionSizeAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] PositionSizeRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var query = new CalculatePositionSizeQuery(
            UserId: userId,
            StopLossDistance: req.StopLossDistance,
            RiskPerTradeOverride: req.RiskPerTradeOverride,
            Currency: req.Currency);

        var result = await sender.Send(query, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }
}
