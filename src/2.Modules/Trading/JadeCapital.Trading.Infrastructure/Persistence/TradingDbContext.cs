using JadeCapital.Trading.Domain.Trades;
using JadeCapital.Trading.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence;

/// <summary>
/// DbContext del modulo Trading. Esquema dedicado "trading".
/// Tablas: trading.trades (Sprint 1 Fase 1C).
/// </summary>
public sealed class TradingDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public TradingDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<TradingDbContext> options)
        : base(options) { }

    public Microsoft.EntityFrameworkCore.DbSet<Trade> Trades => Set<Trade>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("trading");
        modelBuilder.ApplyConfiguration(new TradeConfiguration());
    }
}

internal sealed class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> b)
    {
        b.ToTable("trades");
        b.HasKey(t => t.Id);

        b.Property(t => t.Id).HasColumnName("id");
        b.Property(t => t.UserId).HasColumnName("user_id").IsRequired();

        // Symbol value object — se persiste como VARCHAR via value converter.
        var symbolConv = new SymbolConverter();
        var symbolComp = new SymbolComparer();
        b.Property(t => t.Symbol).HasColumnName("symbol").HasMaxLength(20).IsRequired()
            .HasConversion(symbolConv, symbolComp);

        b.Property(t => t.AssetClass).HasColumnName("asset_class").HasConversion<short>().IsRequired();
        b.Property(t => t.Direction).HasColumnName("direction").HasConversion<short>().IsRequired();
        b.Property(t => t.Status).HasColumnName("status").HasConversion<short>().IsRequired();
        b.Property(t => t.Strategy).HasColumnName("strategy").HasMaxLength(80);
        b.Property(t => t.Notes).HasColumnName("notes").HasMaxLength(2000);
        b.Property(t => t.OpenedAt).HasColumnName("opened_at").IsRequired();
        b.Property(t => t.ClosedAt).HasColumnName("closed_at");
        b.Property(t => t.AccountCurrency).HasColumnName("account_currency").HasMaxLength(3).IsRequired();
        b.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        // Money value objects via OwnsOne — Volume, EntryPrice son required.
        // ExitPrice y PnL son nullable y la columna queda NULL cuando el
        // Money es null. Money se serializa via CurrencyCode (string) + Amount
        // (decimal) usando su constructor `(decimal, string)`.
        b.OwnsOne(t => t.Volume, m =>
        {
            m.Property(x => x.Amount).HasColumnName("volume_amount").HasColumnType("numeric(24,8)").IsRequired();
            m.Property(x => x.CurrencyCode).HasColumnName("volume_currency").HasMaxLength(3).IsRequired();
        });

        b.OwnsOne(t => t.EntryPrice, m =>
        {
            m.Property(x => x.Amount).HasColumnName("entry_price_amount").HasColumnType("numeric(24,8)").IsRequired();
            m.Property(x => x.CurrencyCode).HasColumnName("entry_price_currency").HasMaxLength(3).IsRequired();
        });

        b.OwnsOne(t => t.ExitPrice, m =>
        {
            m.Property(x => x.Amount).HasColumnName("exit_price_amount").HasColumnType("numeric(24,8)");
            m.Property(x => x.CurrencyCode).HasColumnName("exit_price_currency").HasMaxLength(3);
        });

        b.OwnsOne(t => t.PnL, m =>
        {
            m.Property(x => x.Amount).HasColumnName("pnl_amount").HasColumnType("numeric(24,8)");
            m.Property(x => x.CurrencyCode).HasColumnName("pnl_currency").HasMaxLength(3);
        });

        // DomainEvents no se persiste.
        b.Ignore(t => t.DomainEvents);

        // Indices (alineados con la migracion SQL para que EF no genere DROP/CREATE
        // duplicado si en algun momento se usa dotnet-ef migrations add).
        b.HasIndex(t => new { t.UserId, t.OpenedAt })
            .HasDatabaseName("ix_trades_user_opened_at")
            .IsDescending(false, true);
        b.HasIndex(t => new { t.UserId, t.Status }).HasDatabaseName("ix_trades_user_status");
        b.HasIndex(t => new { t.UserId, t.Symbol }).HasDatabaseName("ix_trades_user_symbol");
        b.HasIndex(t => t.OpenedAt).HasDatabaseName("ix_trades_opened_at").IsDescending(true);
    }
}
