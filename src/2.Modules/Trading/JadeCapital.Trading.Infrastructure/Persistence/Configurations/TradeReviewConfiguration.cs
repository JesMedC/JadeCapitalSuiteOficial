using JadeCapital.Trading.Domain.TradeReviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeo de <see cref="TradeReview"/> a la tabla <c>trading.trade_reviews</c>
/// (slice 1d.1).
///
/// Persistencia:
/// <list type="bullet">
///   <item>ReviewEmotionality es <c>: byte</c> -&gt; SMALLINT en la DB.
///   Mismo patron que PreTradeChecklistEmotionality.</item>
///   <item>Rating es <c>byte?</c> -&gt; SMALLINT NULL. <c>HasConversion&lt;byte&gt;</c>
///   no aplica para nullables; la conversion es implicita porque el
///   underlying byte mapea directo.</item>
///   <item>El UNIQUE sobre trade_id ya esta en la columna (la migration
///   SQL lo crea). EF lo entiende por la declaracion de columna + FK
///   unique; no re-declaramos.</item>
///   <item><c>DomainEvents</c> se ignora — los events se despachan fuera
///   de EF y no se persisten.</item>
/// </list>
/// </summary>
internal sealed class TradeReviewConfiguration : IEntityTypeConfiguration<TradeReview>
{
    public void Configure(EntityTypeBuilder<TradeReview> b)
    {
        b.ToTable("trade_reviews");
        b.HasKey(r => r.Id);

        b.Property(r => r.Id).HasColumnName("id");
        b.Property(r => r.TradeId).HasColumnName("trade_id").IsRequired();
        b.Property(r => r.UserId).HasColumnName("user_id").IsRequired();

        b.Property(r => r.Emotionality)
            .HasColumnName("emotionality")
            .HasConversion<byte>()
            .IsRequired();

        b.Property(r => r.SetupUsed)
            .HasColumnName("setup_used")
            .HasMaxLength(64);

        b.Property(r => r.Lessons)
            .HasColumnName("lessons")
            .HasColumnType("text");

        b.Property(r => r.Rating).HasColumnName("rating");

        b.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();

        b.Ignore(r => r.DomainEvents);

        // FKs cross-schema. Como en PreTradeChecklistConfiguration: declaramos
        // la cardinalidad via shadow navigation para que EF entienda las
        // relaciones, pero el constraint FK vive en la migration SQL.
        b.HasOne<JadeCapital.Trading.Domain.Trades.Trade>()
            .WithMany()
            .HasForeignKey(r => r.TradeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indice secundario sobre user_id para queries cross-module futuras.
        b.HasIndex(r => r.UserId).HasDatabaseName("ix_trade_reviews_user");
    }
}
