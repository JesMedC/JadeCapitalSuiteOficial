// Wave 10 slice 10.6 - Stripe options + validator at the host boundary.
// SPDX-License-Identifier: Proprietary
//
// WHY A SEPARATE CLASS WHEN Billing.Infrastructure/Stripe/StripeOptions EXISTS
//   The Billing-side options drives the Stripe.NET SDK construction. The Host-side class here is a
//   *validation gate*: it runs at host startup via ValidateOnStart so
//   misconfigurations fail fast instead of leaking through to runtime.
//
//   The class sits next to DockerSecretConfigurationProvider because both
//   are host-time configuration concerns (startup validation + secrets).
//   Keeping them in JadeCapital.Host.Configuration matches the "host owns
//   the deployment surface" boundary.
//
// WHAT THE VALIDATOR DOES
//   1. Production / Staging env: requires a sk_live_* key + a non-empty
//      WebhookSecret.
//   2. Development / other envs: allows empty key (falls back to
//      StubStripeGateway) or sk_test_* / sk_live_*. Rejects unrecognised
//      values so typos like "sk_text_..." don't quietly live in .env.
//
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace JadeCapital.Host.Configuration;

/// <summary>
/// Host-time Stripe options. Bound from the <c>Stripe</c> config section
/// and validated at startup via <see cref="StripeOptionsValidator"/>.
/// Independent from <c>JadeCapital.Billing.Infrastructure.Stripe.StripeOptions</c>
/// which drives the actual Stripe.NET SDK construction.
/// </summary>
public class StripeOptions
{
    /// <summary>Configuration section name (matches Billing-side for consistency).</summary>
    public const string SectionName = "Stripe";

    /// <summary>
    /// Stripe API key (canonical wire name). Bound from <c>Stripe:SecretKey</c>
    /// (env var <c>Stripe__SecretKey</c>). Empty in dev means "use
    /// StubStripeGateway".
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Webhook signing secret. Required in Production/Staging.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Default Stripe Price id for self-service checkout (used by 6a.2).</summary>
    public string DefaultPriceId { get; set; } = string.Empty;

    /// <summary>Stripe Customer Portal configuration id (used by 6a.2).</summary>
    public string CustomerPortalConfigurationId { get; set; } = string.Empty;
}

/// <summary>
/// Validates <see cref="StripeOptions"/> at host startup. Wired with
/// <c>ValidateOnStart()</c> in <c>Program.cs</c> near the
/// <c>AuditRetentionOptions</c> registration.
/// </summary>
public sealed class StripeOptionsValidator : IValidateOptions<StripeOptions>
{
    private readonly IConfiguration _configuration;

    public StripeOptionsValidator(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    /// <summary>
    /// Pure validation: produces <see cref="ValidateOptionsResult.Success"/>
    /// or <see cref="ValidateOptionsResult.Fail"/> with a list of human-
    /// readable failure reasons. The validator never throws so the
    /// <c>ValidateOnStart</c> hook can collect all failures and surface
    /// them as a single aggregated error at startup.
    /// </summary>
    public ValidateOptionsResult Validate(string? name, StripeOptions options)
    {
        var failures = new List<string>();

        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var isProdLike = env == "Production" || env == "Staging";

        // Probe the raw canonical key when the supplied options instance is empty.
        var apiKey = !string.IsNullOrWhiteSpace(options.SecretKey)
            ? options.SecretKey
            : (_configuration["Stripe:SecretKey"] ?? string.Empty);

        if (isProdLike)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || !apiKey.StartsWith("sk_live_", StringComparison.Ordinal))
                failures.Add("Stripe:SecretKey must be a live key (sk_live_*) in Production/Staging");
            if (string.IsNullOrWhiteSpace(options.WebhookSecret))
                failures.Add("Stripe:WebhookSecret must be set in Production/Staging");
        }
        else
        {
            // Development: empty key OK (stub fallback); explicit key must
            // match sk_test_* / sk_live_* prefix.
            if (!string.IsNullOrWhiteSpace(apiKey)
                && !apiKey.StartsWith("sk_test_", StringComparison.Ordinal)
                && !apiKey.StartsWith("sk_live_", StringComparison.Ordinal))
            {
                failures.Add("Stripe:SecretKey must be a test key (sk_test_*) or live key (sk_live_*)");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
