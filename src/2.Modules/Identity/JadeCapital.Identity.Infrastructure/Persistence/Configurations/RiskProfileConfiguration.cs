using JadeCapital.Identity.Domain.RiskProfile;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>identity.risk_profiles</c>.
///
/// Persistencia:
/// <list type="bullet">
///   <item><c>Capital</c> is an embedded Money VO: amount + currency_code
///   se mapean a <c>capital_amount NUMERIC(24,8)</c> + <c>capital_currency
///   CHAR(3)</c> via OwnsOne.</item>
///   <item><c>MaxDrawdownPercent</c>, <c>RiskPerTradePercent</c>,
///   <c>RiskRewardTarget</c> se mapean a columnas planas: NUMERIC(5,2) +
///   NUMERIC(5,2) + NUMERIC(6,2). El VO FromTrusted las reconstruye al
///   hidratar — la validacion de rango ya se aplico al escribir.</item>
///   <item><c>IsActive</c> + partial unique index
///   <c>ux_risk_profiles_user_active</c> garantiza el single-active
///   invariant al nivel del DB. El indice lo crea la migration 0009;
///   EF no lo duplica (HasFilter(true) sin IsUnique no agrega nada).</item>
///   <item>FK a <c>identity.users(id)</c> ON DELETE CASCADE: si se borra
///   el User, su perfil va con el — no tiene sentido un perfil sin usuario.</item>
/// </list>
/// </summary>
internal sealed class RiskProfileConfiguration : IEntityTypeConfiguration<RiskProfile>
{
    public void Configure(EntityTypeBuilder<RiskProfile> b)
    {
        b.ToTable("risk_profiles");
        b.HasKey(p => p.Id);

        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.UserId).HasColumnName("user_id").IsRequired();

        // Money VO: Amount + CurrencyCode. EF OwnsOne persiste ambos a
        // columnas planas; el derivado `Currency` (Currency.FromTrusted)
        // se ignora.
        b.OwnsOne(p => p.Capital, mb =>
        {
            mb.Property(m => m.Amount)
                .HasColumnName("capital_amount")
                .HasColumnType("numeric(24,8)")
                .IsRequired();
            mb.Property(m => m.CurrencyCode)
                .HasColumnName("capital_currency")
                .HasColumnType("char(3)")
                .IsRequired();
        });

        b.Property(p => p.MaxDrawdownPercent)
            .HasConversion(v => v.Value, v => MaxDrawdownPercent.FromTrusted(v))
            .HasColumnName("max_drawdown_percent")
            .HasColumnType("numeric(5,2)")
            .IsRequired();

        b.Property(p => p.RiskPerTradePercent)
            .HasConversion(v => v.Value, v => RiskPerTradePercent.FromTrusted(v))
            .HasColumnName("risk_per_trade_percent")
            .HasColumnType("numeric(5,2)")
            .IsRequired();

        b.Property(p => p.RiskRewardTarget)
            .HasConversion(v => v.Value, v => RiskRewardRatio.FromTrusted(v))
            .HasColumnName("risk_reward_target")
            .HasColumnType("numeric(6,2)")
            .IsRequired();

        b.Property(p => p.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(p => p.SupersededAt).HasColumnName("superseded_at");
        b.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        b.Ignore(p => p.DomainEvents);

        b.HasOne<JadeCapital.Identity.Domain.Users.User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Non-unique (user_id) — reads cross-module: cualquier perfil del
        // usuario, no solo el activo. El UNIQUE INDEX PARTIAL ya cubre la
        // lectura del activo.
        b.HasIndex(p => p.UserId).HasDatabaseName("ix_risk_profiles_user");

        // Anotacion documentativa — la migration 0009 crea el partial unique
        // index real con `WHERE is_active`. EF no lo recrea en runtime.
        // Si quieres que EF lo maneje en lugar del SQL hand-authored, comenta
        // este bloque Y reemplaza la migration por una generada por EF.
        // Por ahora: SQL wins.
        b.HasIndex(p => p.UserId)
            .HasDatabaseName("ux_risk_profiles_user_active")
            .HasFilter("is_active")
            .IsUnique();
    }
}
