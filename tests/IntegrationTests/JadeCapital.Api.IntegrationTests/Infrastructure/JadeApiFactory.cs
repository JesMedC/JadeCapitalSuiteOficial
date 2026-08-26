using System.Collections.Concurrent;
using JadeCapital.Host;
using JadeCapital.Identity.Api;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Shared.Infrastructure.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary>Captured log lines for assertions about PII / body leaks.</summary>
    public ConcurrentQueue<string> CapturedLogs { get; } = new();

    /// <summary>The single InMemoryCapturingEmailSender registered for this fixture.</summary>
    public InMemoryCapturingEmailSender EmailSender { get; } = new(Microsoft.Extensions.Logging.Abstractions.NullLogger<InMemoryCapturingEmailSender>.Instance);

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
                // Default recovery permit is 5/hour per spec. Tests that need
                // more can override via WebApplicationFactory.WithWebHostBuilder.
                ["RateLimit:RecoveryPermit"] = "5",
                ["Cors:Origins:0"] = "http://localhost",
                ["Mail:Host"] = "localhost",
                ["Mail:Port"] = "1025",
                ["Mail:From"] = "test@jadecapital.test",
                ["Storage:Endpoint"] = "127.0.0.1:9000",
                ["Storage:AccessKey"] = "integration-access-key",
                ["Storage:SecretKey"] = "integration-secret-key",
                ["Storage:Bucket"] = "jade-integration-tests",
                ["Storage:Ssl"] = "false"
            });
        });

        // Replace the Mailpit production sender with the in-memory capturing
        // sender so integration tests can assert on captured messages.
        builder.ConfigureServices(services =>
        {
            var existing = services.Where(s => s.ServiceType == typeof(IEmailSender)).ToList();
            foreach (var s in existing) services.Remove(s);
            services.AddSingleton<IEmailSender>(EmailSender);

            // Keep unrelated integration tests fast. Recovery timing tests inject
            // a controllable gate, while unit tests exercise the production gate.
            var gateDescriptors = services.Where(s => s.ServiceType == typeof(IUniformTimingGate)).ToList();
            foreach (var s in gateDescriptors) services.Remove(s);
            services.AddSingleton<IUniformTimingGate>(new NoopTimingGate());

            // Tee the host's logger into CapturedLogs so log-leak assertions work.
            services.AddLogging(b =>
            {
                b.AddProvider(new TeeLoggerProvider(CapturedLogs));
            });
        });
    }

    /// <summary>No-op timing gate for tests. The prod gate is exercised by
    /// unit tests; integration tests prove shape, not 14s latency.</summary>
    private sealed class NoopTimingGate : IUniformTimingGate
    {
        public UniformTimingDeadline Begin() => default;
        public Task AwaitAsync(UniformTimingDeadline deadline, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Tees every log line into an in-memory list so tests can assert
    /// no plaintext / token / body ever leaks into logs.</summary>
    private sealed class TeeLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _sink;
        public TeeLoggerProvider(ConcurrentQueue<string> sink) { _sink = sink; }
        public ILogger CreateLogger(string categoryName) => new TeeLogger(categoryName, _sink);
        public void Dispose() { }
        private sealed class TeeLogger : ILogger
        {
            private readonly string _cat; private readonly ConcurrentQueue<string> _sink;
            public TeeLogger(string cat, ConcurrentQueue<string> sink) { _cat = cat; _sink = sink; }
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _sink.Enqueue($"[{logLevel}] {_cat}: {formatter(state, exception)}");
            }
            private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
        }
    }

    private async Task ApplyMigrationAsync()
    {
        await using var conn = new Npgsql.NpgsqlConnection(PostgresConnectionString);
        await conn.OpenAsync();

        // Habilitar extension citext para emails case-insensitive.
        await using (var enable = new Npgsql.NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS citext;", conn))
            await enable.ExecuteNonQueryAsync();

        // Apply every hand-authored SQL migration from infrastructure/postgres/migrations/.
        // Order is directory sort (0001, 0006, 0007). All scripts are idempotent
        // (CREATE TABLE/INDEX IF NOT EXISTS, ADD COLUMN IF NOT EXISTS).
        var migrationsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
            "infrastructure/postgres/migrations"));
        if (!Directory.Exists(migrationsDir))
            throw new DirectoryNotFoundException($"Migrations directory not found: {migrationsDir}");

        var files = Directory.GetFiles(migrationsDir, "*.sql").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToArray();
        foreach (var file in files)
        {
            var sql = await File.ReadAllTextAsync(file);
            try
            {
                await using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Npgsql.PostgresException ex)
            {
                throw new InvalidOperationException(
                    $"Migration {Path.GetFileName(file)} failed: {ex.Message} (SQL state {ex.SqlState})", ex);
            }
        }
    }
}
