using JadeCapital.Identity.Application.Features.Auth.Login;
using JadeCapital.Identity.Application.Features.Auth.Logout;
using JadeCapital.Identity.Application.Features.Auth.Refresh;
using JadeCapital.Identity.Application.Features.Auth.Register;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JadeCapital.Identity.Api.Endpoints;

/// <summary>
/// Endpoints de autenticacion. Minimal API: cada endpoint es un delegate que
/// recibe el ISender de MediatR y delega al handler.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/register", RegisterAsync)
            .WithName("RegisterUser")
            .WithSummary("Registra un nuevo usuario y emite tokens.")
            .Produces<RegisterUserResult>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting("auth-strict")
            .AllowAnonymous();

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .WithSummary("Autentica al usuario y emite tokens.")
            .Produces<LoginResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireRateLimiting("auth-strict")
            .AllowAnonymous();

        group.MapPost("/refresh", RefreshAsync)
            .WithName("RefreshToken")
            .WithSummary("Rota el refresh token y emite nuevos tokens.")
            .Produces<RefreshTokenResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireRateLimiting("auth-strict")
            .AllowAnonymous();

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .WithSummary("Revoca el refresh token presentado.")
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] RegisterUserCommand cmd,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Created($"/api/users/{result.Value.UserId}", result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> LoginAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] LoginRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        [Microsoft.AspNetCore.Mvc.FromServices] HttpContext http,
        CancellationToken ct)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString();
        var ua = http.Request.Headers.UserAgent.ToString();
        var cmd = new LoginCommand(req.Email, req.Password, ip, ua);
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> RefreshAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] RefreshTokenRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        [Microsoft.AspNetCore.Mvc.FromServices] HttpContext http,
        CancellationToken ct)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString();
        var ua = http.Request.Headers.UserAgent.ToString();
        var cmd = new RefreshTokenCommand(req.RefreshToken, ip, ua);
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> LogoutAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] LogoutRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        [Microsoft.AspNetCore.Mvc.FromServices] HttpContext http,
        CancellationToken ct)
    {
        var userIdClaim = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
            return Results.Unauthorized();

        var cmd = new LogoutCommand(userId, req.RefreshToken);
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.NoContent()
            : ProblemFromResult(result.Error);
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
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return Results.Problem(
            type: $"https://jadecapital/errors/{error.Code}",
            title: "Request failed",
            detail: error.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}

public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);