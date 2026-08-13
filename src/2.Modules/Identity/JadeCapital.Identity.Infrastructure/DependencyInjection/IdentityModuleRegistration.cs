using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Infrastructure.BackgroundJobs;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JadeCapital.Identity.Infrastructure.DependencyInjection;

public static class IdentityModuleRegistration
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ===== EF Core =====
        // Use factory pattern so the connection string is resolved AFTER host build
        // (this allows WebApplicationFactory tests to inject in-memory config first).
        services.AddDbContext<IdentityDbContext>((sp, opts) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var pgConn = cfg.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("ConnectionStrings:Postgres required.");
            opts.UseNpgsql(pgConn, npg =>
                npg.MigrationsHistoryTable("__ef_migrations", "identity"));
        });

        // ===== Repos =====
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ITemporaryCredentialRepository, TemporaryCredentialRepository>();
        services.AddScoped<IPasswordHistoryRepository, PasswordHistoryRepository>();
        services.AddScoped<IRefreshTokenRevoker, RefreshTokenRevoker>();
        services.AddSingleton<IDistributedLock, InMemoryDistributedLock>();
        services.AddScoped<IUnitOfWork, IdentityUnitOfWork>();

        // ===== Security =====
        services.AddSingleton<IPasswordHasher>(_ =>
            new Pbkdf2PasswordHasher(iterations: configuration.GetValue<int?>("Security:Pbkdf2Iterations") ?? 100_000));
        services.AddSingleton<ITokenService, JwtTokenService>();

        // ===== Background services =====
        services.AddHostedService<RefreshTokenCleanupService>();

        return services;
    }
}