using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Identity.Infrastructure.MultiTenancy;

/// <summary>
/// ASP.NET middleware that enforces the <c>tenant_id</c> JWT claim is
/// present (and well-formed) on authenticated requests (Wave 6, slice 6c.2).
///
/// <para>
/// Sits in the pipeline right after <c>UseAuthentication</c> and
/// <c>UseAuthorization</c>. Anonymous requests pass through unchanged
/// (public endpoints — <c>/auth/login</c>, <c>/auth/register</c>, health
/// checks, billing public catalog — keep working). Authenticated requests
/// MUST carry a <c>tenant_id</c> claim whose value is a valid
/// <see cref="Guid"/>; otherwise the middleware short-circuits with
/// <c>401 auth.tenant_missing</c> / <c>auth.tenant_malformed</c> and the
/// request never reaches a handler.
/// </para>
///
/// <para>
/// <b>Why a separate middleware (not a JWT validator)</b>: we cannot
/// enforce <c>tenant_id</c> at JWT-validation time because the validator
/// is shared across endpoints, some of which (auth + public billing)
/// are anonymous. Splitting the gate at a custom middleware keeps the
/// pipeline declarative: every authenticated endpoint gets the rule for
/// free, and the error code is stable.
/// </para>
///
/// <para>
/// <b>Why it does NOT set the <see cref="JadeCapital.Shared.Kernel.MultiTenancy.ITenantContext"/></b>:
/// the scoped context resolves from <see cref="IHttpContextAccessor"/>
/// at access-time, so the middleware has nothing to cache. If we ever
/// switch to a request-cached implementation, the seam is here.
/// </para>
///
/// <para>
/// <b>Performance</b>: one synchronous <see cref="Guid.TryParse"/> per
/// authenticated request. No allocations beyond the 401 response body.
/// </para>
///
/// <para>
/// <b>Error contract</b>: the response is a problem+json body that
/// mirrors the global <c>ProblemDetails</c> shape used by the rest of
/// the host (<c>Program.cs</c> <c>UseExceptionHandler</c>):
/// <c>{ type, title, status, code, instance }</c>. Clients that already
/// parse problem+json can handle this without a new branch.
/// </para>
/// </summary>
public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    /// <summary>
    /// Pipeline entry. Stamps the request body with a problem+json 401
    /// when the gate fails and leaves the response untouched otherwise.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var user = context.User;

        // Anonymous calls are the normal public path: /auth/login,
        // /auth/register, /health/*, /api/billing/plans, etc. The
        // gate only fires when a JWT IS present.
        if (user?.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var tenantClaim = user.FindFirst("tenant_id");
        if (tenantClaim is null || string.IsNullOrWhiteSpace(tenantClaim.Value))
        {
            await WriteUnauthorizedAsync(context, "auth.tenant_missing",
                "Authenticated request is missing the required tenant_id claim.");
            return;
        }

        if (!Guid.TryParse(tenantClaim.Value, out _))
        {
            await WriteUnauthorizedAsync(context, "auth.tenant_malformed",
                "The tenant_id claim is not a valid identifier.");
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Writes the 401 problem+json body and stops the pipeline.
    /// </summary>
    private static async Task WriteUnauthorizedAsync(HttpContext ctx, string code, string detail)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.Response.ContentType = "application/problem+json";
        // Stable wire shape — matches the global ProblemDetails used in Program.cs.
        var body = $$"""
                     {"type":"https://jadecapital/errors/{{code}}","title":"Tenant required","status":401,"code":"{{code}}","detail":"{{detail}}"}
                     """;
        await ctx.Response.WriteAsync(body);
    }
}
