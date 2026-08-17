using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Attachments;
using MediatR;

namespace JadeCapital.Trading.Application.Attachments;

/// <summary>
/// Read-side query that returns a presigned GET URL pointing to a
/// 256×256 thumbnail of an image attachment. The endpoint is mounted on
/// the existing <c>MapTradeReviewEndpoints</c> group as
/// <c>GET /api/attachments/{id:guid}/thumbnail</c>.
///
/// Returns <c>ThumbnailUrlDto.PresignedUrl</c> with the URL the FE can
/// follow to receive the resized image (302 redirect from this endpoint,
/// then the actual binary from MinIO).
/// </summary>
public sealed record GetAttachmentThumbnailQuery(
    Guid AttachmentId,
    Guid UserId,
    int Width,
    int Height) : IRequest<Result<ThumbnailUrlDto>>;

/// <summary>
/// Wire shape returned by <see cref="GetAttachmentThumbnailQuery"/>.
/// <c>PresignedUrl</c> is a fully-formed URL with <c>?width=</c> +
/// <c>?height=</c> transform params (MinIO bucket policy must support
/// the transform; if not, the SDK falls back to the raw GET).
/// </summary>
public sealed record ThumbnailUrlDto(
    Guid AttachmentId,
    string ContentType,
    int Width,
    int Height,
    string PresignedUrl,
    DateTimeOffset ExpiresAt);