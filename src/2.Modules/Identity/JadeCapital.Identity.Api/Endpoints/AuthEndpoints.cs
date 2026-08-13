using JadeCapital.Identity.Application.Features.Auth.Login;
using JadeCapital.Identity.Application.Features.Auth.Logout;
using JadeCapital.Identity.Application.Features.Auth.Refresh;
using JadeCapital.Identity.Application.Features.Auth.Register;
using JadeCapital.Identity.Application.Features.Recovery;
using JadeCapital.Identity.Application._Common;
using JadeCapital.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using LoginResult = JadeCapital.Identity.Application.Features.Auth.Login.LoginResult;

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

        // Slice 0c — Recovery (always 200 generic; 5/hour/IP throttle; uniform 14s ± 250ms timing).
        group.MapPost("/forgot-password", ForgotPasswordAsync)
            .WithName("ForgotPassword")
            .WithSummary("Solicita recuperacion de contrasena via SMTP.")
            .Produces<ForgotPasswordResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting("recovery")
            .AllowAnonymous();

        // Slice 0c — Forced change with recovery grant (issued after temp login).
        // Or voluntary change with current+new password. Requires JWT with scope=password_change.
        group.MapPost("/change-password", ChangePasswordAsync)
            .WithName("ChangePassword")
            .WithSummary("Cambia la contrasena (grant de recuperacion o voluntaria).")
            .Produces<ChangePasswordResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization("RequirePasswordChangeScope");

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
        HttpContext http,
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
        HttpContext http,
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
        HttpContext http,
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

    private static async Task<IResult> ForgotPasswordAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] ForgotPasswordRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        // Uniform-timing gate: even on the unknown-email path the response
        // returns inside the 14s ± 250ms budget enforced by IUniformTimingGate.
        var ip = http.Connection.RemoteIpAddress?.ToString();
        var cmd = new ForgotPasswordCommand(req.Email ?? string.Empty);
        await sender.Send(cmd, ct);
        // Always 200 generic — never reveal whether the email exists.
        return Results.Ok(new ForgotPasswordResponse(true));
    }

    private static async Task<IResult> ChangePasswordAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] ChangePasswordRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        HttpContext http,
        CancellationToken ct)
    {
        var userIdClaim = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
            return Results.Unauthorized();

        var ip = http.Connection.RemoteIpAddress?.ToString();
        var ua = http.Request.Headers.UserAgent.ToString();

        // Branch on whether the caller is using a recovery grant or a voluntary flow.
        Result<ChangePasswordResult> result;
        if (!string.IsNullOrEmpty(req.GrantJti))
        {
            result = await sender.Send(new ChangePasswordWithGrantCommand(
                userId, req.GrantJti, req.ExpectedSessionVersion, req.NewPassword, ip, ua), ct);
        }
        else
        {
            if (string.IsNullOrEmpty(req.CurrentPassword))
                return ProblemFromResult(IdentityApplicationErrors.Auth.PasswordTooShort);
            result = await sender.Send(new ChangePasswordVoluntaryCommand(
                userId, req.ExpectedSessionVersion, req.CurrentPassword, req.NewPassword, ip, ua), ct);
        }

        return result.IsSuccess
            ? Results.Ok(new ChangePasswordResponse(
                result.Value.AccessToken, result.Value.AccessTokenExpiresAt,
                result.Value.RefreshToken, result.Value.RefreshTokenExpiresAt,
                result.Value.UserId, result.Value.RequiresPasswordChange))
            : ProblemFromResult(result.Error);
    }

    private static IResult ProblemFromResult(JadeCapital.Shared.Kernel.Results.Error error)
    {
        var status = error.Code switch
        {
            // RFC 7807 — slice 0c recovery-specific error codes
            var c when c == "auth.recovery_invalid" || c.StartsWith("unauthorized", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status401Unauthorized,
            var c when c == "auth.password_reused" || c.StartsWith("conflict", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status409Conflict,
            var c when c == "auth.concurrent_update" => StatusCodes.Status409Conflict,
            var c when c.StartsWith("validation", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status400BadRequest,
            var c when c.StartsWith("notfound", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status404NotFound,
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

/// <summary>Request body for <c>POST /api/auth/forgot-password</c>. Email is
/// trimmed and lowercased by the handler. Always returns 200 generic regardless
/// of account existence, throttle outcome, or transport failure.</summary>
public sealed record ForgotPasswordRequest(string? Email);

/// <summary>Response body for <c>POST /api/auth/forgot-password</c>. The boolean
/// is a placeholder; clients MUST treat any 200 as the same outcome.</summary>
public sealed record ForgotPasswordResponse(bool Accepted);

/// <summary>Request body for <c>POST /api/auth/change-password</c>. If
/// <see cref="GrantJti"/> is present the handler treats it as a forced
/// change; otherwise <see cref="CurrentPassword"/> is required.</summary>
public sealed record ChangePasswordRequest(
    string NewPassword,
    int ExpectedSessionVersion,
    string? GrantJti,
    string? CurrentPassword);

/// <summary>Response body for <c>POST /api/auth/change-password</c>.</summary>
public sealed record ChangePasswordResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid UserId,
    bool RequiresPasswordChange);