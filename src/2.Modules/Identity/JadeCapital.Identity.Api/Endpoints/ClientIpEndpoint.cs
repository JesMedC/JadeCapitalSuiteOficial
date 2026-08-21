using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JadeCapital.Identity.Api.Endpoints;

/// <summary>
/// Exposes the transport address resolved by the Host's forwarded-headers
/// middleware for GDPR consent correlation. This endpoint never interprets
/// forwarding headers itself.
/// </summary>
public static class ClientIpEndpoint
{
    public static IEndpointRouteBuilder MapClientIpEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/util/client-ip", GetClientIp)
            .WithName("GetClientIp")
            .WithTags("Utilities")
            .WithSummary("GDPR Art. 7: returns the originating client IP for consent correlation.")
            .Produces<ClientIpDto>(StatusCodes.Status200OK)
            .AllowAnonymous();

        return app;
    }

    private static IResult GetClientIp(HttpContext context) =>
        Results.Ok(new ClientIpDto(
            context.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0"));

    public sealed record ClientIpDto(string Ip);
}
