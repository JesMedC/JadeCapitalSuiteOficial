using JadeCapital.Trading.Domain.Instruments;
using JadeCapital.Trading.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeo de <see cref="Instrument"/> a la tabla trading.instruments.
///
/// Instrument es global (no tiene FK a users). El Symbol se persiste via
/// converter + comparer, igual que en Trade.
/// </summary>
public sealed class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> b)
    {
        b.ToTable("instruments");
        b.HasKey(i => i.Id);

        b.Property(i => i.Id).HasColumnName("id");

        var symbolConv = new SymbolConverter();
        var symbolComp = new SymbolComparer();
        b.Property(i => i.Symbol).HasColumnName("symbol").HasMaxLength(20).IsRequired()
            .HasConversion(symbolConv, symbolComp);

        // AssetClasses es un [Flags] enum que se mapea a SMALLINT (bitmask). EF Core
        // serializa el underlying value (short) automaticamente con HasConversion<short>().
        b.Property(i => i.AssetClasses).HasColumnName("asset_class").HasConversion<short>().IsRequired();
        b.Property(i => i.ContractSize).HasColumnName("contract_size").HasColumnType("numeric(24,8)").IsRequired();
        b.Property(i => i.DecimalPlaces).HasColumnName("decimal_places").IsRequired();
        b.Property(i => i.PipValue).HasColumnName("pip_value").HasColumnType("numeric(24,8)").IsRequired();
        b.Property(i => i.PayoutPercent).HasColumnName("payout_percent").HasColumnType("numeric(5,4)").IsRequired();
        b.Property(i => i.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(i => i.UpdatedAt).HasColumnName("updated_at");

        // Instrument NO es AggregateRoot — no tiene DomainEvents que ignorar.

        b.HasIndex(i => i.Symbol).IsUnique().HasDatabaseName("ux_instruments_symbol");
        b.HasIndex(i => i.AssetClasses).HasDatabaseName("ix_instruments_asset_class");
    }
}
