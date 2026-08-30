using JadeCapital.Shared.Kernel.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace JadeCapital.Shared.Infrastructure.Storage;

/// <summary>
/// Implementacion de <see cref="IAttachmentStorage"/> sobre MinIO SDK.
/// Vive en <c>Shared.Infrastructure</c> (con el SDK pesado <c>Minio</c>
/// NuGet) para que el resto del sistema dependa solo de la abstraccion
/// en <c>Shared.Kernel</c>.
///
/// Responsabilidades:
/// <list type="bullet">
///   <item>Generar URLs PUT prefirmadas para uploads directos del browser
///   (<see cref="GetPresignedPutUrlAsync"/>).</item>
///   <item>Verificar que un object existe + size coincide durante
///   <c>Complete</c> (<see cref="VerifyObjectExistsAsync"/>). Usa
///   <c>StatObjectAsync</c> del SDK; es una llamada HEAD-like.</item>
///   <item>Borrar objects (<see cref="DeleteAsync"/>). Best-effort: si
///   el object no existe (<c>ObjectNotFound</c>), retornamos sin error.
///   Un orphan object en el bucket no rompe al cliente — el log warning
///   lo recoge el GC periodico (fuera del scope de v1).</item>
///   <item>Provisionar el bucket al startup (<see cref="InitializeAsync"/>).
///   Idempotente: <c>BucketAlreadyOwnedByYou</c> es no-error. Si el bucket
///   existe pero pertenece a otra tenant, falla ruidosamente.</item>
/// </list>
///
/// El <c>MinioClient</c> se inyecta via DI; <c>MinioClient</c> es
/// thread-safe per docs del SDK, asi que el store se registra como
/// Singleton.
/// </summary>
public sealed class MinioAttachmentStore : IAttachmentStorage
{
    private readonly IMinioClient _client;
    private readonly MinioOptions _options;
    private readonly ILogger<MinioAttachmentStore> _logger;

    public MinioAttachmentStore(
        IMinioClient client,
        IOptions<MinioOptions> options,
        ILogger<MinioAttachmentStore> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GetPresignedPutUrlAsync(
        string objectKey,
        TimeSpan expiry,
        CancellationToken ct = default)
    {
        var args = new PresignedPutObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(objectKey)
            .WithExpiry((int)expiry.TotalSeconds);

        var url = await _client.PresignedPutObjectAsync(args);
        return url;
    }

    public async Task<bool> VerifyObjectExistsAsync(
        string objectKey,
        long expectedSizeBytes,
        CancellationToken ct = default)
    {
        try
        {
            var args = new StatObjectArgs()
                .WithBucket(_options.Bucket)
                .WithObject(objectKey);
            var stat = await _client.StatObjectAsync(args, ct);

            // El SDK retorna Size como long en <c>ObjectStat</c>; default
            // minio no incluye SHA en la stat, solo size + metadata.
            // Comparamos size para confirmar que el cliente subio lo que
            // declaro al pedir el slot.
            return stat != null && stat.Size == expectedSizeBytes;
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
        catch (MinioException mex)
        {
            _logger.LogWarning(mex, "MinIO StatObject failed for {Key}.", objectKey);
            return false;
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken ct = default)
    {
        try
        {
            var args = new RemoveObjectArgs()
                .WithBucket(_options.Bucket)
                .WithObject(objectKey);
            await _client.RemoveObjectAsync(args, ct);
        }
        catch (ObjectNotFoundException)
        {
            // Best-effort: object ya no estaba (cliente no subio bytes). No-op.
            _logger.LogDebug("MinIO RemoveObject no-op for {Key} (already gone).", objectKey);
        }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // MakeBucketAsync es idempotente solo si la response es
        // <c>BucketAlreadyOwnedByYou</c>. <c>MakeBucketArgs</c> lleva
        // el nombre del bucket.
        var args = new MakeBucketArgs()
            .WithBucket(_options.Bucket);

        try
        {
            await _client.MakeBucketAsync(args, ct);
            _logger.LogInformation(
                "Provisioned MinIO bucket {Bucket} at {Endpoint}.",
                _options.Bucket, _options.Endpoint);
        }
        catch (MinioException mex)
            when (mex.Message?.Contains("Bucket already owned", System.StringComparison.OrdinalIgnoreCase) == true
                  || mex.Message?.Contains("BucketAlreadyOwned", System.StringComparison.OrdinalIgnoreCase) == true)
        {
            _logger.LogDebug(
                "MinIO bucket {Bucket} already exists — idempotent skip.",
                _options.Bucket);
        }
    }
}
