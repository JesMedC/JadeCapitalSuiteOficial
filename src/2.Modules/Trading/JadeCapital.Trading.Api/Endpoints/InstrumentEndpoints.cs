using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Features.Instruments.CreateInstrument;
using JadeCapital.Trading.Application.Features.Instruments.DeactivateInstrument;
using JadeCapital.Trading.Application.Features.Instruments.DeleteInstrument;
using JadeCapital.Trading.Application.Features.Instruments.GetInstrumentById;
using JadeCapital.Trading.Application.Features.Instruments.GetInstruments;
using JadeCapital.Trading.Application.Features.Instruments.UpdateInstrument;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Enums;
using MediatR;

namespace JadeCapital.Trading.Api.Endpoints;

/// <summary>
/// Endpoints CRUD para /api/instruments. Instrument es global (no per-user),
/// asi que NO se valida ownership: cualquier usuario autenticado puede
/// ver / listar / crear / desactivar / borrar instrumentos en V1.
/// </summary>
public static class InstrumentEndpoints
{
    public static IEndpointRouteBuilder MapInstrumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/instruments").WithTags("Instruments").RequireAuthorization();

        group.MapPost("/", CreateInstrumentAsync)
            .WithName("CreateInstrument")
            .WithSummary("Crea un nuevo instrumento en el catalogo global.")
            .Produces<InstrumentDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting("auth-strict");

        group.MapGet("/", GetInstrumentsAsync)
            .WithName("ListInstruments")
            .WithSummary("Lista instrumentos (default: solo activos).")
            .Produces<IReadOnlyList<InstrumentDto>>(StatusCodes.Status200OK)
            .RequireRateLimiting("api-general");

        group.MapGet("/{id:guid}", GetInstrumentByIdAsync)
            .WithName("GetInstrumentById")
            .WithSummary("Obtiene un instrumento por id.")
            .Produces<InstrumentDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting("api-general");

        group.MapPatch("/{id:guid}", UpdateInstrumentAsync)
            .WithName("UpdateInstrument")
            .WithSummary("Actualiza metadata de un instrumento.")
            .Produces<InstrumentDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting("auth-strict");

        group.MapPost("/{id:guid}/deactivate", DeactivateInstrumentAsync)
            .WithName("DeactivateInstrument")
            .WithSummary("Desactiva un instrumento (preserva historial de trades).")
            .Produces<InstrumentDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting("auth-strict");

        group.MapDelete("/{id:guid}", DeleteInstrumentAsync)
            .WithName("DeleteInstrument")
            .WithSummary("Borra un instrumento (solo si no tiene trades asociados).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting("auth-strict");

        return app;
    }

    // ===== Helpers (duplicado: cuando consolidemos en 1.5F/Host, extraemos) =====

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

    private static async Task<IResult> CreateInstrumentAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] CreateInstrumentRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        var cmd = new CreateInstrumentCommand(
            req.Symbol,
            req.AssetClasses,
            req.ContractSize,
            req.DecimalPlaces,
            req.PipValue,
            req.PayoutPercent);

        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Created($"/api/instruments/{result.Value.Id}", result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> GetInstrumentsAsync(
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        bool? activeOnly = null,
        CancellationToken ct = default)
    {
        // Default = solo activos. Pasar ?activeOnly=false para incluir inactivos.
        var query = new GetInstrumentsQuery(ActiveOnly: activeOnly ?? true);
        var result = await sender.Send(query, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> GetInstrumentByIdAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        var query = new GetInstrumentByIdQuery(id);
        var result = await sender.Send(query, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> UpdateInstrumentAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] UpdateInstrumentRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        var cmd = new UpdateInstrumentCommand(
            id,
            req.Symbol,
            req.AssetClasses,
            req.ContractSize,
            req.DecimalPlaces,
            req.PipValue,
            req.PayoutPercent);

        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> DeactivateInstrumentAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        var cmd = new DeactivateInstrumentCommand(id);
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> DeleteInstrumentAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        var cmd = new DeleteInstrumentCommand(id);
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.NoContent()
            : ProblemFromResult(result.Error);
    }
}

// ===== Request records =====

public sealed record CreateInstrumentRequest(
    string Symbol,
    AssetClass AssetClasses,
    decimal? ContractSize,
    int? DecimalPlaces,
    decimal? PipValue,
    decimal? PayoutPercent);

public sealed record UpdateInstrumentRequest(
    string Symbol,
    AssetClass AssetClasses,
    decimal? ContractSize,
    int? DecimalPlaces,
    decimal? PipValue,
    decimal? PayoutPercent);
