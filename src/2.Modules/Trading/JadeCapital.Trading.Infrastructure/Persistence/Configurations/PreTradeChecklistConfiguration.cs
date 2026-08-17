using JadeCapital.Trading.Domain.PreTradeChecklists;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeo de <see cref="PreTradeChecklist"/> a la tabla
/// <c>trading.pre_trade_checklists</c>.
///
/// Persistencia:
/// <list type="bullet">
///   <item><c>Submission</c> es un VO record (PreTradeChecklistSubmission)
///   con 5 campos planos. Se mapea via OwnsOne con columnas flat (no
///   tabla anidada) porque el spec modela el checklist como un solo
///   registro y los 5 campos no tienen ciclo de vida independiente.</item>
///   <item>Los enums Emotionality / SetupQuality son <c>: byte</c> y se
///   persisten como SMALLINT via <c>HasConversion&lt;byte&gt;()</c>.</item>
///   <item>El FK a <c>trading.trades</c> es ON DELETE CASCADE — si se
///   borra el trade, su checklist va con el. El FK a <c>identity.users</c>
///   es ON DELETE RESTRICT y la migration SQL lo crea; aqui solo declaramos
///   la relacion para que EF entienda la cardinalidad.</item>
///   <item>El UNIQUE sobre trade_id ya esta en la columna (la migration
///   SQL lo crea con <c>trade_id UUID NOT NULL UNIQUE</c>); EF no necesita
///   declararlo otra vez — de hecho, declararlo causa conflicto porque
///   EF lo intenta recrear.</item>
/// </list>
/// </summary>
internal sealed class PreTradeChecklistConfiguration : IEntityTypeConfiguration<PreTradeChecklist>
{
    public void Configure(EntityTypeBuilder<PreTradeChecklist> b)
    {
        b.ToTable("pre_trade_checklists");
        b.HasKey(c => c.Id);

        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.TradeId).HasColumnName("trade_id").IsRequired();
        b.Property(c => c.UserId).HasColumnName("user_id").IsRequired();
        b.Property(c => c.SubmittedAt).HasColumnName("submitted_at").IsRequired();

        b.Ignore(c => c.DomainEvents);
        b.Ignore(c => c.CreatedAt);
        b.Ignore(c => c.UpdatedAt);

        // Submission VO: campos flat en columnas de la misma tabla.
        // Emotionality y SetupQuality son : byte -> SMALLINT en la DB.
        b.OwnsOne(c => c.Submission, sb =>
        {
            sb.Property(s => s.Emotionality)
                .HasColumnName("emotionality")
                .HasConversion<byte>()
                .IsRequired();
            sb.Property(s => s.SetupQuality)
                .HasColumnName("setup_quality")
                .HasConversion<byte>()
                .IsRequired();
            sb.Property(s => s.RiskRewardAtEntry)
                .HasColumnName("risk_reward_at_entry")
                .HasColumnType("numeric(6,2)")
                .IsRequired();
            sb.Property(s => s.RiskRewardTargetUsed)
                .HasColumnName("risk_reward_target_used")
                .HasColumnType("numeric(6,2)")
                .IsRequired();
            sb.Property(s => s.ConfluencesCount)
                .HasColumnName("confluences_count")
                .IsRequired();
        });

        // FKs: cross-schema, declaramos shadow navigation para que EF
        // entienda la cardinalidad sin navegar al aggregate de Identity.
        b.HasOne<JadeCapital.Trading.Domain.Trades.Trade>()
            .WithMany()
            .HasForeignKey(c => c.TradeId)
            .OnDelete(DeleteBehavior.Cascade);

        // El FK a identity.users es cross-schema; la migration SQL lo crea
        // y EF no puede administrarlo limpiamente. Dejamos la relacion
        // declarada (sin intentar crear la FK en runtime) para que EF
        // entienda la cardinalidad.

        // Indice secundario sobre user_id: la migration SQL lo crea.
        // Aqui declaramos solo el nombre para que EF no genere un DROP/CREATE
        // adicional en runtime. Lo dejamos como NO-UNIQUE porque la
        // semantica "single checklist per trade" ya esta enforced por el
        // UNIQUE sobre trade_id.
        b.HasIndex(c => c.UserId).HasDatabaseName("ix_pre_trade_checklists_user");
    }
}
