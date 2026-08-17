using JadeCapital.Billing.Domain.Common;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Billing.Domain.Stripe;

/// <summary>
/// Aggregate root mapping a user to a Stripe Customer (Wave 6, slice 6a.1).
///
/// <para>
/// <b>Invariants</b>:
/// <list type="bullet">
///   <item><see cref="UserId"/> MUST be non-empty</item>
///   <item><see cref="StripeCustomerId"/> MUST be non-empty and start with <c>cus_</c></item>
///   <item><see cref="Email"/> MUST be non-empty</item>
///   <item>Aggregate is immutable after <see cref="Create"/> — no mutators</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Idempotency</b>: the repository guarantees uniqueness on
/// <c>(user_id, stripe_customer_id)</c>. A second
/// <see cref="Create"/> call for the same <see cref="UserId"/> is rejected
/// upstream by the gateway (which first queries <c>customers.list</c>) —
/// this aggregate's <c>Create</c> only validates inputs.
/// </para>
/// </summary>
public sealed class StripeCustomer : AggregateRoot<Guid>
{
    public Guid UserId { get; private set; }
    public string StripeCustomerId { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string? DisplayName { get; private set; }

    // EF materialization.
    private StripeCustomer() { }

    private StripeCustomer(
        Guid id,
        Guid userId,
        string stripeCustomerId,
        string email,
        string? displayName,
        DateTimeOffset createdAt) : base(id)
    {
        UserId = userId;
        StripeCustomerId = stripeCustomerId;
        Email = email;
        DisplayName = displayName;
        SetCreatedAt(createdAt);
    }

    /// <summary>
    /// Factory. Validates inputs and returns <c>Result.Success</c> with the
    /// new aggregate, or <c>Result.Failure</c> with the relevant
    /// <see cref="StripeCustomerErrors"/>.
    /// </summary>
    public static Result<StripeCustomer> Create(
        Guid id,
        Guid userId,
        string stripeCustomerId,
        string email,
        string? displayName,
        IClock clock)
    {
        if (id == Guid.Empty)
            return Result.Failure<StripeCustomer>(StripeCustomerErrors.IdRequired);
        if (userId == Guid.Empty)
            return Result.Failure<StripeCustomer>(StripeCustomerErrors.UserIdRequired);
        if (string.IsNullOrWhiteSpace(stripeCustomerId))
            return Result.Failure<StripeCustomer>(StripeCustomerErrors.StripeCustomerIdRequired);
        if (!stripeCustomerId.StartsWith("cus_", StringComparison.Ordinal))
            return Result.Failure<StripeCustomer>(StripeCustomerErrors.InvalidStripeCustomerId);
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure<StripeCustomer>(StripeCustomerErrors.EmailRequired);

        var now = clock.UtcNow;
        return Result.Success(new StripeCustomer(
            id, userId, stripeCustomerId, email, displayName, now));
    }
}
