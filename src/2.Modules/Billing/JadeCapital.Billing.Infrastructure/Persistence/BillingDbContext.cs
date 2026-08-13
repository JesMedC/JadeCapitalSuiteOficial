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
public sealed class BillingDbContext : DbContext
{
    public BillingDbContext(DbContextOptions<BillingDbContext> options) : base(options) { }

    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionHistoryEntry> SubscriptionHistory => Set<SubscriptionHistoryEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("billing");
        modelBuilder.ApplyConfiguration(new PlanConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionHistoryConfiguration());
    }
}
