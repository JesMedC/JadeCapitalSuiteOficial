using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Shared.Kernel.Stripe;

/// <summary>
/// Stripe-specific <see cref="Error"/> wrapper (Wave 6, slice 6a.1).
///
/// <para>
/// Mirrors the implicit-conversion-to-Error pattern used elsewhere (e.g.
/// <c>BillingDomainErrors</c>). Handlers can write
/// <c>Result.Failure&lt;T&gt;(new StripeError(...))</c> and the value flows
/// through the standard Result pipeline via the implicit conversion.
/// </para>
///
/// <para>
/// <b>Code prefix</b>: every <see cref="Code"/> MUST start with
/// <c>stripe.</c> so the DomainGuard can route Stripe-specific failures to
/// the right HTTP status code (typically 502 Bad Gateway for upstream
/// failures, 401 for signature errors, 422 for validation).
/// </para>
/// </summary>
public sealed record StripeError(string Code, string Message)
{
    /// <summary>Implicit conversion to <see cref="Error"/> so the value flows
    /// through the standard <c>Result.Failure&lt;T&gt;(error)</c> pipeline.</summary>
    public static implicit operator Error(StripeError stripeError)
        => new(stripeError.Code, stripeError.Message);

    /// <summary>Implicit conversion to <see cref="Result"/>.</summary>
    public static implicit operator Result(StripeError stripeError)
        => Result.Failure(new Error(stripeError.Code, stripeError.Message));

    public static StripeError Api(string message) => new("stripe.api_error", message);
    public static StripeError Authentication(string message) => new("stripe.authentication_error", message);
    public static StripeError RateLimit(string message) => new("stripe.rate_limit_error", message);
    public static StripeError InvalidRequest(string message) => new("stripe.invalid_request", message);
    public static StripeError Internal(string message) => new("stripe.internal_error", message);
    public static StripeError Unavailable(string message) => new("stripe.unavailable", message);
    public static StripeError SignatureInvalid(string message) => new("stripe.signature_invalid", message);
    public static StripeError SignatureMissing(string message) => new("stripe.signature_missing", message);
    public static StripeError Timeout(string message) => new("stripe.timeout", message);
}
