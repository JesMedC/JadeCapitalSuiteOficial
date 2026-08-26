using System.Security.Claims;
using System.Threading.RateLimiting;
using JadeCapital.Identity.Api.Endpoints;
using JadeCapital.Identity.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Identity.Api;

/// <summary>
/// Composition surface for the Identity module's API surface. Slice 0c adds:
///   * <see cref="MapIdentityApi"/> — endpoint mapping entry point.
///   * <see cref="IUniformTimingGate"/> — pads sensitive paths (forgot-password)
///     to the 14s ± 250ms budget per identity-password-recovery spec.
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
    /// GET/PUT /api/risk-profile) + <c>MapTenantEndpoints</c> (slice 6c.3 —
    /// tenant admin: list / invite / remove users + update).
    /// </summary>
    public static IEndpointRouteBuilder MapIdentityApi(this IEndpointRouteBuilder app)
    {
        app.MapAuthEndpoints();
        app.MapRiskProfileEndpoints();
        app.MapTenantEndpoints();
        // Wave 11 slice 11.2b — /api/users/me (DELETE → GDPR right-to-be-forgotten).
        app.MapUserEndpoints();
        // Wave 11 slice 11.3 — /api/users/me/export (GET → GDPR Art. 20 portability).
        app.MapExportAccountDataEndpoint();
        // Wave 11 slice 11.4 — /api/auth/consent (POST → GDPR ePrivacy cookie banner decision).
        app.MapConsentEndpoint();
        return app;
    }

    /// <summary>Registers the rate limit policy used by the recovery endpoints (5 requests / IP / hour by default;
    /// overridable via <c>RateLimit:RecoveryPermit</c> in configuration for fast-running integration tests).</summary>
    public static IServiceCollection AddRecoveryThrottle(this IServiceCollection services, int permitLimit = 5)
    {
        services.AddSingleton(PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
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
        }));
        return services;
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
/// failure based on response latency.
/// </summary>
public readonly record struct UniformTimingDeadline(long StartedAt, TimeSpan Target);

public interface IUniformTimingGate
{
    UniformTimingDeadline Begin();
    Task AwaitAsync(UniformTimingDeadline deadline, CancellationToken ct = default);
}

public sealed class UniformTimingGate : IUniformTimingGate
{
    private static readonly EventId OverrunEvent = new(1001, "UniformTimingOverrun");
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UniformTimingGate> _logger;

    public UniformTimingGate(TimeProvider timeProvider, ILogger<UniformTimingGate> logger)
    { _timeProvider = timeProvider; _logger = logger; }

    public UniformTimingDeadline Begin() => new(
        _timeProvider.GetTimestamp(),
        TimeSpan.FromMilliseconds(Random.Shared.Next(13_750, 14_251)));

    public async Task AwaitAsync(UniformTimingDeadline deadline, CancellationToken ct = default)
    {
        var elapsed = _timeProvider.GetElapsedTime(deadline.StartedAt);
        var remaining = deadline.Target - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, _timeProvider, ct);
            return;
        }

        if (elapsed > deadline.Target)
            _logger.LogWarning(OverrunEvent, "Uniform timing target overrun.");
    }
}
