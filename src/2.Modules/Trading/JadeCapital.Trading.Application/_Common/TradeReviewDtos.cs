using JadeCapital.Trading.Domain.TradeAttachments;
using JadeCapital.Trading.Domain.TradeReviews;

namespace JadeCapital.Trading.Application._Common;

/// <summary>
/// DTO del post-trade review devuelto al frontend. Incluye la lista de
/// attachments en linea para evitar un round-trip extra cuando el FE
/// renderiza la pantalla de review.
///
/// Mapeado desde <see cref="TradeReview"/> + <see cref="TradeAttachment"/>
/// en <c>TradeReviewMappingExtensions</c>.
/// </summary>
public sealed record TradeReviewDto(
    Guid Id,
    Guid TradeId,
    Guid UserId,
    byte Emotionality,
    string? SetupUsed,
    string? Lessons,
    byte? Rating,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<TradeAttachmentDto> Attachments);

/// <summary>
/// DTO de un attachment del review. NO incluye <c>Sha256</c> (considerado
/// detalles tecnicos; tampoco <c>object_key</c> directamente — ese campo
/// solo lo necesita el cliente durante el upload y se devuelve inline en
/// la respuesta de RequestAttachmentUpload; para GET del review, el cliente
/// usa el id + presigned GET que el FE puede pedir en otra iteracion).
///
/// <c>Status</c> es un string ('pending' | 'uploaded' | 'failed') para que
/// el FE no tenga que conocer el enum interno.
/// </summary>
public sealed record TradeAttachmentDto(
    Guid Id,
    Guid ReviewId,
    string ObjectKey,
    string ContentType,
    long SizeBytes,
    string? Sha256,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UploadedAt);

/// <summary>
/// Resultado de <c>RequestAttachmentUploadHandler</c>. El FE recibe:
/// <list type="bullet">
///   <item><c>AttachmentId</c> — para identificar el attachment en
///   confirmaciones y deletes.</item>
///   <item><c>ObjectKey</c> — para debugging / idempotency.</item>
///   <item><c>PutUrl</c> — presigned URL; el FE hace PUT directo aqui.</item>
///   <item><c>ExpiresInSeconds</c> — TTL del URL; el FE debe rechazarlo
///   si la ventana expira antes del upload.</item>
/// </list>
/// </summary>
public sealed record RequestAttachmentUploadResultDto(
    Guid AttachmentId,
    string ObjectKey,
    string PutUrl,
    int ExpiresInSeconds);
