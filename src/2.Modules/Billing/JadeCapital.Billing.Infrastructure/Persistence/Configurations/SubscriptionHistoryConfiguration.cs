using JadeCapital.Billing.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Billing.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the append-only <c>billing.subscription_history</c>
/// table. Index ordering is (subscription_id, occurred_at DESC, id DESC) —
/// matches the <see cref="SubscriptionHistoryEntry.OrderNewestFirst"/>
/// projection exactly so the DB can serve the detail view newest-first
/// without an in-memory sort. Mirrored in 0007_BillingSubscriptions.sql.
/// </summary>
internal sealed class SubscriptionHistoryConfiguration : IEntityTypeConfiguration<SubscriptionHistoryEntry>
{
    public void Configure(EntityTypeBuilder<SubscriptionHistoryEntry> b)
    {
        b.ToTable("subscription_history");
        b.HasKey(h => h.Id);

        b.Property(h => h.Id).HasColumnName("id");
        b.Property(h => h.SubscriptionId).HasColumnName("subscription_id").IsRequired();
        b.Property(h => h.Action).HasColumnName("action").HasConversion<string>().HasMaxLength(24).IsRequired();

        // PlanCode VO embedded: both prior and resulting codes are columns on
        // the history row to support the "no-op rejection" rule and detail
        // UI without re-aggregating from the parent.
        b.OwnsOne(h => h.PriorPlanCode, cb =>
        {
            cb.Property(c => c.Value).HasColumnName("prior_plan_code").HasMaxLength(32).IsRequired();
        });
        b.OwnsOne(h => h.ResultingPlanCode, cb =>
        {
            cb.Property(c => c.Value).HasColumnName("resulting_plan_code").HasMaxLength(32).IsRequired();
        });

        b.Property(h => h.PriorStatus).HasColumnName("prior_status").HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(h => h.ResultingStatus).HasColumnName("resulting_status").HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(h => h.Actor).HasColumnName("actor").HasMaxLength(120).IsRequired();
        b.Property(h => h.OccurredAt).HasColumnName("occurred_at").IsRequired();
        b.Property(h => h.Version).HasColumnName("version").IsRequired();
        b.Property(h => h.Reason).HasColumnName("reason").HasMaxLength(500);
        b.Property(h => h.PriorTrialEndsAt).HasColumnName("prior_trial_ends_at");
        b.Property(h => h.NewTrialEndsAt).HasColumnName("new_trial_ends_at");
        b.Property(h => h.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(h => h.UpdatedAt).HasColumnName("updated_at");

        // Append-only: no UPDATE permission enforced here, but the file
        // declares the table to make the intent clear. The DB-level rule is
        // out of scope for 0e.

        // Per-subscription newest-first: filter predicate narrows the index
        // to subscriptions owned by a single customer at a time.
        b.HasIndex(h => new { h.SubscriptionId, h.OccurredAt, h.Id })
            .HasDatabaseName("ix_subscription_history_subscription_occurred_id_desc")
            .IsDescending(false, true, true);
    }
}
