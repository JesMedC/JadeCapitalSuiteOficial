using JadeCapital.Billing.Domain.Stripe;

namespace JadeCapital.Billing.Application.Stripe;

/// <summary>
/// Repository abstraction for <see cref="StripeCustomer"/> (Wave 6, slice 6a.1).
/// Lives in Billing.Application; implementation lives in
/// <c>JadeCapital.Billing.Infrastructure.Persistence.StripeCustomerRepository</c>.
///
/// <para>
/// <b>Why in Application, not Infrastructure</b>: same precedent as
/// <c>ISubscriptionAdminRepository</c> — the abstraction belongs to the
/// application layer so handlers depend on contracts, not EF.
/// </para>
/// </summary>
public interface IStripeCustomerRepository
{
    /// <summary>Looks up a mapping by user id. Returns null if not found.</summary>
    Task<StripeCustomer?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Looks up a mapping by Stripe customer id. Returns null if not found.</summary>
    Task<StripeCustomer?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default);

    /// <summary>Adds a new mapping to the DbContext. Caller flushes via SaveChanges.</summary>
    Task AddAsync(StripeCustomer customer, CancellationToken ct = default);
}
