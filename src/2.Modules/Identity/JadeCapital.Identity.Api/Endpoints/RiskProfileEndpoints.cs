using JadeCapital.Identity.Application.Features.RiskProfileActions.CreateOrSupersedeRiskProfile;
using JadeCapital.Identity.Application.Features.RiskProfileActions.GetActiveRiskProfile;
using JadeCapital.Identity.Contracts.RiskProfiles;
using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Identity.Api.Endpoints;

/// <summary>
/// Endpoints publicos del perfil de riesgo del usuario. Slice 1a.1b.
///
/// <list type="bullet">
///   <item><c>GET /api/risk-profile</c> — devuelve el perfil activo o
///   <c>404</c> si no existe. RequireAuthorization, rate limit
///   api-general.</item>
///   <item><c>PUT /api/risk-profile</c> — crea o supersede el perfil activo.
///   RequireAuthorization. La validacion de los rangos la corre
///   FluentValidation en el body binding (max 422); el aggregate domain
///   re-chekea como defense in depth. La concurrencia (race entre dos
///   PUT simultaneos del mismo usuario) surfaces como 409 conflict.
/// </item>
/// </list>
///
/// PII: el handler NO loggea capital / currency / percentages al info
/// level. Solo el userId aparece en logs (cumpliendo el scenario
/// "Profile read or write logging" del spec).
/// </summary>
public static class RiskProfileEndpoints
{
    public static IEndpointRouteBuilder MapRiskProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/risk-profile").WithTags("Risk Profile").RequireAuthorization();

        group.MapGet("/", GetRiskProfileAsync)
            .WithName("GetRiskProfile")
            .WithSummary("Obtiene el perfil de riesgo activo del usuario autenticado.")
            .Produces<RiskProfileDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireRateLimiting("api-general");

        group.MapPut("/", UpsertRiskProfileAsync)
            .WithName("UpsertRiskProfile")
            .WithSummary("Crea o supersede el perfil de riesgo del usuario autenticado.")
            .Produces<RiskProfileDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
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
        // Spec scenario "Out-of-range field" requires 422 for range
        // validation failures (i.e. domain-level VOs like
        // RiskPerTradePercent.Create). FluentValidation still maps to
        // 400 (schema / structural). Distinguimos por el prefijo
        // "validation.risk_profile.*" (los codigos del aggregate) vs
        // los emitidos por FluentValidation ("validation.*" genericos).
        var status = error.Code switch
        {
            var c when c.StartsWith("notfound", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status404NotFound,
            var c when c.StartsWith("conflict", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status409Conflict,
            var c when c.StartsWith("unauthorized", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status401Unauthorized,
            var c when c.StartsWith("forbidden", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status403Forbidden,
            // Domain range errors (aggregate + VOs) → 422 per spec.
            // FluentValidation ValidationException stays at 400 (handled
            // by the global exception handler in Program.cs).
            var c when c.StartsWith("validation.risk_profile", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status422UnprocessableEntity,
            var c when c.StartsWith("validation", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status400BadRequest,
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

    private static async Task<IResult> GetRiskProfileAsync(
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var result = await sender.Send(new GetActiveRiskProfileQuery(userId), ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> UpsertRiskProfileAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] UpsertRiskProfileRequest req,
        HttpContext http,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        Guid userId;
        try { userId = GetUserId(http); }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }

        var cmd = new CreateOrSupersedeRiskProfileCommand(
            UserId: userId,
            CapitalAmount: req.CapitalAmount,
            CapitalCurrency: req.CapitalCurrency,
            MaxDrawdownPercent: req.MaxDrawdownPercent,
            RiskPerTradePercent: req.RiskPerTradePercent,
            RiskRewardTarget: req.RiskRewardTarget);

        var upsertResult = await sender.Send(cmd, ct);
        if (upsertResult.IsFailure) return ProblemFromResult(upsertResult.Error);

        // Then load the full DTO so the client gets a complete representation
        // (we don't trust the in-memory aggregate after SaveChangesAsync).
        var query = new GetActiveRiskProfileQuery(userId);
        var dtoResult = await sender.Send(query, ct);
        return dtoResult.IsSuccess
            ? Results.Ok(dtoResult.Value)
            : ProblemFromResult(dtoResult.Error);
    }
}
