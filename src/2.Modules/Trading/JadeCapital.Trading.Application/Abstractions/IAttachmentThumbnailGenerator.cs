namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Generates presigned GET URLs for attachment thumbnails. The default
/// impl <c>MinioThumbnailGenerator</c> lives in Trading.Infrastructure
/// and delegates to the MinIO SDK with <c>?width=</c> + <c>?height=</c>
/// transform query params (requires the bucket lifecycle policy from
/// docker-compose; otherwise falls back to raw GET).
///
/// Lives in Application.Abstractions so the handler can depend on the
/// abstraction without pulling in the MinIO SDK.
/// </summary>
public interface IAttachmentThumbnailGenerator
{
    /// <summary>
    /// Returns a presigned GET URL for the given object with
    /// <paramref name="width"/> + <paramref name="height"/> transform
    /// params. The URL is valid for ~1 hour.
    /// </summary>
    Task<string> GetThumbnailUrlAsync(string objectKey, int width, int height, CancellationToken ct = default);
}