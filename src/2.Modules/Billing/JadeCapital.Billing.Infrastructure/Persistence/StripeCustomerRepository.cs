using JadeCapital.Billing.Application.Stripe;
using JadeCapital.Billing.Domain.Stripe;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Billing.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IStripeCustomerRepository"/> (Wave 6,
/// slice 6a.1).
/// </summary>
public sealed class StripeCustomerRepository : IStripeCustomerRepository
{
    private readonly BillingDbContext _db;

    public StripeCustomerRepository(BillingDbContext db)
    {
        _db = db;
    }

    public Task<StripeCustomer?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => _db.StripeCustomers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);

    public Task<StripeCustomer?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
        => _db.StripeCustomers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.StripeCustomerId == stripeCustomerId, ct);

    public async Task AddAsync(StripeCustomer customer, CancellationToken ct = default)
    {
        await _db.StripeCustomers.AddAsync(customer, ct);
    }
}
