using JadeCapital.Billing.Domain.Stripe;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Billing.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>billing.stripe_customers</c> (Wave 6, slice 6a.1).
/// Mirrors migration 0022. UNIQUE on (user_id, stripe_customer_id) — DB-level
/// idempotency guarantee.
/// </summary>
internal sealed class StripeCustomerConfiguration : IEntityTypeConfiguration<StripeCustomer>
{
    public void Configure(EntityTypeBuilder<StripeCustomer> b)
    {
        b.ToTable("stripe_customers");
        b.HasKey(c => c.Id);

        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.UserId).HasColumnName("user_id").IsRequired();
        b.Property(c => c.StripeCustomerId).HasColumnName("stripe_customer_id").HasMaxLength(64).IsRequired();
        b.Property(c => c.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        b.Property(c => c.DisplayName).HasColumnName("display_name").HasMaxLength(120);
        b.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        b.Ignore(c => c.DomainEvents);

        // UNIQUE (user_id) — one Stripe Customer per user. Mirrors migration 0022.
        b.HasIndex(c => c.UserId)
            .IsUnique()
            .HasDatabaseName("ux_stripe_customers_user");

        // UNIQUE (stripe_customer_id) — Stripe id is globally unique.
        b.HasIndex(c => c.StripeCustomerId)
            .IsUnique()
            .HasDatabaseName("ux_stripe_customers_stripe_id");
    }
}
