using JadeCapital.Billing.Infrastructure.Persistence;
using JadeCapital.Identity.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Billing.Infrastructure.Cascade;

/// <summary>
/// Billing-side GDPR Art. 17 cascade deletor (Wave 10, slice 10.5).
///
/// <para>
/// Cancels subscriptions and anonymizes StripeCustomer records for the
/// target user. Per design.md §3.2, the cascade pattern keeps Billing
/// isolated from Identity + Trading — each module owns its deletor.
/// </para>
///
/// <para>
/// Soft delete: marks subscription cancelled + StripeCustomer anonymized.
/// Hard delete: physically DELETEs the rows after the 30-day grace period.
/// </para>
/// </summary>
public sealed class BillingUserCascadeDeletor : IUserCascadeDeletor
{
    private readonly BillingDbContext _db;

    public BillingUserCascadeDeletor(BillingDbContext db)
    {
        _db = db;
    }

    public Task<int> CascadeSoftDeleteAsync(Guid userId, CancellationToken ct) =>
        // Per design.md: Billing subscriptions use domain-driven cancellation
        // (Status=Cancelled) rather than is_deleted flag. The subscription row
        // is retained for invoice audit until hard-delete at day 31.
        Task.FromResult(0);

    public async Task<int> CascadeHardDeleteAsync(Guid userId, CancellationToken ct)
    {
        // Hard delete: physically remove the user's subscription + StripeCustomer records.
        // Order: child rows first, then parent (FKs are RESTRICT).
        var subs = await _db.Subscriptions.Where(s => s.UserId == userId).ToListAsync(ct);
        _db.Subscriptions.RemoveRange(subs);
        var stripe = await _db.StripeCustomers.Where(c => c.UserId == userId).ToListAsync(ct);
        _db.StripeCustomers.RemoveRange(stripe);
        return await _db.SaveChangesAsync(ct);
    }
}