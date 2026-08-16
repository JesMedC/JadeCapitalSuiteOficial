using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Features.Journal.CreateOrUpdate;
using JadeCapital.Trading.Application.Features.Journal.Delete;
using JadeCapital.Trading.Application.Features.Journal.GetByRange;
using JadeCapital.Trading.Application.Features.Journal.GetToday;
using JadeCapital.Trading.Contracts.Journal;
using MediatR;

namespace JadeCapital.Trading.Api.Endpoints;

/// <summary>
/// Endpoints del daily journal (slice 2a.1).
///
/// Sigue el patron minimal-API del modulo:
/// <list type="bullet">
///   <item>Cada handler MediatR se expone via delegate que recibe
///   <see cref="ISender"/>; no hay logica de negocio en el endpoint.</item>
///   <item>Validacion corre en el <c>ValidationBehavior</c> pipeline;
///   errores de dominio/application se mapean a ProblemDetails via
///   <see cref="ProblemFromResult"/>.</item>
///   <item>Rate limit: <c>api-general</c> para todos (los journals no
///   son write-heavy — un trader tipico hace 1 upsert por dia).</item>
///   <item>El timezone del usuario se resuelve via
///   <see cref="IUserTimezoneAccessor"/> (lee <c>X-User-Timezone</c>
///   con fallback UTC) y se pasa al handler. El handler nunca toca
///   headers HTTP directamente.</item>
/// </list>
///
/// URLs:
/// <list type="bullet">
///   <item><c>GET /api/journal/today</c> — devuelve el entry de hoy o 404.</item>
///   <item><c>GET /api/journal?from=YYYY-MM-DD&amp;to=YYYY-MM-DD</c> — lista en rango.</item>
///   <item><c>POST /api/journal/today</c> — upsert del entry de hoy.</item>
///   <item><c>DELETE /api/journal/{id}</c> — hard delete (Wave 2).</item>
/// </list>
/// </summary>
public static class JournalEndpoints
{
    public static IEndpointRouteBuilder MapJournalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/journal")
            .WithTags("Trading")
            .RequireAuthorization();

        // GET /api/journal/today — devuelve el entry del usuario para la
        // fecha local de hoy en su timezone, o 404 si no existe.
        group.MapGet("/today", GetTodayAsync)
            .WithName("GetTodayJournalEntry")
            .WithSummary("Devuelve el journal entry del usuario para la fecha local de hoy en su timezone (header X-User-Timezone, fallback UTC). 404 si no existe.")
            .Produces<JournalEntryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting("api-general");

        // GET /api/journal?from=YYYY-MM-DD&to=YYYY-MM-DD — lista en rango.
        group.MapGet("/", GetByRangeAsync)
            .WithName("GetJournalEntriesByRange")
            .WithSummary("Lista los journal entries del usuario en un rango de fechas locales (inclusivo en ambos extremos).")
            .Produces<JournalEntryDto[]>(StatusCodes.Status200OK)
            .RequireRateLimiting("api-general");

        // POST /api/journal/today — upsert (create OR update). El body
        // puede ser parcial; el aggregate rechaza con nothing_to_save
        // si todos los campos son null/empty.
        group.MapPost("/today", UpsertAsync)
            .WithName("UpsertJournalEntry")
            .WithSummary("Crea o actualiza el journal entry del usuario para hoy en su timezone.")
            .Produces<JournalEntryDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireRateLimiting("api-general");

        // DELETE /api/journal/{id} — hard delete. 404 si no existe o si
        // pertenece a otro usuario (cross-user scope colapsa a 404).
        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteJournalEntry")
            .WithSummary("Borra un journal entry por id. 404 si no existe o pertenece a otro usuario.")
            .Produces(StatusCodes.Status204NoContent)
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

    /// <summary>
    /// Parsea un param string <c>YYYY-MM-DD</c> como <see cref="LocalDate"/>.
    /// Devuelve 400 con ProblemDetails si el formato es invalido.
    /// </summary>
    private static IResult? TryParseLocalDate(string? raw, out LocalDate date)
    {
        if (string.IsNullOrWhiteSpace(raw)
            || !DateOnly.TryParseExact(raw, "yyyy-MM-dd", out var dateOnly))
        {
            date = default;
            return Results.Problem(
                type: "https://jadecapital/errors/validation.journal.invalid_date",
                title: "Invalid date",
                detail: $"Date must be in ISO 8601 format (YYYY-MM-DD). Got: '{raw ?? "<null>"}'.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "validation.journal.invalid_date" });
        }

        date = LocalDate.From(dateOnly);
        return null;
    }

    // ===== Handlers =====

    private static async Task<IResult> GetTodayAsync(
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        [Microsoft.AspNetCore.Mvc.FromServices] IUserTimezoneAccessor timezones,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var timezone = timezones.GetTimezoneOrUtc();
        var result = await sender.Send(new GetTodayJournalEntryQuery(userId, timezone), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> GetByRangeAsync(
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        string? from = null,
        string? to = null,
        CancellationToken ct = default)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        if (TryParseLocalDate(from, out var fromDate) is { } fromErr) return fromErr;
        if (TryParseLocalDate(to, out var toDate) is { } toErr) return toErr;

        var result = await sender.Send(new GetJournalEntriesByRangeQuery(userId, fromDate, toDate), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> UpsertAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] UpsertJournalEntryRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        [Microsoft.AspNetCore.Mvc.FromServices] IUserTimezoneAccessor timezones,
        [Microsoft.AspNetCore.Mvc.FromServices] IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var timezone = timezones.GetTimezoneOrUtc();
        var today = LocalDate.From(clock.UtcNow, timezone);

        var cmd = new CreateOrUpdateJournalEntryCommand(
            UserId: userId,
            LocalDate: today,
            Timezone: timezone,
            MoodPre: req.MoodPre,
            MoodDuring: req.MoodDuring,
            MoodPost: req.MoodPost,
            PremarketPlan: req.PremarketPlan,
            PostmarketReflection: req.PostmarketReflection,
            Tags: req.Tags);

        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var result = await sender.Send(new DeleteJournalEntryCommand(id, userId), ct);
        return result.IsSuccess
            ? Results.NoContent()
            : ProblemFromResult(result.Error);
    }
}
