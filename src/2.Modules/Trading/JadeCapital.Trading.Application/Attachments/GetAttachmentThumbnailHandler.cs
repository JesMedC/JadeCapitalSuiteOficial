using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application.Attachments;
using JadeCapital.Trading.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Attachments;

/// <summary>
/// Read-side handler that returns a presigned GET URL pointing to a
/// resized thumbnail of an image attachment. The URL embeds
/// <c>?width=</c> + <c>?height=</c> query params — the MinIO bucket
/// lifecycle policy (configured in <c>docker-compose.yml</c>) applies
/// the transform server-side. If the bucket does not support the
/// transform, the SDK falls back to the raw GET (the caller still gets
/// a usable URL).
///
/// Constraints (per spec):
/// <list type="bullet">
///   <item>Only image MIME types (<c>image/png</c>, <c>image/jpeg</c>,
///   <c>image/webp</c>) — non-images return
///   <see cref="AttachmentsErrors.ThumbnailNotSupported"/> (HTTP 415).</item>
///   <item>Width/height clamped to <c>[16, 1024]</c> to prevent abuse
///   (e.g. attacker requesting <c>?width=100000</c> to trigger expensive
///   transforms).</item>
///   <item>Cross-user scope: 404 if the attachment is not owned by
///   <c>UserId</c>. The handler delegates to
///   <see cref="ITradeReviewRepository.FindAttachmentByIdAsync"/>.</item>
/// </list>
/// </summary>
public sealed class GetAttachmentThumbnailHandler
    : IRequestHandler<GetAttachmentThumbnailQuery, Result<ThumbnailUrlDto>>
{
    private const int MinDimension = 16;
    private const int MaxDimension = 1024;
    private static readonly TimeSpan UrlTtl = TimeSpan.FromHours(1);

    private static readonly IReadOnlySet<string> ImageMimeTypes =
        new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "image/png",
            "image/jpeg",
            "image/webp",
        };

    private readonly ITradeReviewRepository _reviews;
    private readonly IAttachmentThumbnailGenerator _thumbnailGenerator;
    private readonly ILogger<GetAttachmentThumbnailHandler> _logger;

    public GetAttachmentThumbnailHandler(
        ITradeReviewRepository reviews,
        IAttachmentThumbnailGenerator thumbnailGenerator,
        ILogger<GetAttachmentThumbnailHandler> logger)
    {
        _reviews = reviews;
        _thumbnailGenerator = thumbnailGenerator;
        _logger = logger;
    }

    public async Task<Result<ThumbnailUrlDto>> Handle(
        GetAttachmentThumbnailQuery req,
        CancellationToken ct)
    {
        var width = Math.Clamp(req.Width, MinDimension, MaxDimension);
        var height = Math.Clamp(req.Height, MinDimension, MaxDimension);

        // 1) Cross-user scope: find the attachment via the review repo.
        var attachment = await _reviews.FindAttachmentByIdAsync(req.AttachmentId, req.UserId, ct);
        if (attachment is null)
        {
            return Result.Failure<ThumbnailUrlDto>(TradingDomainErrors.TradeAttachment.NotFound);
        }

        // 2) MIME-type gate.
        if (!ImageMimeTypes.Contains(attachment.ContentType))
        {
            return Result.Failure<ThumbnailUrlDto>(AttachmentsErrors.ThumbnailNotSupported);
        }

        // 3) Ask the thumbnail generator for the presigned URL.
        var presigned = await _thumbnailGenerator.GetThumbnailUrlAsync(
            attachment.ObjectKey, width, height, ct);

        var dto = new ThumbnailUrlDto(
            AttachmentId: attachment.Id,
            ContentType: attachment.ContentType,
            Width: width,
            Height: height,
            PresignedUrl: presigned,
            ExpiresAt: DateTimeOffset.UtcNow.Add(UrlTtl));

        return Result.Success(dto);
    }
}