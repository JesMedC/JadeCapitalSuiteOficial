using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Features.Dashboard.GetDashboardSummary;
using JadeCapital.Trading.Application.Features.Dashboard.GetPnlCalendar;
using JadeCapital.Trading.Application.Features.Trades.CloseTrade;
using JadeCapital.Trading.Application.Features.Trades.DeleteTrade;
using JadeCapital.Trading.Application.Features.Trades.GetTradeById;
using JadeCapital.Trading.Application.Features.Trades.GetTrades;
using JadeCapital.Trading.Application.Features.Trades.OpenTrade;
using JadeCapital.Trading.Application.Features.Trades.UpdateTradeNotes;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Enums;
using MediatR;

namespace JadeCapital.Trading.Api.Endpoints;

/// <summary>
/// Endpoints del modulo Trading. Minimal API: cada handler de aplicacion
/// (Commands/Queries) se expone via un delegate que recibe el ISender de
/// MediatR. Validacion corre por el ValidationBehavior registrado en el
/// pipeline; errores de dominio/aplicacion se mapean a ProblemDetails.
/// </summary>
public static class TradeEndpoints
{
    public static IEndpointRouteBuilder MapTradeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/trades").WithTags("Trading").RequireAuthorization();

        // POST /api/trades — open trade
        group.MapPost("/", OpenTradeAsync)
            .WithName("OpenTrade")
            .WithSummary("Abre una nueva operacion de trading.")
            .Produces<TradeDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting("auth-strict");

        // GET /api/trades — listar
        group.MapGet("/", GetTradesAsync)
            .WithName("ListTrades")
            .WithSummary("Lista operaciones del usuario con paginacion y filtros.")
            .Produces<PagedTradesDto>(StatusCodes.Status200OK)
            .RequireRateLimiting("api-general");

        // GET /api/trades/dashboard — summary
        group.MapGet("/dashboard", GetDashboardSummaryAsync)
            .WithName("GetDashboardSummary")
            .WithSummary("KPIs agregados para el dashboard del usuario.")
            .Produces<DashboardSummaryDto>(StatusCodes.Status200OK)
            .RequireRateLimiting("api-general");

        // GET /api/trades/calendar — calendar data
        group.MapGet("/calendar", GetPnlCalendarAsync)
            .WithName("GetPnlCalendar")
            .WithSummary("P&L diario agregado por mes.")
            .Produces<CalendarDto>(StatusCodes.Status200OK)
            .RequireRateLimiting("api-general");

        // GET /api/trades/{id}
        group.MapGet("/{id:guid}", GetTradeByIdAsync)
            .WithName("GetTradeById")
            .WithSummary("Obtiene una operacion por id (solo del usuario autenticado).")
            .Produces<TradeDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting("api-general");

        // PUT /api/trades/{id}/close
        group.MapPut("/{id:guid}/close", CloseTradeAsync)
            .WithName("CloseTrade")
            .WithSummary("Cierra una operacion abierta calculando P&L.")
            .Produces<TradeDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting("auth-strict");

        // PATCH /api/trades/{id}
        group.MapPatch("/{id:guid}", UpdateTradeNotesAsync)
            .WithName("UpdateTradeNotes")
            .WithSummary("Actualiza strategy y notes de una operacion.")
            .Produces<TradeDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting("auth-strict");

        // DELETE /api/trades/{id}
        group.MapDelete("/{id:guid}", DeleteTradeAsync)
            .WithName("DeleteTrade")
            .WithSummary("Elimina una operacion (solo Open o Cancelled).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting("auth-strict");

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

    private static async Task<IResult> OpenTradeAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] OpenTradeRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new OpenTradeCommand(
            UserId: userId,
            Symbol: req.Symbol,
            AssetClass: req.AssetClass,
            Direction: req.Direction,
            Volume: req.Volume,
            VolumeCurrency: req.VolumeCurrency,
            EntryPrice: req.EntryPrice,
            EntryPriceCurrency: req.EntryPriceCurrency,
            Strategy: req.Strategy,
            Notes: req.Notes);

        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Created($"/api/trades/{result.Value.Id}", result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> GetTradesAsync(
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        int page = 1,
        int pageSize = 20,
        TradeStatus? status = null,
        string? symbol = null,
        CancellationToken ct = default)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var query = new GetTradesQuery(userId, page, pageSize, status, symbol);
        var result = await sender.Send(query, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> GetTradeByIdAsync(
        Guid id,
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var query = new GetTradeByIdQuery(id, userId);
        var result = await sender.Send(query, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> CloseTradeAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] CloseTradeRequest req,
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new CloseTradeCommand(id, userId, req.ExitPrice, req.ExitPriceCurrency);
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> UpdateTradeNotesAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] UpdateTradeNotesRequest req,
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new UpdateTradeNotesCommand(id, userId, req.Strategy, req.Notes);
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> DeleteTradeAsync(
        Guid id,
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new DeleteTradeCommand(id, userId);
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.NoContent()
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> GetDashboardSummaryAsync(
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var now = DateTimeOffset.UtcNow;
        var query = new GetDashboardSummaryQuery(
            userId,
            from ?? now.AddDays(-30),
            to ?? now);
        var result = await sender.Send(query, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> GetPnlCalendarAsync(
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        int? year = null,
        int? month = null,
        CancellationToken ct = default)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var now = DateTimeOffset.UtcNow;
        var query = new GetPnlCalendarQuery(
            userId,
            year ?? now.Year,
            month ?? now.Month);
        var result = await sender.Send(query, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }
}

// ===== Request records =====

public sealed record OpenTradeRequest(
    string Symbol,
    AssetClass AssetClass,
    TradeDirection Direction,
    decimal Volume,
    string VolumeCurrency,
    decimal EntryPrice,
    string EntryPriceCurrency,
    string? Strategy,
    string? Notes);

public sealed record CloseTradeRequest(decimal ExitPrice, string ExitPriceCurrency);

public sealed record UpdateTradeNotesRequest(string? Strategy, string? Notes);
