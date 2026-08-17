using JadeCapital.Shared.Kernel.Storage;
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

        // Slice 4d — lifecycle + virus scan + thumbnail (migration 0018).
        // Note: migration 0018 ALSO adds a redundant `bytes` column for
        // backward-compat with future snapshots (lifecycle audit) — EF
        // does NOT map it because SizeBytes is already mapped to the
        // legacy `size_bytes` column. The sweep reads `size_bytes` via
        // the existing entity mapping.
        b.Property(a => a.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(a => a.SweptAt).HasColumnName("swept_at");
        b.Property(a => a.ThumbnailObjectKey).HasColumnName("thumbnail_object_key").HasMaxLength(255);
        b.Property(a => a.ExpiresAt).HasColumnName("expires_at");
        b.Property(a => a.VirusScannedAt).HasColumnName("virus_scanned_at");
        b.Property(a => a.ScanResult).HasColumnName("scan_result").HasConversion<byte>();

        b.Ignore(a => a.DomainEvents);

        // FKs cross-schema: shadow navigation para cardinalidad; los
        // constraints los crea la migration SQL.
        b.HasOne<JadeCapital.Trading.Domain.TradeReviews.TradeReview>()
            .WithMany()
            .HasForeignKey(a => a.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(a => a.ReviewId).HasDatabaseName("ix_trade_attachments_review");

        // Slice 4d — sweep index on expires_at (range scan for the daily
        // AttachmentLifecycleService). Migration 0018 creates this with
        // the same definition; EF mirrors it so dotnet-ef migrations add
        // stays a no-op.
        b.HasIndex(a => a.ExpiresAt)
            .HasDatabaseName("ix_trade_attachments_expires_sweep")
            .HasFilter("is_active = true AND expires_at IS NOT NULL");
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
