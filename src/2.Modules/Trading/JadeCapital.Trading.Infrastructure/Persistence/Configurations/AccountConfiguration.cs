using JadeCapital.Trading.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeo de <see cref="Account"/> a la tabla trading.accounts.
///
/// El FK a identity.users NO se declara aqui (EF no puede crear FK cross-schema
/// de forma confiable desde un DbContext que no posee el schema identity): va
/// en la migracion SQL.
/// </summary>
public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable("accounts");
        b.HasKey(a => a.Id);

        b.Property(a => a.Id).HasColumnName("id");
        b.Property(a => a.UserId).HasColumnName("user_id").IsRequired();
        b.Property(a => a.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        b.Property(a => a.Broker).HasColumnName("broker").HasMaxLength(80).IsRequired();
        b.Property(a => a.MarketType).HasColumnName("market_type").HasConversion<short>().IsRequired();
        b.Property(a => a.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(a => a.InitialBalance).HasColumnName("initial_balance").HasColumnType("numeric(24,8)").IsRequired();
        // Nullable: Forex usa > 0, Binary acepta null (default 1.0 via handler).
        b.Property(a => a.Leverage).HasColumnName("leverage").HasColumnType("numeric(10,2)");
        b.Property(a => a.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        b.Ignore(a => a.DomainEvents);

        b.HasIndex(a => a.UserId).HasDatabaseName("ix_accounts_user");
        b.HasIndex(a => new { a.UserId, a.IsActive }).HasDatabaseName("ix_accounts_user_active");
    }
}
