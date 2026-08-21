using FluentAssertions;
using JadeCapital.Host.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JadeCapital.Host.UnitTests.Configuration;

public sealed class ReverseProxyConfigurationTests
{
    [Theory]
    [InlineData("ReverseProxy:KnownProxies:0", "not-an-ip", "KnownProxies")]
    [InlineData("ReverseProxy:KnownNetworks:0", "10.0.0.0/not-a-prefix", "KnownNetworks")]
    [InlineData("ReverseProxy:ForwardLimit", "0", "ForwardLimit")]
    public async Task MalformedConfiguration_RejectsStartup(string key, string value, string expectedError)
    {
        await using var app = CreateApp(new Dictionary<string, string?> { [key] = value });

        var start = () => app.StartAsync();

        await start.Should().ThrowAsync<OptionsValidationException>()
            .WithMessage($"*{expectedError}*");
    }

    [Fact]
    public async Task ValidConfiguration_BindsKnownTrustAndForwardLimit()
    {
        await using var app = CreateApp(new Dictionary<string, string?>
        {
            ["ReverseProxy:KnownProxies:0"] = "10.0.0.10",
            ["ReverseProxy:KnownNetworks:0"] = "10.42.0.0/16",
            ["ReverseProxy:ForwardLimit"] = "2",
        });
        await app.StartAsync();

        var options = app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.ForwardedHeaders.Should().Be(
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        options.ForwardLimit.Should().Be(2);
        options.KnownProxies.Should().ContainSingle().Which.Should().Be(
            System.Net.IPAddress.Parse("10.0.0.10"));
        options.KnownIPNetworks.Should().ContainSingle().Which.ToString().Should().Be("10.42.0.0/16");
    }

    [Fact]
    public async Task EmptyConfiguration_ClearsFrameworkLoopbackTrust()
    {
        await using var app = CreateApp(new Dictionary<string, string?>());
        await app.StartAsync();

        var options = app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.ForwardLimit.Should().Be(1);
        options.ForwardedHeaders.Should().Be(ForwardedHeaders.None);
        options.KnownProxies.Should().BeEmpty();
        options.KnownIPNetworks.Should().BeEmpty();
    }

    [Fact]
    public void HostPipeline_AppliesForwardedHeadersBeforeOtherMiddleware()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var source = File.ReadAllText(Path.Combine(
            root, "src", "1.Api", "JadeCapital.Host", "Program.cs"));
        var buildIndex = source.IndexOf("var app = builder.Build();", StringComparison.Ordinal);
        var forwardedIndex = source.IndexOf("app.UseForwardedHeaders();", StringComparison.Ordinal);

        buildIndex.Should().BeGreaterThanOrEqualTo(0);
        forwardedIndex.Should().BeGreaterThan(buildIndex);
        source[(buildIndex + "var app = builder.Build();".Length)..forwardedIndex]
            .Should().NotContain("app.Use", "forwarded headers must be the first middleware");
    }

    private static WebApplication CreateApp(IReadOnlyDictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddJadeCapitalReverseProxy();
        return builder.Build();
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "JadeCapital.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            $"Could not locate repository root from {startDirectory}.");
    }
}
