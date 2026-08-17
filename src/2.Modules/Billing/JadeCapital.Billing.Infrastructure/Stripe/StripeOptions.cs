namespace JadeCapital.Billing.Infrastructure.Stripe;

/// <summary>
/// Options for Stripe SDK integration (Wave 6, slice 6a.1).
///
/// <para>
/// <b>ApiKey</b>: when null or empty, DI swaps the real <c>StripeGateway</c>
/// for a <c>StubStripeGateway</c> (dev / CI without a Stripe key). This is
/// the Wave 5/5b.1 precedent (OllamaHttpClient falls back to null when no
/// provider is configured).
/// </para>
///
/// <para>
/// <b>ApiVersion</b>: pinned at construction time. The user-chosen value for
/// Wave 6 is <c>"2025-08-13"</c> (Stripe API version). The default value
/// here matches Stripe.net 47.0.0's library default.
/// </para>
///
/// <para>
/// <b>WebhookSecret</b>: the signing secret from the Stripe dashboard. The
/// webhook endpoint MUST reject any request whose signature does not verify
/// against this secret.
/// </para>
/// </summary>
public sealed class StripeOptions
{
    /// <summary>Configuration section name in <c>appsettings.json</c> / env vars.</summary>
    public const string SectionName = "Stripe";

    /// <summary>Stripe API key (<c>Stripe__ApiKey</c> env var). When null/empty the stub is used.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Stripe API version to pin to (default: <c>2025-08-13</c> per user choice).</summary>
    public string ApiVersion { get; set; } = "2025-08-13";

    /// <summary>Webhook signing secret (<c>Stripe__WebhookSecret</c> env var). Required for webhook verification.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Default Stripe Price id for self-service checkout (used by 6a.2).</summary>
    public string? DefaultPriceId { get; set; }

    /// <summary>Stripe Customer Portal configuration id (used by 6a.2).</summary>
    public string? CustomerPortalConfigurationId { get; set; }
}
