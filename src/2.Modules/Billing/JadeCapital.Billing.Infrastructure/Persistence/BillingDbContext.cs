using JadeCapital.Billing.Domain.Stripe;
using JadeCapital.Billing.Domain.Subscriptions;
using JadeCapital.Billing.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace JadeCapital.Billing.Infrastructure.Persistence;

/// <summary>
/// DbContext for the Billing bounded context. Schema <c>billing</c>. Owns
/// the Plans catalog, the Subscription aggregate, and the append-only
/// subscription history. Slice 0f will add repositories and the handler
/// layer; 0e wires the persistence shape and indexes only.
/// </summary>
public class BillingDbContext : DbContext
{
    public BillingDbContext(DbContextOptions<BillingDbContext> options) : base(options) { }

    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionHistoryEntry> SubscriptionHistory => Set<SubscriptionHistoryEntry>();

    /// <summary>Wave 6a.1: Stripe Customer mapping per user.</summary>
    public DbSet<StripeCustomer> StripeCustomers => Set<StripeCustomer>();

    /// <summary>Wave 6a.2: append-only log of received Stripe webhook events.</summary>
    public DbSet<StripeWebhookEvent> StripeWebhookEvents => Set<StripeWebhookEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("billing");
        modelBuilder.ApplyConfiguration(new PlanConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new StripeCustomerConfiguration());
        modelBuilder.ApplyConfiguration(new StripeWebhookEventConfiguration());
    }
}
