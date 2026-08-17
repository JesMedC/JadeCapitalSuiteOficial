using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Billing.Domain.Stripe;

/// <summary>
/// Error catalog for the <see cref="StripeCustomer"/> aggregate (Wave 6,
/// slice 6a.1). Codes are namespaced under <c>stripe.customer.</c> for
/// consistent routing.
/// </summary>
public static class StripeCustomerErrors
{
    public static readonly Error IdRequired =
        Error.Validation("stripe.customer.id_required", "StripeCustomer identifier is required.");

    public static readonly Error UserIdRequired =
        Error.Validation("stripe.customer.user_id_required", "User identifier is required.");

    public static readonly Error StripeCustomerIdRequired =
        Error.Validation("stripe.customer.stripe_customer_id_required", "Stripe customer id is required.");

    public static readonly Error InvalidStripeCustomerId =
        Error.Validation("stripe.customer.invalid_stripe_customer_id",
            "Stripe customer id must start with 'cus_'.");

    public static readonly Error EmailRequired =
        Error.Validation("stripe.customer.email_required", "Email is required.");
}
