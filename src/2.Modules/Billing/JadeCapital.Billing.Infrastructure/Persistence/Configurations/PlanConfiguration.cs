using JadeCapital.Billing.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Billing.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <c>billing.plans</c> catalog table. Plans are
/// Admin-managed (slice 0f+); 0e wires the persistence shape only.
/// </summary>
internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> b)
    {
        b.ToTable("plans");
        b.HasKey(p => p.Id);

        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.Name).HasColumnName("name").HasMaxLength(80).IsRequired();

        // PlanCode is a complex VO: EF Core persists `Value` via the backing
        // field exposed through the navigation. The unique index is on the
        // value column.
        b.OwnsOne(p => p.Code, cb =>
        {
            cb.Property(c => c.Value).HasColumnName("code").HasMaxLength(32).IsRequired();
            cb.HasIndex(c => c.Value).IsUnique().HasDatabaseName("ux_plans_code");
        });

        // Money: amount + currency code, both required, mapped directly to
        // NUMERIC(24,8) and a 3-letter code. Owned value object's currency
        // property is derived (Currency.FromTrusted) and EF ignores it.
        b.OwnsOne(p => p.MonthlyPrice, mb =>
        {
            mb.Property(m => m.Amount).HasColumnName("monthly_price").HasColumnType("numeric(24,8)").IsRequired();
            mb.Property(m => m.CurrencyCode).HasColumnName("monthly_price_currency").HasMaxLength(3).IsRequired();
        });

        b.Property(p => p.IsEligibleForSelfService).HasColumnName("is_eligible_for_self_service").IsRequired();
        b.Property(p => p.IsDeprecated).HasColumnName("is_deprecated").IsRequired();
        b.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(p => p.UpdatedAt).HasColumnName("updated_at");
    }
}
