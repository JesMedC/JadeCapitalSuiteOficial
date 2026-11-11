using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Sentry;
using Sentry.AspNetCore;
using Sentry.Extensibility;

namespace JadeCapital.Host.Configuration;

public sealed record TelemetrySettings(string? SentryDsn, string? Release, Uri? OtlpEndpoint);

public static class TelemetryConfiguration
{
    public static TelemetrySettings Read(IConfiguration configuration) => new(
        Trim(configuration["Sentry:Dsn"]),
        Trim(configuration["Observability:Release"]),
        AbsoluteHttpUri(configuration["OpenTelemetry:Otlp:Endpoint"]));

    public static WebApplicationBuilder AddJadeCapitalTelemetry(
        this WebApplicationBuilder builder,
        ITransport? sentryTransport = null,
        BaseExporter<Activity>? traceExporter = null)
    {
        var settings = Read(builder.Configuration);

        if (settings.SentryDsn is not null)
        {
            builder.WebHost.UseSentry(options =>
            {
                ConfigureSentry(options, settings, builder.Environment.EnvironmentName);
                options.Transport = sentryTransport;
            });
        }

        if (settings.OtlpEndpoint is not null)
        {
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
                .WithTracing(tracing =>
                {
                    tracing.AddAspNetCoreInstrumentation(options => options.RecordException = true)
                        .AddProcessor(new SensitiveHttpAttributeProcessor());

                    if (traceExporter is null)
                        tracing.AddOtlpExporter(options => options.Endpoint = settings.OtlpEndpoint);
                    else
                        tracing.AddProcessor(new SimpleActivityExportProcessor(traceExporter));
                });
        }

        return builder;
    }

    public static void ConfigureSentry(
        SentryAspNetCoreOptions options,
        TelemetrySettings settings,
        string environment)
    {
        options.Dsn = settings.SentryDsn;
        options.Release = settings.Release;
        options.Environment = environment;
        options.AttachStacktrace = true;
        options.SendDefaultPii = false;
        options.IncludeActivityData = false;
        options.MaxRequestBodySize = RequestSize.None;
        options.DisableDiagnosticSourceIntegration();
        options.SetBeforeSend(ScrubSentryEvent);
    }

    private static SentryEvent ScrubSentryEvent(SentryEvent sentryEvent)
    {
        sentryEvent.ServerName = null;
        sentryEvent.User.Id = null;
        sentryEvent.User.Username = null;
        sentryEvent.User.Email = null;
        sentryEvent.User.IpAddress = null;
        sentryEvent.User.Other.Clear();
        if (sentryEvent.Request is not null)
        {
            sentryEvent.Request.QueryString = null;
            sentryEvent.Request.Cookies = null;
            sentryEvent.Request.Data = null;
            sentryEvent.Request.Headers.Clear();
        }

        return sentryEvent;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Uri? AbsoluteHttpUri(string? value)
    {
        var candidate = Trim(value);
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri
                : null;
    }

    private sealed class SensitiveHttpAttributeProcessor : BaseProcessor<Activity>
    {
        private static readonly string[] SensitiveTags =
        [
            "client.address",
            "enduser.id",
            "http.url",
            "http.user_agent",
            "network.peer.address",
            "server.address",
            "url.full",
            "url.query",
            "user.id",
            "user_agent.original"
        ];

        public override void OnEnd(Activity activity)
        {
            foreach (var tag in SensitiveTags)
                activity.SetTag(tag, null);
        }
    }
}
