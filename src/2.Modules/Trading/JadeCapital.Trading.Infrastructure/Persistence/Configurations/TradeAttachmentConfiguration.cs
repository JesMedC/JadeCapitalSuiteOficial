using JadeCapital.Trading.Domain.TradeAttachments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeo de <see cref="TradeAttachment"/> a la tabla
/// <c>trading.trade_attachments</c> (slice 1d.1).
///
/// Persistencia:
/// <list type="bullet">
///   <item><c>Status</c> es <c>: byte</c> (Pending=0, Uploaded=1, Failed=2).
///   Se persiste como <c>VARCHAR(16)</c> lowercase string en la DB (no
///   SMALLINT) porque la migration SQL asi lo declara y el spec requiere
///   el set exacto <c>('pending','uploaded','failed')</c>. Usamos
///   <c>ValueConverter</c> byte-to-string lowercase.</item>
///   <item><c>ObjectKey</c> es UNIQUE en la DB (la migration ya lo crea
///   con la columna UNIQUE); EF lo entiende por la declaracion y no
///   duplica el index.</item>
///   <item><c>Sha256</c> es CHAR(64) NULL. La migration SQL enforces el
///   formato via CHECK; EF solo mapea la columna.</item>
///   <item><c>DomainEvents</c> se ignora.</item>
/// </list>
/// </summary>
internal sealed class TradeAttachmentConfiguration : IEntityTypeConfiguration<TradeAttachment>
{
    public void Configure(EntityTypeBuilder<TradeAttachment> b)
    {
        b.ToTable("trade_attachments");
        b.HasKey(a => a.Id);

        b.Property(a => a.Id).HasColumnName("id");
        b.Property(a => a.ReviewId).HasColumnName("review_id").IsRequired();
        b.Property(a => a.UserId).HasColumnName("user_id").IsRequired();
        b.Property(a => a.ObjectKey).HasColumnName("object_key").HasMaxLength(512).IsRequired();
        b.Property(a => a.ContentType).HasColumnName("content_type").HasMaxLength(127).IsRequired();
        b.Property(a => a.SizeBytes).HasColumnName("size_bytes").IsRequired();
        b.Property(a => a.Sha256).HasColumnName("sha256").HasMaxLength(64);
        b.Property(a => a.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion(
                v => StatusToString(v),
                v => StringToStatus(v))
            .IsRequired();

        b.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(a => a.UploadedAt).HasColumnName("uploaded_at");

        b.Ignore(a => a.DomainEvents);

        // FKs cross-schema: shadow navigation para cardinalidad; los
        // constraints los crea la migration SQL.
        b.HasOne<JadeCapital.Trading.Domain.TradeReviews.TradeReview>()
            .WithMany()
            .HasForeignKey(a => a.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(a => a.ReviewId).HasDatabaseName("ix_trade_attachments_review");
    }

    private static string StatusToString(TradeAttachmentStatus status) => status switch
    {
        TradeAttachmentStatus.Pending  => "pending",
        TradeAttachmentStatus.Uploaded => "uploaded",
        TradeAttachmentStatus.Failed   => "failed",
        _ => "pending",
    };

    private static TradeAttachmentStatus StringToStatus(string value) => value switch
    {
        "pending"  => TradeAttachmentStatus.Pending,
        "uploaded" => TradeAttachmentStatus.Uploaded,
        "failed"   => TradeAttachmentStatus.Failed,
        _          => TradeAttachmentStatus.Pending,
    };
}
