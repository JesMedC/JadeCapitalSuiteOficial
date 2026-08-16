using JadeCapital.Shared.Kernel.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minio;

namespace JadeCapital.Shared.Infrastructure.Storage;

/// <summary>
/// Extension de DI para registrar la pila de MinIO en el host.
///
/// Registra:
/// <list type="bullet">
///   <item><see cref="MinioOptions"/> via <c>IConfiguration</c> section
///   <c>ConnectionStrings:Storage</c> (parser de semicolon-separated) o
///   <c>Storage</c> como fallback.</item>
///   <item><c>IMinioClient</c> como Singleton (thread-safe per SDK docs).</item>
///   <item><see cref="IAttachmentStorage"/> como Singleton.</item>
///   <item><see cref="MinioInitializerHostedService"/> como hosted service
///   que provisiona el bucket al startup.</item>
/// </list>
///
/// </summary>
public static class MinioServiceCollectionExtensions
{
    public static IServiceCollection AddMinioInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // El parser maneja AMBAS formas:
        //   - ConnectionStrings:Storage=Endpoint=...;AccessKey=...
        //   - Storage:Endpoint=..., Storage:AccessKey=... (por section)
        // Si existe la connection-string form, gana; sino cae al section.
        var connectionString = configuration.GetConnectionString("Storage");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.Configure<MinioOptions>(opts =>
            {
                var parsed = MinioOptions.ParseConnectionString(connectionString);
                opts.Endpoint  = parsed.Endpoint;
                opts.AccessKey = parsed.AccessKey;
                opts.SecretKey = parsed.SecretKey;
                opts.Bucket    = parsed.Bucket;
                opts.Ssl       = parsed.Ssl;
            });
        }
        else
        {
            services.Configure<MinioOptions>(configuration.GetSection("Storage"));
        }

        services.AddSingleton<IMinioClient>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MinioOptions>>().Value;
            var client = new MinioClient()
                .WithEndpoint(opts.Endpoint)
                .WithCredentials(opts.AccessKey, opts.SecretKey)
                .WithSSL(opts.Ssl);
            return client.Build();
        });

        services.AddSingleton<IAttachmentStorage, MinioAttachmentStore>();
        services.AddHostedService<MinioInitializerHostedService>();

        return services;
    }
}
