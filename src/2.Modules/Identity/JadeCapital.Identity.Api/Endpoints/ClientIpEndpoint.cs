using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JadeCapital.Identity.Api.Endpoints;

// ============================================================================
//  ClientIpEndpoint — Wave 12 slice 12.1
//
//  `GET /api/util/client-ip` — returns the originating client IP for the
//  GDPR Art. 7 consent-correlation path.
//
//  <para>
//  Anonymous: the endpoint is intentionally unauthenticated. The cookie
//  consent banner must be able to read the IP BEFORE the visitor has
//  logged in (or even registered). The IP is captured for consent audit
//  purposes only — it does NOT unlock anything.
//  </para>
//
//  <para>
//  Resolution order:
//  <list type="number">
//  <item><c>X-Forwarded-For</c> first hop — left-most is the original
//  client per RFC 7239 §5.2; subsequent hops are intermediaries. We read
//  the header verbatim because <c>UseForwardedHeaders()</c> in
//  <c>Program.cs</c> clears KnownProxies so ANY upstream proxy is
//  trusted (acceptable because the only ingress in prod is nginx —
//  see <c>docker-compose.prod.yml</c>).</item>
//  <item><c>HttpContext.Connection.RemoteIpAddress</c> — the
//  transport-layer peer when there is no proxy in front of us (local
//  dev, integration tests).</item>
//  </list>
//  </para>
//
//  <para>
//  Wire shape:
//  <code>
//    GET /api/util/client-ip
//    Response 200: { "ip": "203.0.113.42" }
//    Response 200: { "ip": "0.0.0.0" }   // when both fallbacks are null
//  </code>
//  The "0.0.0.0" sentinel makes the FE's `?? "0.0.0.0"` fallback explicit
//  and prevents the consent column from receiving NULL (which would break
//  the GDPR audit query that filters on <c>consent_ip IS NOT NULL</c>).
//  </para>
//
//  <para>
//  <b>Threat model</b>: a hostile client could spoof <c>X-Forwarded-For</c>
//  to register a fake consent IP. We accept that — the IP is for AUDIT,
//  not authorisation. Trusting the header is required for nginx to work
//  at all, and the nginx config does not strip the header.
//  </para>
// ============================================================================

public static class ClientIpEndpoint
{
    public static IEndpointRouteBuilder MapClientIpEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/util/client-ip", GetClientIpAsync)
            .WithName("GetClientIp")
            .WithTags("Utilities")
            .WithSummary("GDPR Art. 7: returns the originating client IP for consent correlation.")
            .Produces<ClientIpDto>(StatusCodes.Status200OK)
            .AllowAnonymous();

        return app;
    }

    private static IResult GetClientIpAsync(HttpContext ctx)
    {
        var forwarded = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            // X-Forwarded-For is a comma-separated list: client, proxy1, proxy2, ...
            // Left-most (index 0) is the original client per RFC 7239 §5.2.
            var firstHop = forwarded.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(firstHop))
            {
                return Results.Ok(new ClientIpDto(firstHop));
            }
        }

        var remoteIp = ctx.Connection.RemoteIpAddress?.ToString();
        return Results.Ok(new ClientIpDto(remoteIp ?? "0.0.0.0"));
    }

    /// <summary>Wire shape for <c>GET /api/util/client-ip</c>.</summary>
    public sealed record ClientIpDto(string Ip);
}
