using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Features.Scanner.CreateOrUpdateScannerFilter;
using JadeCapital.Trading.Application.Features.Scanner.DeleteScannerFilter;
using JadeCapital.Trading.Application.Features.Scanner.ListScannerFilters;
using JadeCapital.Trading.Application.Features.Scanner.RunScanner;
using JadeCapital.Trading.Contracts.Scanner;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace JadeCapital.Trading.Api.Endpoints;

public static class ScannerEndpoints
{
    public static IEndpointRouteBuilder MapScannerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/scanner").RequireAuthorization().RequireRateLimiting("api-general");

        group.MapPost("/filters", CreateFilterAsync);
        group.MapGet("/filters", ListFiltersAsync);
        group.MapGet("/filters/{id:guid}", GetFilterAsync);
        group.MapPatch("/filters/{id:guid}", UpdateFilterAsync);
        group.MapDelete("/filters/{id:guid}", DeleteFilterAsync);
        group.MapPost("/run", RunAsync);

        return app;
    }

    private static async Task<IResult> CreateFilterAsync(
        UpsertScannerFilterRequest body, HttpContext http, ISender sender, CancellationToken ct)
    {
        var userId = GetUserId(http);
        if (userId is null) return Results.Unauthorized();
        var result = await sender.Send(new CreateOrUpdateScannerFilterCommand(
            userId.Value, body.Name, body.MinSpread, body.MaxSpread, body.MinVolume,
            body.MinRiskReward, body.VolatilityWindow, body.ActiveHours), ct);
        return ResultToHttp(result, dto => Results.Created($"/api/scanner/filters/{dto.Id}", dto));
    }

    private static async Task<IResult> ListFiltersAsync(
        bool? activeOnly, HttpContext http, ISender sender, CancellationToken ct)
    {
        var userId = GetUserId(http);
        if (userId is null) return Results.Unauthorized();
        var result = await sender.Send(new ListScannerFiltersQuery(userId.Value, activeOnly ?? false), ct);
        return ResultToHttp(result, dtos => Results.Ok(dtos));
    }

    private static async Task<IResult> GetFilterAsync(
        Guid id, HttpContext http, ISender sender, CancellationToken ct)
    {
        var userId = GetUserId(http);
        if (userId is null) return Results.Unauthorized();
        var result = await sender.Send(new ListScannerFiltersQuery(userId.Value, false), ct);
        if (result.IsSuccess)
        {
            var found = result.Value.FirstOrDefault(f => f.Id == id);
            return found is null
                ? Results.NotFound(new { code = "scanner.not_found" })
                : Results.Ok(found);
        }
        return Results.BadRequest(new { code = result.Error.Code, detail = result.Error.Message });
    }

    private static async Task<IResult> UpdateFilterAsync(
        Guid id, UpsertScannerFilterRequest body, HttpContext http, ISender sender, CancellationToken ct)
    {
        var userId = GetUserId(http);
        if (userId is null) return Results.Unauthorized();
        var result = await sender.Send(new CreateOrUpdateScannerFilterCommand(
            userId.Value, body.Name, body.MinSpread, body.MaxSpread, body.MinVolume,
            body.MinRiskReward, body.VolatilityWindow, body.ActiveHours), ct);
        return ResultToHttp(result, dto => Results.Ok(dto));
    }

    private static async Task<IResult> DeleteFilterAsync(
        Guid id, HttpContext http, ISender sender, CancellationToken ct)
    {
        var userId = GetUserId(http);
        if (userId is null) return Results.Unauthorized();
        var result = await sender.Send(new DeleteScannerFilterCommand(id, userId.Value), ct);
        return result.IsSuccess ? Results.NoContent() : Results.NotFound(new { code = result.Error.Code });
    }

    private static async Task<IResult> RunAsync(
        RunScannerRequest body, HttpContext http, ISender sender, CancellationToken ct)
    {
        var userId = GetUserId(http);
        if (userId is null) return Results.Unauthorized();
        var result = await sender.Send(new RunScannerQuery(body.FilterId, userId.Value, body.Limit ?? 20), ct);
        return ResultToHttp(result, dtos => Results.Ok(dtos));
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
            "scanner.not_found" => Results.NotFound(new { code, detail = result.Error.Message }),
            var c when c.StartsWith("validation.", StringComparison.Ordinal) => Results.UnprocessableEntity(new { code = c, detail = result.Error.Message }),
            _ => Results.BadRequest(new { code, detail = result.Error.Message })
        };
    }
}
