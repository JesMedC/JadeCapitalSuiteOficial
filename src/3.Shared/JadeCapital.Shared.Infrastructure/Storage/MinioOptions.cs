namespace JadeCapital.Shared.Infrastructure.Storage;

/// <summary>
/// Configuracion del cliente MinIO (S3-compatible) usado por
/// <see cref="MinioAttachmentStore"/> en el slice 1d.1.
///
/// Cargado desde la cadena <c>ConnectionStrings:Storage</c> en
/// <c>appsettings.json</c> (<c>.env</c> produce <c>ConnectionStrings__Storage</c>):
/// <code>
/// Endpoint=http://minio:9000;AccessKey=...;SecretKey=...;Bucket=jade-uploads;Ssl=false
/// </code>
/// El parser es tolerante al orden y case-insensitive en las claves.
/// </summary>
public sealed class MinioOptions
{
    /// <summary>Endpoint del servidor MinIO (e.g. <c>http://minio:9000</c>).</summary>
    public string Endpoint { get; set; } = "http://localhost:9000";

    /// <summary>Access key del bucket.</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>Secret key del bucket.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Nombre del bucket donde se persisten los attachments.</summary>
    public string Bucket { get; set; } = "jade-uploads";

    /// <summary>Si usar HTTPS hacia el endpoint MinIO. Default false (dev).</summary>
    public bool Ssl { get; set; }

    /// <summary>
    /// Parsea la connection string en formato
    /// <c>Key1=value1;Key2=value2</c>. Usado por el bootstrapper
    /// <c>AddMinioInfrastructure</c> cuando la config no viene de
    /// <c>IConfiguration</c> section-style.
    /// </summary>
    public static MinioOptions ParseConnectionString(string connectionString)
    {
        var opts = new MinioOptions();
        if (string.IsNullOrWhiteSpace(connectionString)) return opts;

        foreach (var part in connectionString.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0) continue;
            var key = part.Substring(0, eq).Trim();
            var value = part.Substring(eq + 1).Trim();
            switch (key.ToLowerInvariant())
            {
                case "endpoint":  opts.Endpoint  = value; break;
                case "accesskey": opts.AccessKey = value; break;
                case "secretkey": opts.SecretKey = value; break;
                case "bucket":    opts.Bucket    = value; break;
                case "ssl":
                    opts.Ssl = bool.TryParse(value, out var b) && b;
                    break;
            }
        }
        return opts;
    }
}
