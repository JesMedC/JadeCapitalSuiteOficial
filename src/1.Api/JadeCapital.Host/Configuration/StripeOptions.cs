// Wave 10 slice 10.6 - Stripe options + validator at the host boundary.
// SPDX-License-Identifier: Proprietary
//
// WHY A SEPARATE CLASS WHEN Billing.Infrastructure/Stripe/StripeOptions EXISTS
//   The Billing-side options drives the Stripe.NET SDK construction (ApiKey
//   + WebhookSecret + DefaultPriceId, etc.). The Host-side class here is a
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
// LEGACY ALIAS (Wave 11 slice 11.4)
//   The canonical wire name is `SecretKey` — matching `Stripe__SecretKey`
//   in docker-compose. The Billing-side class still uses `ApiKey` (which
//   is the Stripe.NET SDK convention) and is untouched here. The
//   validator reads `Stripe:SecretKey` first and falls back to
//   `Stripe:ApiKey` for backwards compatibility with environments that
//   haven't migrated yet. Wave 11.4 ships the property rename + the
//   [Obsolete] alias bridge so the migration is forward-compat.

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

    private string _secretKey = string.Empty;

    /// <summary>
    /// Stripe API key (canonical name). Empty in dev means "use
    /// StubStripeGateway". Wave 11.4 — this property replaces the
    /// previous <c>ApiKey</c> name; the validator reads either one
    /// (the legacy alias is preserved via the [Obsolete] bridge
    /// property below).
    /// </summary>
    public string SecretKey
    {
        get => _secretKey;
        set => _secretKey = value ?? string.Empty;
    }

    /// <summary>
    /// Legacy alias for <see cref="SecretKey"/>. Wave 11.4 — kept for
    /// backwards compatibility with environments that bind
    /// <c>Stripe:ApiKey</c> via env-var <c>Stripe__ApiKey</c>. New
    /// deploys should bind <c>Stripe:SecretKey</c> directly. Marked
    /// [Obsolete] so the IDE + compiler flag the legacy usage; the
    /// property still serialises/deserialises so the bridge works.
    /// </summary>
    [Obsolete("Use SecretKey instead — StripeOptions.SecretKey matches the docker-compose Stripe__SecretKey convention. The ApiKey alias is preserved for backwards compatibility and will be removed in Wave 12.")]
    public string? ApiKey
    {
        get => _secretKey;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _secretKey = value;
            }
        }
    }

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

    /// <summary>
    /// Constructor takes <see cref="IConfiguration"/> so the validator can
    /// fall back to <c>Stripe:ApiKey</c> (legacy alias) when
    /// <c>Stripe:SecretKey</c> is not bound. See file-level comment for
    /// why the alias exists.
    /// </summary>
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

        // Wave 11.4 — prefer the canonical `Stripe:SecretKey`. The
        // legacy `Stripe:ApiKey` (via Obsolete bridge) still works,
        // so an un-migrated environment doesn't fail validation.
#pragma warning disable CS0618 // ApiKey is the legacy alias bridge
        var apiKey = !string.IsNullOrWhiteSpace(options.SecretKey)
            ? options.SecretKey
            : (options.ApiKey ?? string.Empty);
#pragma warning restore CS0618

        // Belt-and-braces: also probe the raw configuration in case
        // the binder did not pick up the section at all (e.g., legacy
        // compose with only `Stripe__ApiKey`).
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = _configuration["Stripe:SecretKey"]
                ?? _configuration["Stripe:ApiKey"]
                ?? string.Empty;
        }

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
