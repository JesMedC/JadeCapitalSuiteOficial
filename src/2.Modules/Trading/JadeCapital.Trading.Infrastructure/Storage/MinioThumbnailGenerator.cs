using JadeCapital.Shared.Infrastructure.Storage;
using JadeCapital.Trading.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;

namespace JadeCapital.Trading.Infrastructure.Storage;

/// <summary>
/// MinIO-backed implementation of <see cref="IAttachmentThumbnailGenerator"/>.
///
/// Calls <c>IMinioClient.PresignedGetObjectAsync</c> with a 1h TTL, then
/// appends <c>?width=</c> + <c>?height=</c> query params so the bucket
/// lifecycle policy (configured in <c>docker-compose.yml</c> via
/// <c>minio-init</c>) can apply the transform server-side. If the bucket
/// does NOT support transforms, MinIO returns the raw object — the FE
/// still gets a usable URL.
///
/// The class lives in <c>Trading.Infrastructure.Storage</c> alongside
/// <c>MinioAttachmentStore</c> (which lives in <c>Shared.Infrastructure</c>
/// for cross-module use). Keeping the thumbnail-specific code in Trading
/// reflects that this is a Trading feature, even though the SDK is the
/// same one.
/// </summary>
public sealed class MinioThumbnailGenerator : IAttachmentThumbnailGenerator
{
    private const int ExpirySeconds = 3600;

    private readonly IMinioClient _client;
    private readonly MinioOptions _options;
    private readonly ILogger<MinioThumbnailGenerator> _logger;

    public MinioThumbnailGenerator(
        IMinioClient client,
        IOptions<MinioOptions> options,
        ILogger<MinioThumbnailGenerator> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GetThumbnailUrlAsync(string objectKey, int width, int height, CancellationToken ct = default)
    {
        var args = new Minio.DataModel.Args.PresignedGetObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(objectKey)
            .WithExpiry(ExpirySeconds);

        var url = await _client.PresignedGetObjectAsync(args);

        // Append transform params. UriBuilder.Query preserves any params
        // the SDK already added (signature, etc.).
        var builder = new System.UriBuilder(url);
        var existing = builder.Query.TrimStart('?');
        var separator = string.IsNullOrEmpty(existing) ? string.Empty : "&";
        builder.Query = $"{existing}{separator}width={width}&height={height}";

        _logger.LogDebug(
            "Generated thumbnail URL for {Key} ({W}x{H}); expires in {ExpirySec}s.",
            objectKey, width, height, ExpirySeconds);

        return builder.Uri.ToString();
    }
}