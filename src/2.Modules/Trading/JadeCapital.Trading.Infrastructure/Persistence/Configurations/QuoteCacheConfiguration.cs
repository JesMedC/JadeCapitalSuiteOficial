using JadeCapital.Trading.Domain.MarketData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence.Configurations;

internal sealed class QuoteCacheConfiguration : IEntityTypeConfiguration<QuoteCacheEntry>
{
    public void Configure(EntityTypeBuilder<QuoteCacheEntry> b)
    {
        b.ToTable("quotes_cache");
        b.HasKey(q => q.Symbol);
        b.Property(q => q.Symbol).HasColumnName("symbol").HasMaxLength(20).IsRequired();
        b.Property(q => q.Bid).HasColumnName("bid").HasColumnType("numeric(18,8)").IsRequired();
        b.Property(q => q.Ask).HasColumnName("ask").HasColumnType("numeric(18,8)").IsRequired();
        b.Property(q => q.Spread).HasColumnName("spread").HasColumnType("numeric(18,8)").IsRequired();
        b.Property(q => q.Volume24h).HasColumnName("volume_24h").HasColumnType("numeric(24,8)").IsRequired();
        b.Property(q => q.Source).HasColumnName("source").HasConversion<byte>().IsRequired();
        b.Property(q => q.CachedAt).HasColumnName("cached_at").IsRequired();
        b.Ignore(q => q.Id);
        b.Ignore(q => q.CreatedAt);
        b.Ignore(q => q.UpdatedAt);
    }
}
