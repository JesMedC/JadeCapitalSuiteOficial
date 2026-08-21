using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace JadeCapital.Host.Configuration;

public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    public string[] KnownProxies { get; set; } = [];

    public string[] KnownNetworks { get; set; } = [];

    public int ForwardLimit { get; set; } = 1;
}

public static class ReverseProxyConfiguration
{
    public static WebApplicationBuilder AddJadeCapitalReverseProxy(
        this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<ReverseProxyOptions>()
            .Bind(builder.Configuration.GetSection(ReverseProxyOptions.SectionName))
            .Validate(
                options => options.KnownProxies.All(value => IPAddress.TryParse(value, out _)),
                "ReverseProxy:KnownProxies must contain valid IP addresses.")
            .Validate(
                options => options.KnownNetworks.All(IsValidCidr),
                "ReverseProxy:KnownNetworks must contain valid CIDR networks.")
            .Validate(
                options => options.ForwardLimit > 0,
                "ReverseProxy:ForwardLimit must be greater than zero.")
            .ValidateOnStart();

        builder.Services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ReverseProxyOptions>>((forwarded, configured) =>
            {
                var options = configured.Value;
                var hasTrustedUpstream =
                    options.KnownProxies.Length > 0 || options.KnownNetworks.Length > 0;
                forwarded.ForwardedHeaders = hasTrustedUpstream
                    ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
                    : ForwardedHeaders.None;
                forwarded.ForwardLimit = options.ForwardLimit;

                forwarded.KnownProxies.Clear();
                foreach (var proxy in options.KnownProxies)
                {
                    forwarded.KnownProxies.Add(IPAddress.Parse(proxy));
                }

                forwarded.KnownIPNetworks.Clear();
                foreach (var network in options.KnownNetworks)
                {
                    forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
                }
            });

        return builder;
    }

    private static bool IsValidCidr(string value) =>
        value.Contains('/', StringComparison.Ordinal) && System.Net.IPNetwork.TryParse(value, out _);
}
