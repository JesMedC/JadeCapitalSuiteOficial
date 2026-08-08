using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JadeCapital.Identity.Infrastructure.BackgroundJobs;

/// <summary>
/// Hosted service que limpia refresh tokens expirados hace mas de 7 dias.
/// Reemplaza al Hangfire job (ver ADR-0002). Se ejecuta en un loop infinito con
/// el intervalo configurado en <see cref="JwtOptions.CleanupIntervalHours"/>.
///
/// Cada corrida borra como maximo <see cref="BatchLimit"/> filas para no
/// mantener un lock largo en la tabla.
/// </summary>
public sealed class RefreshTokenCleanupService : BackgroundService
{
    private const int RetentionDaysAfterExpiry = 7;
    private const int BatchLimit = 1000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JwtOptions _options;
    private readonly ILogger<RefreshTokenCleanupService> _logger;

    public RefreshTokenCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<JwtOptions> options,
        ILogger<RefreshTokenCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "RefreshTokenCleanupService started. Interval={Hours}h, RetentionDays={Days}d, BatchLimit={Limit}",
            _options.CleanupIntervalHours, RetentionDaysAfterExpiry, BatchLimit);

        // Primer corrida inmediata para limpiar backlogs al deploy.
        await RunOnceAsync(stoppingToken);

        var interval = TimeSpan.FromHours(Math.Max(1, _options.CleanupIntervalHours));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunOnceAsync(stoppingToken);
        }

        _logger.LogInformation("RefreshTokenCleanupService stopped.");
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDaysAfterExpiry);

            // EF Core 9 permite FromSql con composicion limitada. Hacemos un ExecuteDelete
            // atomico con LIMIT via Take() proyectado. ExecuteDeleteAsync traduce a DELETE
            // unico en Postgres, sin cargar filas en memoria.
            var deleted = await db.RefreshTokens
                .Where(r => r.ExpiresAt < cutoff)
                .Take(BatchLimit)
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
                _logger.LogInformation(
                    "Refresh token cleanup deleted {Deleted} row(s) older than {Cutoff:o} (batch limit {Limit}).",
                    deleted, cutoff, BatchLimit);
            else
                _logger.LogDebug("Refresh token cleanup found nothing to delete (cutoff {Cutoff:o}).", cutoff);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // No queremos que una falla del cleanup tumbe el host. Logueamos y seguimos.
            _logger.LogError(ex, "Refresh token cleanup run failed.");
        }
    }
}