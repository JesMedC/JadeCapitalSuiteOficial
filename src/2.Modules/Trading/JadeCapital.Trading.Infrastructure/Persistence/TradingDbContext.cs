using JadeCapital.Trading.Domain.Accounts;
using JadeCapital.Trading.Domain.Instruments;
using JadeCapital.Trading.Domain.Journal;
using JadeCapital.Trading.Domain.PreTradeChecklists;
using JadeCapital.Trading.Domain.Strategies;
using JadeCapital.Trading.Domain.Trades;
using JadeCapital.Trading.Infrastructure.Persistence.Configurations;
using JadeCapital.Trading.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence;

/// <summary>
/// DbContext del modulo Trading. Esquema dedicado "trading".
/// Tablas: trading.accounts, trading.instruments, trading.trades,
/// trading.pre_trade_checklists, trading.trade_reviews,
/// trading.trade_attachments, trading.journal_entries.
/// </summary>
public sealed class TradingDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public TradingDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<TradingDbContext> options)
        : base(options) { }

    public Microsoft.EntityFrameworkCore.DbSet<Trade> Trades => Set<Trade>();
    public Microsoft.EntityFrameworkCore.DbSet<Account> Accounts => Set<Account>();
    public Microsoft.EntityFrameworkCore.DbSet<Instrument> Instruments => Set<Instrument>();
    public Microsoft.EntityFrameworkCore.DbSet<PreTradeChecklist> PreTradeChecklists => Set<PreTradeChecklist>();
    // Slice 1d.1 — post-trade reviews + attachments.
    public Microsoft.EntityFrameworkCore.DbSet<JadeCapital.Trading.Domain.TradeReviews.TradeReview> TradeReviews => Set<JadeCapital.Trading.Domain.TradeReviews.TradeReview>();
    public Microsoft.EntityFrameworkCore.DbSet<JadeCapital.Trading.Domain.TradeAttachments.TradeAttachment> TradeAttachments => Set<JadeCapital.Trading.Domain.TradeAttachments.TradeAttachment>();
    // Slice 2a.1 — daily journal entries.
    public Microsoft.EntityFrameworkCore.DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    // Slice 3a — trader strategies (named setups + analytics).
    public Microsoft.EntityFrameworkCore.DbSet<Strategy> Strategies => Set<Strategy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("trading");
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new InstrumentConfiguration());
        modelBuilder.ApplyConfiguration(new TradeConfiguration());
        modelBuilder.ApplyConfiguration(new PreTradeChecklistConfiguration());
        modelBuilder.ApplyConfiguration(new TradeReviewConfiguration());
        modelBuilder.ApplyConfiguration(new TradeAttachmentConfiguration());
        modelBuilder.ApplyConfiguration(new JournalEntryConfiguration());
        modelBuilder.ApplyConfiguration(new StrategyConfiguration());
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
        b.Property(t => t.AccountId).HasColumnName("account_id").IsRequired();
        b.Property(t => t.InstrumentId).HasColumnName("instrument_id").IsRequired();

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

        // Slice 2c — MFE/MAE columns (migration 0014). Nullable: open trades
        // no tienen MFE/MAE computados. La invariante de signo (MFE >= 0,
        // MAE <= 0) vive en el dominio (MfeMaeCalculator + ApplyMfeMae);
        // la DB no enforce CHECK para mantener la migracion additive-only.
        b.Property(t => t.MfeAmount).HasColumnName("mfe_amount").HasColumnType("numeric(24,8)");
        b.Property(t => t.MaeAmount).HasColumnName("mae_amount").HasColumnType("numeric(24,8)");
        b.Property(t => t.MfeCurrency).HasColumnName("mfe_currency").HasMaxLength(3).IsFixedLength();
        b.Property(t => t.MaeCurrency).HasColumnName("mae_currency").HasMaxLength(3).IsFixedLength();

        // Slice 3a — Strategy FK additive nullable (migration 0015a).
        // La shadow navigation HasOne<Strategy>().WithMany() mapea la FK
        // logica sin requerir navigation property en el aggregate Trade.
        // ON DELETE SET NULL en la DB; la DB hace el SET NULL si Wave 4+
        // algun dia hard-delete una strategy.
        b.Property(t => t.StrategyId).HasColumnName("strategy_id").IsRequired(false);
        b.HasOne<Strategy>().WithMany().HasForeignKey(t => t.StrategyId)
            .OnDelete(DeleteBehavior.SetNull);

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

        // FKs via shadow navigation: Account y Instrument son referencias de
        // tabla, no navegaciones del modelo de dominio. El HasOne sin WithMany
        // deja que EF entienda la relacion sin forzar una propiedad nav.
        b.HasOne<Account>().WithMany().HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Instrument>().WithMany().HasForeignKey(t => t.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indices (alineados con la migracion SQL para que EF no genere DROP/CREATE
        // duplicado si en algun momento se usa dotnet-ef migrations add).
        b.HasIndex(t => new { t.UserId, t.OpenedAt })
            .HasDatabaseName("ix_trades_user_opened_at")
            .IsDescending(false, true);
        b.HasIndex(t => new { t.UserId, t.Status }).HasDatabaseName("ix_trades_user_status");
        b.HasIndex(t => new { t.UserId, t.Symbol }).HasDatabaseName("ix_trades_user_symbol");
        b.HasIndex(t => new { t.AccountId, t.OpenedAt })
            .HasDatabaseName("ix_trades_account_opened_at")
            .IsDescending(false, true);
        b.HasIndex(t => new { t.InstrumentId, t.OpenedAt })
            .HasDatabaseName("ix_trades_instrument_opened_at")
            .IsDescending(false, true);
        b.HasIndex(t => t.OpenedAt).HasDatabaseName("ix_trades_opened_at").IsDescending(true);
    }
}
