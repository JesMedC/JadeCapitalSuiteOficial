using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Application.Features.Planner.CreatePlannerSession;
using JadeCapital.Trading.Application.Features.Planner.GetPlannerSessionById;
using JadeCapital.Trading.Application.Features.Planner.GetPlannerSessionsByWeek;
using JadeCapital.Trading.Application.Features.Planner.MarkPlannerSessionStatus;
using JadeCapital.Trading.Application.Features.Planner.UpdatePlannerSession;
using JadeCapital.Trading.Contracts.Planner;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JadeCapital.Trading.Api.Endpoints;

// ============================================================================
//  PlannerEndpoints — slice 3c.
//
//  5 endpoints CRUD + 1 list-by-week. RequireAuthorization. Rate limit
//  api-general.
//
//  UserId siempre viene del JWT claim (NameIdentifier) — NUNCA del body.
//  Esto es cross-user safe: si alguien intenta enviar UserId de otro, el
//  endpoint ignora el body y usa el del JWT.
// ============================================================================

public static class PlannerEndpoints
{
    public static IEndpointRouteBuilder MapPlannerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/planner").RequireAuthorization().RequireRateLimiting("api-general");

        group.MapPost("/sessions", CreateAsync);
        group.MapGet("/sessions/{id:guid}", GetByIdAsync);
        group.MapPatch("/sessions/{id:guid}", UpdateAsync);
        group.MapPatch("/sessions/{id:guid}/status", UpdateStatusAsync);
        group.MapGet("/week", GetWeekAsync);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        UpsertPlannerSessionRequest body,
        HttpContext http,
        ISender sender,
        CancellationToken ct)
    {
        var userId = GetUserId(http);
        if (userId is null) return Results.Unauthorized();

        var sessionDate = PlannerMappingExtensions.ParseLocalDate(body.SessionDate);
        if (sessionDate is null)
            return Results.UnprocessableEntity(new { code = "validation.planner.invalid_session_date" });

        var start = PlannerMappingExtensions.ParseTimeOnly(body.PlannedStartTime);
        var end = PlannerMappingExtensions.ParseTimeOnly(body.PlannedEndTime);

        var result = await sender.Send(new CreatePlannerSessionCommand(
            userId.Value, sessionDate.Value, start, end, body.Symbol, body.Notes), ct);
        return ResultToHttp(result, dto => Results.Created($"/api/planner/sessions/{dto.Id}", dto));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        HttpContext http,
        ISender sender,
        CancellationToken ct)
    {
        var userId = GetUserId(http);
        if (userId is null) return Results.Unauthorized();

        var result = await sender.Send(new GetPlannerSessionByIdQuery(id, userId.Value), ct);
        return ResultToHttp(result, dto => Results.Ok(dto));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpsertPlannerSessionRequest body,
        HttpContext http,
        ISender sender,
        CancellationToken ct)
    {
        var userId = GetUserId(http);
        if (userId is null) return Results.Unauthorized();

        var start = PlannerMappingExtensions.ParseTimeOnly(body.PlannedStartTime);
        var end = PlannerMappingExtensions.ParseTimeOnly(body.PlannedEndTime);

        var result = await sender.Send(new UpdatePlannerSessionCommand(
            id, userId.Value, start, end, body.Symbol, body.Notes), ct);
        return ResultToHttp(result, dto => Results.Ok(dto));
    }

    private static async Task<IResult> UpdateStatusAsync(
        Guid id,
        UpdatePlannerStatusRequest body,
        HttpContext http,
        ISender sender,
        CancellationToken ct)
    {
        var userId = GetUserId(http);
        if (userId is null) return Results.Unauthorized();

        var result = await sender.Send(new MarkPlannerSessionStatusCommand(id, userId.Value, (byte)body.NewStatus), ct);
        return ResultToHttp(result, dto => Results.Ok(dto));
    }

    private static async Task<IResult> GetWeekAsync(
        string? week,
        HttpContext http,
        ISender sender,
        CancellationToken ct)
    {
        var userId = GetUserId(http);
        if (userId is null) return Results.Unauthorized();

        var weekStart = PlannerMappingExtensions.ParseLocalDate(week ?? "");
        if (weekStart is null)
            return Results.UnprocessableEntity(new { code = "validation.planner.invalid_week" });

        var result = await sender.Send(new GetPlannerSessionsByWeekQuery(userId.Value, weekStart.Value), ct);
        return ResultToHttp(result, dto => Results.Ok(dto));
    }

    private static Guid? GetUserId(HttpContext http)
    {
        var raw = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static IResult ResultToHttp<T>(Result<T> result, Func<T, IResult> onSuccess)
    {
        if (result.IsSuccess) return onSuccess(result.Value);
        var code = result.Error.Code ?? "error";
        return code switch
        {
            "planner.not_found" => Results.NotFound(new { code, detail = result.Error.Message }),
            "planner.already_exists_for_date" => Results.Conflict(new { code, detail = result.Error.Message }),
            var c when c.StartsWith("validation.", StringComparison.Ordinal) => Results.UnprocessableEntity(new { code = c, detail = result.Error.Message }),
            _ => Results.BadRequest(new { code, detail = result.Error.Message })
        };
    }
}
