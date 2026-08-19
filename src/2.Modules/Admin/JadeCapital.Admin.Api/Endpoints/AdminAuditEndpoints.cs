using JadeCapital.Admin.Application.Features.Audit;
using JadeCapital.Shared.Kernel.Audit;
using JadeCapital.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JadeCapital.Admin.Api.Endpoints;

/// <summary>
/// Admin-only audit query endpoint (Wave 9, slice 9b.1 — admin query
/// API, sub-scope B).
///
/// <para>
/// Surface: <c>GET /api/admin/audit/events</c> with structured filters +
/// opaque base64 cursor pagination. Enforces
/// <c>RequireAuthorization("AdminOnly")</c> at the endpoint boundary via
/// the existing <see cref="Authorization.RequireAdminPolicyHandler"/>
/// from slice 0f — anonymous → 401, Trader role → 403, Admin role →
/// 200 (no DB lookup happens for the deny paths).
/// </para>
///
/// <para>
/// <b>Filters</b> (all optional, AND-combined):
/// <list type="bullet">
///   <item><c>entity_type</c> — exact match on <c>audit.events.entity_type</c>.</item>
///   <item><c>action</c> — short byte value (0=Created, 1=Updated, 2=Deleted, 3=Restored, 4=Denied, 5=Failed).</item>
///   <item><c>user_id</c> — actor Guid.</item>
///   <item><c>tenant_id</c> — tenant Guid.</item>
///   <item><c>from</c>, <c>to</c> — ISO 8601 timestamps, half-open <c>[from, to)</c>.</item>
///   <item><c>cursor</c> — opaque base64 of the previous page's last item's <c>(occurred_at_ticks, id_guid)</c>.</item>
///   <item><c>limit</c> — page size, clamped to <c>[1, 200]</c>, default 50.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>PII contract</b>: admin role sees all fields (id, entity_type,
/// entity_id, action as enum name, tenant_id, user_id, changes, occurred_at).
/// The compliance contract is enforced at the endpoint boundary — no
/// trader or anonymous access ever reaches the DTO mapping.
/// </para>
///
/// <para>
/// <b>Rate limiting</b>: <c>RequireRateLimiting("api-general")</c>
/// (matches the slice 0f <see cref="AdminSubscriptionEndpoints"/>
/// precedent).
/// </para>
/// </summary>
public static class AdminAuditEndpoints
{
    public static IEndpointRouteBuilder MapAdminAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/audit/events")
            .WithTags("Admin.Audit")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/", ListAsync)
            .WithName("AdminListAuditEvents")
            .WithSummary("Paged list of audit events with structured filters + cursor pagination.")
            .Produces<PagedAuditEventsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireRateLimiting("api-general");

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromQuery] string? entity_type,
        [FromQuery] AuditAction? action,
        [FromQuery] Guid? user_id,
        [FromQuery] Guid? tenant_id,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var query = new ListAuditEventsQuery(
            EntityType: entity_type,
            Action: action,
            UserId: user_id,
            TenantId: tenant_id,
            From: from,
            To: to,
            Cursor: cursor,
            Limit: limit ?? ListAuditEventsQuery.DefaultLimit);

        var result = await sender.Send(query, ct);

        if (result.IsSuccess)
            return Results.Ok(result.Value);

        // Map validation errors to 400 (the handler emits "validation.*" codes).
        var status = result.Error.Code.StartsWith("validation", StringComparison.OrdinalIgnoreCase)
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status422UnprocessableEntity;

        return Results.Problem(
            type: $"https://jadecapital/errors/{result.Error.Code}",
            title: "Request failed",
            detail: result.Error.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
    }
}