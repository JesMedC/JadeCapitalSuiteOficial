using System.Security.Claims;
using System.Threading.RateLimiting;
using JadeCapital.Identity.Api.Endpoints;
using JadeCapital.Identity.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace JadeCapital.Identity.Api;

/// <summary>
/// Composition surface for the Identity module's API surface. Slice 0c adds:
///   * <see cref="MapIdentityApi"/> — endpoint mapping entry point.
///   * <see cref="IUniformTimingGate"/> — pads sensitive paths (forgot-password)
///     to the 14s ± 250ms budget per identity-password-recovery spec, using a
///     dummy PBKDF2 chain so the response time is invariant in account state.
///   * <see cref="AddAdminOnly"/> — Admin authorization policy skeleton (full
///     wiring lives in slice 0f).
///   * <see cref="AddRecoveryThrottle"/> — 5-per-hour-per-IP rate limit policy
///     for the recovery endpoints.
/// </summary>
public static class IdentityApiRegistration
{
    /// <summary>
    /// Map all Identity-module endpoints. Combines <c>MapAuthEndpoints</c> (auth,
    /// recovery, refresh) + <c>MapRiskProfileEndpoints</c> (slice 1a.1b —
    /// GET/PUT /api/risk-profile).
    /// </summary>
    public static IEndpointRouteBuilder MapIdentityApi(this IEndpointRouteBuilder app)
    {
        app.MapAuthEndpoints();
        app.MapRiskProfileEndpoints();
        return app;
    }

    /// <summary>Registers the rate limit policy used by the recovery endpoints (5 requests / IP / hour by default;
    /// overridable via <c>RateLimit:RecoveryPermit</c> in configuration for fast-running integration tests).</summary>
    public static RateLimiterOptions AddRecoveryThrottle(this RateLimiterOptions options, int permitLimit = 5)
    {
        options.AddPolicy("recovery", ctx =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: $"recovery-{ip}",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        });
        return options;
    }

    /// <summary>Registers the restricted-scope authorization policy used by the forced-change endpoint.</summary>
    public static AuthorizationOptions AddRestrictedScopePolicy(this AuthorizationOptions options)
    {
        options.AddPolicy("RequirePasswordChangeScope", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(ctx => ctx.User.HasClaim(c => c.Type == "scope" && c.Value == "password_change"));
        });
        return options;
    }

    /// <summary>Skeleton Admin-only authorization policy. Full implementation lands in slice 0f (Admin.Api).</summary>
    public static AuthorizationOptions AddAdminOnly(this AuthorizationOptions options)
    {
        options.AddPolicy("AdminOnly", policy =>
        {
            // Skeleton: require an admin role claim once slice 0f introduces the
            // role-claim layout. Slice 0c does NOT touch Admin scope; the policy
            // is registered here so AddIdentityApi is forward-compatible.
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(ctx => ctx.User.IsInRole("Admin"));
        });
        return options;
    }
}

/// <summary>
/// Uniform-timing gate. Pads sensitive paths to a fixed budget so attackers
/// cannot distinguish between account-existing / not-found / throttle / transport
/// failure based on response latency. The dummy PBKDF2 chain runs the same
/// number of iterations regardless of branch, achieving time-invariant response
/// at the cost of ~14s CPU per request.
/// </summary>
public interface IUniformTimingGate
{
    /// <summary>
    /// Block the current request until the uniform budget elapses. The jitter is
    /// ±250ms per spec.
    /// </summary>
    Task AwaitAsync(double targetSeconds, CancellationToken ct = default);
}

public sealed class UniformTimingGate : IUniformTimingGate
{
    private const int DummyIterations = 100_000; // matches Pbkdf2PasswordHasher minimum
    private readonly ILogger<UniformTimingGate> _logger;

    public UniformTimingGate(ILogger<UniformTimingGate> logger) { _logger = logger; }

    public async Task AwaitAsync(double targetSeconds, CancellationToken ct = default)
    {
        // Jitter ±250ms per spec; we keep the original budget inside that band.
        var jitterMs = Random.Shared.Next(-250, 250);
        var targetMs = (int)(targetSeconds * 1000) + jitterMs;
        var start = DateTimeOffset.UtcNow;

        // Dummy PBKDF2 chain: 100k iterations × 32 bytes is a budget-filler
        // that produces identical CPU work on every code path.
        var dummy = new byte[32];
        var salt = new byte[16];
        for (var i = 0; i < DummyIterations; i++)
        {
            Microsoft.AspNetCore.Cryptography.KeyDerivation.KeyDerivation.Pbkdf2(
                "uniform-timing-dummy", salt,
                Microsoft.AspNetCore.Cryptography.KeyDerivation.KeyDerivationPrf.HMACSHA256,
                1, dummy.Length);
        }

        var elapsed = (DateTimeOffset.UtcNow - start).TotalMilliseconds;
        var remaining = targetMs - elapsed;
        if (remaining > 0)
        {
            try { await Task.Delay(TimeSpan.FromMilliseconds(remaining), ct); }
            catch (OperationCanceledException) { /* caller aborted; respect */ }
        }

        _logger.LogDebug("Uniform timing gate released at {Elapsed} ms (budget {Budget} ms).", elapsed, targetMs);
    }
}