using JadeCapital.Host;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace JadeCapital.Api.IntegrationTests.Infrastructure;

/// <summary>
///
/// Solo crea los contenedores LA PRIMERA VEZ que se instancia (estatico, lazy).
/// Reusa entre tests para velocidad.
/// </summary>
public sealed class JadeApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly Lazy<Task<PostgreSqlContainer>> _pgTask = new(async () =>
    {
        var container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("jade_test")
            .WithUsername("jade")
            .WithPassword("test_password_strong")
            .Build();
        await container.StartAsync();
        return container;
    });

    private static readonly Lazy<Task<RedisContainer>> _redisTask = new(async () =>
    {
        var container = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        await container.StartAsync();
        return container;
    });

    private static bool _dbInitialized;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    public string PostgresConnectionString { get; private set; } = string.Empty;
    public string RedisConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var pg = await _pgTask.Value;
        var redis = await _redisTask.Value;
        PostgresConnectionString = pg.GetConnectionString();
        RedisConnectionString = $"redis:{redis.GetConnectionString().Replace("redis://", "")}";

        // Inicializamos la BD la primera vez con la migracion SQL.
        if (!_dbInitialized)
        {
            await _initLock.WaitAsync();
            try
            {
                if (!_dbInitialized)
                {
                    await ApplyMigrationAsync();
                    _dbInitialized = true;
                }
            }
            finally
            {
                _initLock.Release();
            }
        }
    }

    public new async Task DisposeAsync()
    {
        // No cerramos los contenedores: son estaticos y se reutilizan.
        await Task.CompletedTask;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Set the content root to the source project directory so Mvc.Testing does not
        // try to walk up looking for a *.sln file (the repo uses *.slnx which the
        // framework does not recognize).
        var sourceDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..",
            "src/1.Api/JadeCapital.Host"));
        if (Directory.Exists(sourceDir))
            builder.UseContentRoot(sourceDir);

        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = PostgresConnectionString,
                ["ConnectionStrings:Redis"] = RedisConnectionString,
                ["Jwt:Issuer"] = "JadeCapitalSuite.Test",
                ["Jwt:Audience"] = "JadeCapitalSuite.Test.Client",
                ["Jwt:AccessTokenSecret"] = "test_secret_for_integration_tests_must_be_32_chars_xx",
                ["Jwt:RefreshTokenSecret"] = "test_refresh_secret_for_integration_tests_also_32_xx",
                ["Jwt:AccessTokenTtlMinutes"] = "15",
                ["Jwt:RefreshTokenTtlDays"] = "14",
                ["RateLimit:AuthPermit"] = "10000",
                ["RateLimit:ApiPermit"] = "10000",
                ["Cors:Origins:0"] = "http://localhost"
            });
        });
    }

    private async Task ApplyMigrationAsync()
    {
        await using var conn = new Npgsql.NpgsqlConnection(PostgresConnectionString);
        await conn.OpenAsync();

        // Habilitar extension citext para emails case-insensitive.
        await using (var enable = new Npgsql.NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS citext;", conn))
            await enable.ExecuteNonQueryAsync();

        var sqlPath = Path.Combine(AppContext.BaseDirectory, "Migrations", "20260806_0001_InitialIdentitySchema.sql");
        if (!File.Exists(sqlPath))
        {
            // Buscar el archivo fuente (el test runner no copia el .sql al output).
            // 6 niveles arriba de bin/Debug/net10.0/ llegan a la raiz del repo.
            var srcPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
                "src/2.Modules/Identity/JadeCapital.Identity.Infrastructure/Persistence/Migrations/20260806_0001_InitialIdentitySchema.sql"));
            if (File.Exists(srcPath))
                sqlPath = srcPath;
            else
                throw new FileNotFoundException($"Migration SQL not found. Tried: {sqlPath}");
        }

        var sql = await File.ReadAllTextAsync(sqlPath);
        await using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}