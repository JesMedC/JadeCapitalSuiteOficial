using Microsoft.AspNetCore.Authorization;

namespace JadeCapital.Admin.Api.Authorization;

/// <summary>
/// Custom <see cref="AuthorizationHandler{T}"/> that enforces the Admin-only
/// authorization policy at the endpoint boundary — BEFORE any handler runs
/// and BEFORE any subscription lookup or mutation side effect. Slice 0f of
/// <c>jade-trader-os-core-portals</c>.
///
/// Spec requirement (subscription-administration/spec.md, "Unauthorized
/// caller" scenario): "access MUST be denied before lookup or mutation …
/// no existence, owner, plan, or history information MUST leak".
///
/// What the handler checks:
/// <list type="bullet">
///   <item>The principal MUST be authenticated (a forced-change / restricted
///   scope JWT also passes the JWT-bearer middleware but MUST NOT pass here).
///   <see cref="AuthorizationHandlerContext.User"/> carries the role claim
///   populated by the existing <c>RoleClaimType = ClaimTypes.Role</c>
///   binding in Program.cs.</item>
///   <item>The principal MUST carry the <c>Admin</c> role claim. Trader,
///   anonymous, suspended, restricted-scope, and locked-out identities all
///   fail this gate.</item>
/// </list>
///
/// Why an explicit handler instead of <c>RequireRole</c>:
/// we want a clear seam to surface 401 (unauthenticated) vs. 403 (authenticated
/// but not Admin) without leaking which role names exist. The Admin role name
/// itself is not in the response body, only the standard ProblemDetails code.
/// </summary>
public sealed class RequireAdminPolicyHandler : AuthorizationHandler<RequireAdminRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RequireAdminRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            // No identity at all — fail the requirement. The endpoint will
            // surface 401 via the JWT-bearer challenge.
            return Task.CompletedTask;
        }

        // The Admin role is the only allowlisted role for Wave 0. Trader users
        // (the default RegisterUserHandler role) MUST be denied. Restricted-scope
        // tokens (scope=password_change) carry no Admin role claim.
        if (context.User.IsInRole("Admin"))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Marker requirement consumed by <see cref="RequireAdminPolicyHandler"/>.</summary>
public sealed class RequireAdminRequirement : IAuthorizationRequirement { }
