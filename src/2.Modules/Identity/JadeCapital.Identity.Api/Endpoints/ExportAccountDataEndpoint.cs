using System.Security.Claims;
using System.Text.Json;
using JadeCapital.Identity.Application.Features.Auth.ExportAccountData;
using JadeCapital.Shared.Kernel.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JadeCapital.Identity.Api.Endpoints;

// ============================================================================
//  ExportAccountDataEndpoint — Wave 11 slice 11.3
//
//  GDPR Art. 20 data-portability endpoint: `GET /api/users/me/export`.
//
//  <para>
//  Auth: `RequireAuthorization()` — any authenticated user can export
//  their OWN data. The handler resolves the userId from the JWT
//  `NameIdentifier` claim; there is no path for an actor to export
//  anyone else's data (no role check is needed beyond authentication).
//  </para>
//
//  <para>
//  Response: `200 OK` with `Content-Type: application/json`. The body
//  is a JSON object with named properties per `ExportSectionType`
//  (e.g. `{ "user_profile": {...}, "password_history": {...}, ... }`).
//  The handler streams section-by-section through
//  <see cref="IAsyncEnumerable{T}"/>; the endpoint materialises them
//  into a JSON object keyed by `SectionType`. The wire format version
//  is `1.0` (initial).
//  </para>
//
//  <para>
//  <b>Error contract</b>: 401 if the JWT is missing or malformed;
//  404 if the user has been hard-deleted between token issuance and
//  the request (rare; covered by the canonical `notfound.identity.user_not_found`
//  error code).
//  </para>
// ============================================================================

public static class ExportAccountDataEndpoint
{
    public static IEndpointRouteBuilder MapExportAccountDataEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users/me").WithTags("Users").RequireAuthorization();

        group.MapGet("/export", ExportAccountDataAsync)
            .WithName("ExportMyAccountData")
            .WithSummary("GDPR Art. 20: export the authenticated user's data as machine-readable JSON.")
            .Produces<ExportAccountDataWireDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// Endpoint delegate. Resolves the JWT-derived userId, sends the
    /// <see cref="ExportAccountDataQuery"/>, and materialises the
    /// streamed sections into a JSON object keyed by `SectionType`.
    /// </summary>
    private static async Task<IResult> ExportAccountDataAsync(
        ClaimsPrincipal user,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ExportAccountDataQuery(userId), ct);
        if (result.IsFailure)
        {
            return ProblemFromResult(result.Error);
        }

        var dto = result.Value;
        // Materialise the IAsyncEnumerable<ExportSection> into a
        // {SectionType: Data} JSON object. We use a Dictionary so
        // duplicate section types (defensive: future additions
        // shouldn't ship duplicates) collapse to the last seen
        // value — fail-loud vs fail-silent is preferable for
        // opaque data, but serialisation is the canonical surface
        // and the wire-format version ("1.0") signals the layout.
        var sections = new Dictionary<string, object?>(StringComparer.Ordinal);
        await foreach (var section in dto.Sections.WithCancellation(ct))
        {
            sections[section.SectionType] = section.Data;
        }

        var wire = new ExportAccountDataWireDto(
            UserId: dto.UserId,
            FormatVersion: dto.FormatVersion,
            ExportedAt: dto.ExportedAt,
            Sections: sections);

        return Results.Json(wire, contentType: "application/json");
    }

    private static IResult ProblemFromResult(Error error)
    {
        var status = error.Code switch
        {
            var c when c.StartsWith("unauthorized", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status401Unauthorized,
            var c when c.StartsWith("notfound", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status404NotFound,
            var c when c.StartsWith("conflict", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status409Conflict,
            var c when c.StartsWith("forbidden", StringComparison.OrdinalIgnoreCase) => StatusCodes.Status403Forbidden,
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

    /// <summary>Wire-shape for `GET /api/users/me/export`. The
    /// <c>Sections</c> dictionary is keyed by
    /// <see cref="ExportSection.SectionType"/>. Each value is the
    /// opaque Data payload (an anonymous object the handler yields).</summary>
    public sealed record ExportAccountDataWireDto(
        Guid UserId,
        string FormatVersion,
        DateTimeOffset ExportedAt,
        IReadOnlyDictionary<string, object?> Sections);
}
