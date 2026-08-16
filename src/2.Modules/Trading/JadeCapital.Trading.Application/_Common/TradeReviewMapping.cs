using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.TradeAttachments;
using JadeCapital.Trading.Domain.TradeReviews;

namespace JadeCapital.Trading.Application._Common;

/// <summary>
/// Mapeos entre los aggregates <see cref="TradeReview"/>/<see cref="TradeAttachment"/>
/// y los DTOs de aplicacion. La conversion no toca navegaciones: si un
/// caller quiere el review con attachments, tiene que pedir el review y
/// los attachments por separado y unirlos en este mapper.
/// </summary>
public static class TradeReviewMappingExtensions
{
    /// <summary>
    /// Convierte un aggregate <see cref="TradeReview"/> en su DTO. La lista
    /// de attachments se pasa por parametro (separada del aggregate para
    /// evitar cargar navegaciones lazy en EF que no usamos).
    /// </summary>
    public static TradeReviewDto ToDto(this TradeReview review, IReadOnlyList<TradeAttachmentDto> attachments)
        => new(
            review.Id,
            review.TradeId,
            review.UserId,
            (byte)review.Emotionality,
            review.SetupUsed,
            review.Lessons,
            review.Rating,
            review.CreatedAt,
            review.UpdatedAt,
            attachments);

    /// <summary>
    /// Convierte un aggregate <see cref="TradeAttachment"/> en su DTO. El
    /// <c>Status</c> se serializa como string lowercase para que el FE no
    /// necesite conocer el enum interno.
    /// </summary>
    public static TradeAttachmentDto ToDto(this TradeAttachment attachment)
        => new(
            attachment.Id,
            attachment.ReviewId,
            attachment.ObjectKey,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.Sha256,
            StatusToString(attachment.Status),
            attachment.CreatedAt,
            attachment.UploadedAt);

    private static string StatusToString(TradeAttachmentStatus status) => status switch
    {
        TradeAttachmentStatus.Pending  => "pending",
        TradeAttachmentStatus.Uploaded => "uploaded",
        TradeAttachmentStatus.Failed   => "failed",
        _ => "pending",
    };
}
