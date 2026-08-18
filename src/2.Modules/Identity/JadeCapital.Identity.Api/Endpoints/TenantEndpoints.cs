using JadeCapital.Identity.Application.Features.Tenants.InviteTenantUser;
using JadeCapital.Identity.Application.Features.Tenants.ListTenantUsers;
using JadeCapital.Identity.Application.Features.Tenants.RemoveTenantUser;
using JadeCapital.Identity.Application.Features.Tenants.UpdateTenant;
using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Identity.Api.Endpoints;

/// <summary>
/// Tenant admin endpoints (Wave 6, slice 6c.3).
///
/// <para>
/// All endpoints follow the same authorization contract:
/// </para>
/// <list type="bullet">
///   <item><c>RequireAuthorization()</c> — the <c>TenantContextMiddleware</c>
///         (slice 6c.2) gates unauthenticated callers and rejects missing /
///         malformed <c>tenant_id</c> JWT claims BEFORE these endpoints
///         run. The middleware ensures a valid tenant context exists.</item>
///   <item>Cross-tenant access returns 404 (not 403) — the handler
///         enforces this rule. The endpoint layer just maps the result
///         to the right HTTP status.</item>
///   <item>SuperAdmin bypass is implemented inside each handler
///         (<c>_tenantContext.IsSuperAdmin</c>). The endpoint does NOT
///         add an explicit <c>RequireRole("SuperAdmin")</c> — tenant
///         owners are regular users, not admins, and the handler-side
///         check is the single source of truth.</item>
/// </list>
///
/// <para>
/// Wire shape (request/response bodies) is defined as nested records at
/// the bottom of the file. The endpoints serialize through MediatR +
/// <see cref="ISender"/>; the handler returns a <see cref="Result{T}"/>
/// that maps to either a JSON body or a 4xx problem detail via
/// <see cref="ProblemFromResult"/>.
/// </para>
/// </summary>
public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants").WithTags("Tenants").RequireAuthorization();

        // PATCH /api/tenants/{id} — rename / change plan.
        group.MapPatch("/{id:guid}", UpdateTenantAsync)
            .WithName("UpdateTenant")
            .WithSummary("Update a tenant's name and/or plan. Tenant owner only.")
            .Produces<JadeCapital.Identity.Application._Common.TenantDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireRateLimiting("api-general");

        // GET /api/tenants/{id}/users — list tenant members.
        group.MapGet("/{id:guid}/users", ListTenantUsersAsync)
            .WithName("ListTenantUsers")
            .WithSummary("List the members of a tenant. Tenant owner only.")
            .Produces<IReadOnlyList<JadeCapital.Identity.Application._Common.TenantUserDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireRateLimiting("api-general");

        // POST /api/tenants/{id}/users — invite a new member.
        group.MapPost("/{id:guid}/users", InviteTenantUserAsync)
            .WithName("InviteTenantUser")
            .WithSummary("Invite a user to the tenant (or assign an existing one). Tenant owner only.")
            .Produces<InviteTenantUserResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireRateLimiting("auth-strict");

        // DELETE /api/tenants/{id}/users/{userId} — remove a member.
        group.MapDelete("/{id:guid}/users/{userId:guid}", RemoveTenantUserAsync)
            .WithName("RemoveTenantUser")
            .WithSummary("Remove a user from the tenant. Tenant owner only; owner cannot remove self.")
            .Produces<RemoveTenantUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireRateLimiting("api-general");

        return app;
    }

    // ===== Helpers =====

    /// <summary>
    /// Maps a domain <see cref="Error"/> to the canonical HTTP status.
    /// Reuses the same routing rules as the existing Identity endpoints
    /// (validation → 400, validation.&lt;aggregate&gt; → 422, notfound →
    /// 404, conflict → 409, forbidden → 403, unauthorized → 401). The
    /// default fallback is 422 because Wave-6 handler failures lean on
    /// domain validation rather than framework-level errors.
    /// </summary>
    private static IResult ProblemFromResult(Error error)
    {
        var status = error.Code switch
        {
            var c when c.StartsWith("notfound", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status404NotFound,
            var c when c.StartsWith("conflict", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status409Conflict,
            var c when c.StartsWith("unauthorized", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status401Unauthorized,
            var c when c.StartsWith("forbidden", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status403Forbidden,
            // Domain validation (range / invariant) → 422 per spec.
            // FluentValidation ValidationException stays at 400 (handled
            // by the global exception handler in Program.cs).
            var c when c.StartsWith("validation", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status422UnprocessableEntity,
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

    private static async Task<IResult> UpdateTenantAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] UpdateTenantRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        var cmd = new UpdateTenantCommand(
            TenantId: id,
            Name: req.Name,
            Plan: req.Plan is null ? (JadeCapital.Identity.Domain.Tenants.TenantPlan?)null
                 : Enum.Parse<JadeCapital.Identity.Domain.Tenants.TenantPlan>(req.Plan, ignoreCase: true));

        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> ListTenantUsersAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        var query = new ListTenantUsersQuery(id);
        var result = await sender.Send(query, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> InviteTenantUserAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromBody] InviteTenantUserRequest req,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        var cmd = new InviteTenantUserCommand(
            TenantId: id,
            Email: req.Email,
            DisplayName: req.DisplayName);

        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Created(
                $"/api/tenants/{id}/users/{result.Value}",
                new InviteTenantUserResponse(result.Value))
            : ProblemFromResult(result.Error);
    }

    private static async Task<IResult> RemoveTenantUserAsync(
        Guid id,
        Guid userId,
        [Microsoft.AspNetCore.Mvc.FromServices] ISender sender,
        CancellationToken ct)
    {
        var cmd = new RemoveTenantUserCommand(TenantId: id, UserId: userId);
        var result = await sender.Send(cmd, ct);
        return result.IsSuccess
            ? Results.Ok(new RemoveTenantUserResponse(result.Value))
            : ProblemFromResult(result.Error);
    }
}

// ===== Request / response records =====

/// <summary>Request body for <c>PATCH /api/tenants/{id}</c>. Both fields
/// are nullable so the client can patch a single field.</summary>
public sealed record UpdateTenantRequest(
    string? Name,
    string? Plan);

/// <summary>Request body for <c>POST /api/tenants/{id}/users</c>.</summary>
public sealed record InviteTenantUserRequest(
    string Email,
    string? DisplayName);

/// <summary>Response body for <c>POST /api/tenants/{id}/users</c>. The
/// <c>UserId</c> identifies the invited-or-assigned user; clients can
/// reference it in subsequent calls.</summary>
public sealed record InviteTenantUserResponse(Guid UserId);

/// <summary>Response body for <c>DELETE /api/tenants/{id}/users/{userId}</c>.
/// The <c>UserId</c> echoes the removed user for client-side bookkeeping.</summary>
public sealed record RemoveTenantUserResponse(Guid UserId);
