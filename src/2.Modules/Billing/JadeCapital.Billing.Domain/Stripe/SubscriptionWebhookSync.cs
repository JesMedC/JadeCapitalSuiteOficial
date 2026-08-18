using JadeCapital.Billing.Domain.Common;
using JadeCapital.Billing.Domain.Subscriptions;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Stripe;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Billing.Domain.Stripe;

/// <summary>
/// Read-model translator that maps a Stripe subscription DTO onto a local
/// <see cref="Subscription"/> aggregate (Wave 6, slice 6a.2).
///
/// <para>
/// <b>Why a static translator</b>: the mapping rule (Stripe's
/// free-form <c>status</c> string → internal
/// <see cref="SubscriptionStatus"/> enum) is a domain invariant because
/// status transitions affect billing, entitlement, and audit. Putting the
/// rule in a dedicated static class lets the domain tests pin the contract
/// without going through the handler / DI layer.
/// </para>
///
/// <para>
/// <b>State mutation</b>: the translator is NOT a mutator itself — it
/// delegates to <see cref="Subscription.SyncFromStripe"/>. The translator
/// owns the status mapping; the aggregate owns the state transition +
/// history append + version bump.
/// </para>
///
/// <para>
/// <b>Status mapping</b> (per the design):
/// <list type="bullet">
///   <item><c>"active"</c> → <see cref="SubscriptionStatus.Active"/></item>
///   <item><c>"trialing"</c> → <see cref="SubscriptionStatus.Trial"/></item>
///   <item><c>"canceled"</c> → <see cref="SubscriptionStatus.Cancelled"/></item>
///   <item><c>"past_due"</c> → <see cref="SubscriptionStatus.PastDue"/></item>
///   <item><c>"unpaid"</c> → <see cref="SubscriptionStatus.PastDue"/>
///   (defensive — unpaid is a separate Stripe state but functionally
///   the same outcome as past_due: payment failed, not yet cancelled)</item>
///   <item>anything else → Result.Failure
///   <c>validation.stripe.subscription.unknown_status</c></item>
/// </list>
/// </para>
/// </summary>
public static class SubscriptionWebhookSync
{
    /// <summary>
    /// Applies the Stripe DTO to the local subscription. Maps the status
    /// first; on a known status, calls
    /// <see cref="Subscription.SyncFromStripe"/> to mutate the aggregate.
    /// </summary>
    public static Result ApplyToSubscription(
        Subscription subscription,
        StripeSubscriptionDto stripeSub,
        IClock clock)
    {
        if (subscription is null)
            return Result.Failure(
                Error.Validation("validation.subscription.required",
                    "Subscription is required."));

        var statusResult = MapStripeStatusToInternal(stripeSub?.Status);
        if (statusResult.IsFailure)
            return Result.Failure(statusResult.Error);

        return subscription.SyncFromStripe(stripeSub!, statusResult.Value, clock.UtcNow);
    }

    /// <summary>
    /// Pure mapping. Public for tests + future callers (e.g. the
    /// <c>GetSubscriptionHandler</c> in slice 6b.1) that need the mapping
    /// without mutating an aggregate.
    /// </summary>
    public static Result<SubscriptionStatus> MapStripeStatusToInternal(string? stripeStatus)
    {
        if (string.IsNullOrWhiteSpace(stripeStatus))
            return Result.Failure<SubscriptionStatus>(
                Error.Validation("stripe.subscription.unknown_status",
                    "Stripe subscription status is required."));

        return stripeStatus.ToLowerInvariant() switch
        {
            "active"     => Result.Success(SubscriptionStatus.Active),
            "trialing"   => Result.Success(SubscriptionStatus.Trial),
            "canceled" or "cancelled" => Result.Success(SubscriptionStatus.Cancelled),
            "past_due"   => Result.Success(SubscriptionStatus.PastDue),
            "unpaid"     => Result.Success(SubscriptionStatus.PastDue),
            _ => Result.Failure<SubscriptionStatus>(
                Error.Validation("stripe.subscription.unknown_status",
                    $"Stripe subscription status '{stripeStatus}' is not recognised."))
        };
    }
}
