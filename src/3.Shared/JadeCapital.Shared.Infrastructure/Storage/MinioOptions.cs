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
///
/// Nota: el <c>Endpoint</c> puede venir con esquema (<c>http://</c> / <c>https://</c>)
/// o sin el (<c>minio:9000</c>). El SDK de MinIO acepta SOLO el formato
/// <c>host:port</c> — el esquema se controla via <see cref="Ssl"/>. El
/// <see cref="ParseConnectionString"/> quita el esquema si esta presente.
/// </summary>
public sealed class MinioOptions
{
    /// <summary>Host:port del servidor MinIO (SIN esquema).
    /// El SDK rechaza strings con <c>http://</c>; use <see cref="Ssl"/>
    /// para indicar HTTPS.</summary>
    public string Endpoint { get; set; } = "localhost:9000";

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
    /// <c>Key1=value1;Key2=value2</c>. Quita el esquema del endpoint si
    /// esta presente (<c>http://</c> o <c>https://</c>) — el SDK de MinIO
    /// rechaza endpoints con esquema.
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
                case "endpoint":
                    opts.Endpoint = StripScheme(value);
                    if (value.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase))
                        opts.Ssl = true;
                    break;
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

    /// <summary>Quita el prefijo <c>http://</c> o <c>https://</c> del
    /// endpoint. El SDK de MinIO 6.x requiere <c>host:port</c> sin esquema.</summary>
    private static string StripScheme(string endpoint)
    {
        if (string.IsNullOrEmpty(endpoint)) return endpoint;
        if (endpoint.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase))
            return endpoint.Substring("https://".Length);
        if (endpoint.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase))
            return endpoint.Substring("http://".Length);
        return endpoint;
    }
}
