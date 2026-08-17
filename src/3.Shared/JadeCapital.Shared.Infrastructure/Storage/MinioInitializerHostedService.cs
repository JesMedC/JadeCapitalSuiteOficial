using JadeCapital.Shared.Kernel.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Shared.Infrastructure.Storage;

/// <summary>
/// Hosted service que provisiona el bucket MinIO al startup del host.
/// Idempotente: corre en cada arranque sin duplicar trabajo.
///
/// Por que hosted service (no startup filter): la limpieza DI nos da
/// <see cref="IAttachmentStorage"/> ya construido; un startup filter
/// tendria que resolver manualmente y rompe el orden de dependencias.
/// El <see cref="BackgroundService.StartAsync"/> corre en startup antes
/// de aceptar trafico HTTP, asi que el bucket esta listo cuando el
/// primer endpoint recibe un request.
/// </summary>
public sealed class MinioInitializerHostedService : IHostedService
{
    private readonly IAttachmentStorage _storage;
    private readonly ILogger<MinioInitializerHostedService> _logger;

    public MinioInitializerHostedService(
        IAttachmentStorage storage,
        ILogger<MinioInitializerHostedService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Bootstrapping MinIO bucket...");
        try
        {
            await _storage.InitializeAsync(cancellationToken);
        }
        catch (System.Exception ex)
        {
            // Loggeamos pero NO fallamos el startup: si MinIO esta
            // momentaneamente caido, los endpoints daran 503/error
            // y el operador vera el log. Preferible a tirar el host
            // entero por una dependencia que se puede recuperar sola.
            _logger.LogError(ex,
                "MinIO bucket bootstrap failed — non-fatal. The host will start; MinIO-dependent endpoints will fail until storage recovers.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
