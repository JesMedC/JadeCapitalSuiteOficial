using JadeCapital.Trading.Domain.Scanner;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence.Configurations;

internal sealed class ScannerFilterConfiguration : IEntityTypeConfiguration<ScannerFilter>
{
    public void Configure(EntityTypeBuilder<ScannerFilter> b)
    {
        b.ToTable("scanner_filters");
        b.HasKey(f => f.Id);

        b.Property(f => f.Id).HasColumnName("id");
        b.Property(f => f.UserId).HasColumnName("user_id").IsRequired();
        b.Property(f => f.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        b.Property(f => f.MinSpread).HasColumnName("min_spread");
        b.Property(f => f.MaxSpread).HasColumnName("max_spread");
        b.Property(f => f.MinVolume).HasColumnName("min_volume");
        b.Property(f => f.MinRiskReward).HasColumnName("min_risk_reward");
        b.Property(f => f.VolatilityWindow).HasColumnName("volatility_window").HasConversion<byte>().IsRequired();
        b.Property(f => f.ActiveHours).HasColumnName("active_hours");
        b.Property(f => f.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(f => f.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(f => f.UpdatedAt).HasColumnName("updated_at");
    }
}
