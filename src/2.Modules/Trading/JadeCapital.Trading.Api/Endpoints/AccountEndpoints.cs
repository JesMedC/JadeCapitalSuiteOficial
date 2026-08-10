using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Features.Accounts.DeactivateAccount;
using JadeCapital.Trading.Application.Features.Accounts.DeleteAccount;
using JadeCapital.Trading.Application.Features.Accounts.GetAccountById;
using JadeCapital.Trading.Application.Features.Accounts.GetAccounts;
using JadeCapital.Trading.Application.Features.Accounts.OpenAccount;
using JadeCapital.Trading.Application.Features.Accounts.ReactivateAccount;
using JadeCapital.Trading.Application.Features.Accounts.UpdateAccount;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Enums;
using MediatR;

namespace JadeCapital.Trading.Api.Endpoints;

/// <summary>
/// Endpoints CRUD para /api/accounts. Minimal API: cada handler de
/// aplicacion se expone via un delegate que recibe el ISender de MediatR.
/// Validacion corre por ValidationBehavior; errores se mapean a ProblemDetails.
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts").WithTags("Accounts").RequireAuthorization();

        group.MapPost("/", OpenAccountAsync)
            .WithName("OpenAccount")
            .WithSummary("Abre una nueva cuenta de trading para el usuario autenticado.")
            .Produces<AccountDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .RequireRateLimiting("auth-strict");

        group.MapGet("/", GetAccountsAsync)
            .WithName("ListAccounts")
            .WithSummary("Lista las cuentas del usuario autenticado.")
            .Produces<IReadOnlyList<AccountDto>>(StatusCodes.Status200OK)
            .RequireRateLimiting("api-general");

        group.MapGet("/{id:guid}", GetAccountByIdAsync)
            .WithName("GetAccountById")
            .WithSummary("Obtiene una cuenta por id (solo del usuario autenticado).")
            .Produces<AccountDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting("api-general");

        group.MapPatch("/{id:guid}", UpdateAccountAsync)
            .WithName("UpdateAccount")
            .WithSummary("Actualiza metadata (name, broker, currency, leverage, payout) de una cuenta.")
            .Produces<AccountDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting("auth-strict");

        group.MapPost("/{id:guid}/deactivate", DeactivateAccountAsync)
            .WithName("DeactivateAccount")
            .WithSummary("Desactiva una cuenta (preserva el historial de trades).")
            .Produces<AccountDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting("auth-strict");

        group.MapPost("/{id:guid}/reactivate", ReactivateAccountAsync)
            .WithName("ReactivateAccount")
            .WithSummary("Reactiva una cuenta previamente desactivada.")
            .Produces<AccountDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting("auth-strict");

        group.MapDelete("/{id:guid}", DeleteAccountAsync)
            .WithName("DeleteAccount")
            .WithSummary("Borra una cuenta (solo si no tiene trades asociados).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting("auth-strict");

        return app;
    }

    // ===== Helpers (duplicado de TradeEndpoints: cuando consolidemos los
    // helpers en 1.5F/Host lo extraemos a una clase compartida) =====

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

    private static async Task<IResult> OpenAccountAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] OpenAccountRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new OpenAccountCommand(
            userId,
            req.Name,
            req.Broker,
            req.MarketType,
            req.Currency,
            req.InitialBalance,
            req.Leverage);

        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Created($"/api/accounts/{result.Value.Id}", result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> GetAccountsAsync(
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var query = new GetAccountsQuery(userId);
        var result = await sender.Send(query, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> GetAccountByIdAsync(
        Guid id,
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var query = new GetAccountByIdQuery(id, userId);
        var result = await sender.Send(query, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> UpdateAccountAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] UpdateAccountRequest req,
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new UpdateAccountCommand(
            id,
            userId,
            req.Name,
            req.Broker,
            req.MarketType,
            req.Currency,
            req.Leverage);

        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> DeactivateAccountAsync(
        Guid id,
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new DeactivateAccountCommand(id, userId);
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> ReactivateAccountAsync(
        Guid id,
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new ReactivateAccountCommand(id, userId);
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> DeleteAccountAsync(
        Guid id,
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new DeleteAccountCommand(id, userId);
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.NoContent()
            : ProblemFromResult(result.Error);
    }
}

// ===== Request records =====

public sealed record OpenAccountRequest(
    string Name,
    string Broker,
    MarketType MarketType,
    string Currency,
    decimal InitialBalance,
    decimal? Leverage);

public sealed record UpdateAccountRequest(
    string Name,
    string Broker,
    MarketType MarketType,
    string Currency,
    decimal? Leverage);
