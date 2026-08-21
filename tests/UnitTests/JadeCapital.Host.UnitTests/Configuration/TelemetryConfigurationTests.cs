using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using FluentAssertions;
using JadeCapital.Host.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Sentry;
using Sentry.Extensibility;
using Sentry.Protocol.Envelopes;

namespace JadeCapital.Host.UnitTests.Configuration;

[Collection("Telemetry SDK")]
public sealed class TelemetryConfigurationTests
{
    private const string Dsn = "https://public@example.invalid/42";
    private const string Release = "jadecapital@12.1.0+test";

    [Fact]
    public void Read_UsesOnlyCanonicalSentryDsnAndTrimsOptionalEndpoints()
    {
        var canonical = Configuration(new Dictionary<string, string?>
        {
            ["Sentry:Dsn"] = $"  {Dsn}  ",
            ["Sentry__Dsn"] = "https://legacy@example.invalid/99",
            ["Observability:Release"] = $"  {Release}  ",
            ["OpenTelemetry:Otlp:Endpoint"] = "  http://127.0.0.1:4317  "
        });
        var legacyOnly = Configuration(new Dictionary<string, string?>
        {
            ["Sentry__Dsn"] = Dsn
        });

        var settings = TelemetryConfiguration.Read(canonical);

        settings.SentryDsn.Should().Be(Dsn);
        settings.Release.Should().Be(Release);
        settings.OtlpEndpoint.Should().Be(new Uri("http://127.0.0.1:4317"));
        TelemetryConfiguration.Read(legacyOnly).SentryDsn.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-absolute-uri")]
    public void Read_DisablesInvalidOrEmptyOtlpEndpoint(string? endpoint)
    {
        var settings = TelemetryConfiguration.Read(Configuration(
            new Dictionary<string, string?> { ["OpenTelemetry:Otlp:Endpoint"] = endpoint }));

        settings.OtlpEndpoint.Should().BeNull();
    }

    [Fact]
    public void ConfigureSentry_PreservesReleaseAndStacksWithPiiDisabled()
    {
        var options = new Sentry.AspNetCore.SentryAspNetCoreOptions();
        var settings = new TelemetrySettings(Dsn, Release, null);

        TelemetryConfiguration.ConfigureSentry(options, settings, "Production");

        options.Dsn.Should().Be(Dsn);
        options.Release.Should().Be(Release);
        options.Environment.Should().Be("Production");
        options.AttachStacktrace.Should().BeTrue();
        options.SendDefaultPii.Should().BeFalse();
        options.MaxRequestBodySize.Should().Be(RequestSize.None);
    }

    [Fact]
    public async Task EmptyConfiguration_StartsWithoutSentryOrTraceExporter()
    {
        var sentryTransport = new EnvelopeCaptureTransport();
        var traceExporter = new ActivityCaptureExporter();
        await using var app = await StartHostAsync(
            new Dictionary<string, string?>(),
            sentryTransport,
            traceExporter,
            map: web => web.MapGet("/health", () => Results.Ok()));

        var response = await CreateClient(app).GetAsync("/health");
        await SentrySdk.FlushAsync(TimeSpan.FromSeconds(1));

        response.IsSuccessStatusCode.Should().BeTrue();
        sentryTransport.Envelopes.Should().BeEmpty();
        traceExporter.Activities.Should().BeEmpty();
        app.Services.GetService<TracerProvider>().Should().BeNull();
    }

    [Fact]
    public async Task UnhandledException_ReachesLocalSentryTransportWithReleaseAndSafeResolvableStack()
    {
        const string email = "private.person@example.com";
        const string bearer = "top-secret-bearer";
        const string cookie = "session=top-secret-cookie";
        const string bodySecret = "top-secret-body";
        var transport = new EnvelopeCaptureTransport();
        await using var app = await StartHostAsync(
            new Dictionary<string, string?>
            {
                ["Sentry:Dsn"] = Dsn,
                ["Observability:Release"] = Release
            },
            transport,
            traceExporter: null,
            map: web => web.MapPost("/telemetry/fail", () => ThrowUnhandled()));
        var client = CreateClient(app);
        client.DefaultRequestHeaders.Authorization = new("Bearer", bearer);
        client.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await client.PostAsync(
            $"/telemetry/fail?email={email}&ip=203.0.113.9",
            new StringContent(bodySecret));
        await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var envelope = transport.Envelopes.Single(value => value.Contains("\"type\":\"event\"", StringComparison.Ordinal));
        envelope.Should().Contain("jadecapital@12.1.0");
        envelope.Should().Contain("test");
        envelope.Should().Contain(nameof(ThrowUnhandled));
        envelope.Should().Contain("TelemetryConfigurationTests.cs");
        envelope.Should().NotContain(email);
        envelope.Should().NotContain("203.0.113.9");
        envelope.Should().NotContain(bearer);
        envelope.Should().NotContain(cookie);
        envelope.Should().NotContain(bodySecret);
        SentrySdk.Close();
    }

    [Fact]
    public async Task ConfiguredOtlp_ExportsSafeAspNetCoreRouteFieldsThroughLocalExporter()
    {
        const string email = "private.person@example.com";
        const string bearer = "top-secret-bearer";
        const string cookie = "session=top-secret-cookie";
        var exporter = new ActivityCaptureExporter();
        await using var app = await StartHostAsync(
            new Dictionary<string, string?>
            {
                ["OpenTelemetry:Otlp:Endpoint"] = "http://127.0.0.1:4317"
            },
            sentryTransport: null,
            exporter,
            map: web => web.MapGet("/telemetry/{id:int}", (int id) => Results.Ok(new { id })));
        var client = CreateClient(app);
        client.DefaultRequestHeaders.Authorization = new("Bearer", bearer);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        var provider = app.Services.GetRequiredService<TracerProvider>();

        var response = await client.GetAsync($"/telemetry/42?email={email}&ip=203.0.113.9");
        provider.ForceFlush(2000).Should().BeTrue();

        response.IsSuccessStatusCode.Should().BeTrue();
        var activity = exporter.Activities.Should().ContainSingle().Subject;
        activity.TraceId.Should().NotBe(default);
        activity.DisplayName.Should().Be("GET /telemetry/{id:int}");
        activity.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        activity.TagObjects.Any(tag =>
            (tag.Key == "http.request.method" || tag.Key == "http.method") && Equals(tag.Value, "GET"))
            .Should().BeTrue();
        activity.TagObjects.Any(tag =>
            (tag.Key == "http.response.status_code" || tag.Key == "http.status_code")
            && Convert.ToInt32(tag.Value) == 200).Should().BeTrue();
        activity.TagObjects.Should().Contain(tag => tag.Key == "http.route" && Equals(tag.Value, "/telemetry/{id:int}"));
        var exported = string.Join('|', activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"))
            + string.Join('|', activity.Events.Select(e => e.Name));
        exported.Should().NotContain(email);
        exported.Should().NotContain("203.0.113.9");
        exported.Should().NotContain(bearer);
        exported.Should().NotContain(cookie);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static async Task<WebApplication> StartHostAsync(
        Dictionary<string, string?> values,
        ITransport? sentryTransport,
        BaseExporter<Activity>? traceExporter,
        Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(values);
        builder.AddJadeCapitalTelemetry(sentryTransport, traceExporter);
        var app = builder.Build();
        map(app);
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app) =>
        new() { BaseAddress = new Uri(app.Urls.Single()) };

    private static IResult ThrowUnhandled() =>
        throw new InvalidOperationException("telemetry transport probe");

    private sealed class ActivityCaptureExporter : BaseExporter<Activity>
    {
        public ConcurrentQueue<Activity> Activities { get; } = new();

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
                Activities.Enqueue(activity);
            return ExportResult.Success;
        }
    }

    private sealed class EnvelopeCaptureTransport : ITransport
    {
        public ConcurrentQueue<string> Envelopes { get; } = new();

        public async Task SendEnvelopeAsync(Envelope envelope, CancellationToken cancellationToken = default)
        {
            await using var stream = new MemoryStream();
            await envelope.SerializeAsync(stream, null, cancellationToken);
            Envelopes.Enqueue(Encoding.UTF8.GetString(stream.ToArray()));
        }
    }
}

[CollectionDefinition("Telemetry SDK", DisableParallelization = true)]
public sealed class TelemetrySdkDefinition;
