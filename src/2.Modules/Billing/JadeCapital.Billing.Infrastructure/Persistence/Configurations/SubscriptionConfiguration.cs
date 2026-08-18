using JadeCapital.Billing.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Billing.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>billing.subscriptions</c>. Tracks:
/// - UNIQUE (user_id) — at most one subscription per user.
/// - (status, updated_at DESC) — Admin list queries by status with newest-first tie-break.
/// - Version is the optimistic-concurrency token bumped on every mutation.
/// SubscriptionPeriod is an embedded VO; SubscriptionHistoryEntry is exposed
/// via a private navigation that EF materialises through the backing field.
/// </summary>
internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> b)
    {
        b.ToTable("subscriptions");
        b.HasKey(s => s.Id);

        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.UserId).HasColumnName("user_id").IsRequired();

        // PlanCode is an embedded VO mapped to the `plan_code` column.
        b.OwnsOne(s => s.PlanCode, cb =>
        {
            cb.Property(c => c.Value).HasColumnName("plan_code").HasMaxLength(32).IsRequired();
        });

        b.Property(s => s.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(s => s.TrialEndsAt).HasColumnName("trial_ends_at");

        // CurrentPeriod is an embedded VO; both columns are part of the
        // subscription aggregate's own row. Validation lives in the VO
        // constructor.
        b.OwnsOne(s => s.CurrentPeriod, pb =>
        {
            pb.Property(p => p.Start).HasColumnName("current_period_start").IsRequired();
            pb.Property(p => p.End).HasColumnName("current_period_end").IsRequired();
        });

        b.Property(s => s.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();
        b.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        // Wave 6a.2: nullable Stripe subscription id. Populated by the
        // webhook handler on the first customer.subscription.* event.
        // UNIQUE partial index lives at the SQL level (migration 0023); the
        // EF model doesn't need to declare it because we never query by it
        // through EF — the only reader is SubscriptionAdminRepository.
        b.Property(s => s.StripeSubscriptionId)
            .HasColumnName("stripe_subscription_id")
            .HasMaxLength(64);

        // History is a computed projection (newest-first ordering on the
        // backing field). EF must NOT auto-discover it as a collection nav
        // (it would clash with the private _history navigation), so we Ignore
        // the public property and expose the private list via the HasMany
        // navigation named "_history" with field access mode.
        b.Ignore(s => s.History);
        b.HasMany<SubscriptionHistoryEntry>("_history")
            .WithOne()
            .HasForeignKey(h => h.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation("_history")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(s => s.DomainEvents);

        // UNIQUE (user_id) — enforces one active subscription per user at the
        // DB level. Mirrored in 0007_BillingSubscriptions.sql.
        b.HasIndex(s => s.UserId)
            .IsUnique()
            .HasDatabaseName("ux_subscriptions_user");

        // (status, updated_at DESC) — Admin list/search by status, newest first.
        b.HasIndex(s => new { s.Status, s.UpdatedAt })
            .HasDatabaseName("ix_subscriptions_status_updated_at_desc")
            .IsDescending(false, true);

        // `version` index is implicit (it's part of the PK-row lifetime). Add
        // an explicit one if/when concurrent-update probes become a hot path.
    }
}
