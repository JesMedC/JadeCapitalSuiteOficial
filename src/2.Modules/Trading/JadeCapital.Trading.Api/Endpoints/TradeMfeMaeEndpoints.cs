using JadeCapital.Trading.Application.Features.MfeMae.GetTradeMfeMae;
using JadeCapital.Trading.Contracts.MfeMae;
using MediatR;

namespace JadeCapital.Trading.Api.Endpoints;

// ============================================================================
//  TradeMfeMaeEndpoints — slice 2c (Trader Journal Core / MFE/MAE Charts).
//
//  GET /api/trades/{tradeId}/mfe-mae
//
//  Returns the requested trade's MFE/MAE plus the user's aggregate
//  histograms for direction × {winners, losers} × {MFE, MAE}.
//
//  Conventions (same as BehavioralEndpoints / JournalEndpoints):
//   - Each handler is a delegate receiving ISender (no business logic).
//   - RequireAuthorization + api-general rate limit.
//   - ProblemFromResult maps Result.Error to RFC 7807 ProblemDetails.
//   - The userId comes from the NameIdentifier claim (never from the body).
//
//  Cross-user safety: the handler unifies missing + foreign-owned trades
//  in NotFound so the 404 response can't leak trade existence.
// ============================================================================

public static class TradeMfeMaeEndpoints
{
    public static IEndpointRouteBuilder MapTradeMfeMaeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/trades").WithTags("Trading").RequireAuthorization();

        group.MapGet("/{id:guid}/mfe-mae", GetTradeMfeMaeAsync)
            .WithName("GetTradeMfeMae")
            .WithSummary(
                "Devuelve el MFE/MAE aproximado del trade (Wave 2) y los histogramas " +
                "agregados del usuario por direccion (Long/Short) x outcome (winners/losers). " +
                "Open trades devuelven mfeAmount=null, maeAmount=null. Cross-user → 404.")
            .Produces<TradeMfeMaeDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting("api-general");

        return app;
    }

    private static Guid GetUserId(HttpContext http)
    {
        var claim = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (claim is null || !Guid.TryParse(claim, out var id))
            throw new UnauthorizedAccessException("Invalid user claim.");
        return id;
    }

    private static IResult ProblemFromResult(JadeCapital.Shared.Kernel.Results.Error error)
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

    private static async Task<IResult> GetTradeMfeMaeAsync(
        Guid id,
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var query = new GetTradeMfeMaeQuery(id, userId);
        var result = await sender.Send(query, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }
}