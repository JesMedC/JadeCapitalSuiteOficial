using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.RiskProfile;
using JadeCapital.Identity.Domain.Users;
using JadeCapital.Identity.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Identity.Infrastructure.Persistence;

/// <summary>
/// DbContext del módulo Identity. Esquema dedicado "identity" dentro del schema "jade".
/// Tablas: identity.users, identity.refresh_tokens, identity.temporary_credentials, identity.password_history,
/// identity.risk_profiles (slice 1a.1b — single-active risk profile per user).
/// </summary>
public sealed class IdentityDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public IdentityDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<IdentityDbContext> options)
        : base(options) { }

    public Microsoft.EntityFrameworkCore.DbSet<User> Users => Set<User>();
    public Microsoft.EntityFrameworkCore.DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public Microsoft.EntityFrameworkCore.DbSet<TemporaryCredential> TemporaryCredentials => Set<TemporaryCredential>();
    public Microsoft.EntityFrameworkCore.DbSet<PasswordHistoryEntry> PasswordHistory => Set<PasswordHistoryEntry>();
    public Microsoft.EntityFrameworkCore.DbSet<RiskProfile> RiskProfiles => Set<RiskProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new TemporaryCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new PasswordHistoryEntryConfiguration());
        modelBuilder.ApplyConfiguration(new RiskProfileConfiguration());
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);

        b.Property(u => u.Id).HasColumnName("id");
        b.Property(u => u.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        b.Property(u => u.DisplayName).HasColumnName("display_name").HasMaxLength(80).IsRequired();
        b.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
        b.Property(u => u.Role).HasColumnName("role").HasConversion<int>().IsRequired();
        b.Property(u => u.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(u => u.EmailConfirmedAt).HasColumnName("email_confirmed_at");
        b.Property(u => u.LastLoginAt).HasColumnName("last_login_at");
        b.Property(u => u.FailedLoginCount).HasColumnName("failed_login_count").IsRequired();
        b.Property(u => u.LockedUntil).HasColumnName("locked_until");
        b.Property(u => u.Timezone).HasColumnName("timezone").HasMaxLength(64);
        b.Property(u => u.SessionVersion).HasColumnName("session_version").IsRequired();
        b.Property(u => u.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(u => u.UpdatedAt).HasColumnName("updated_at");

        // PasswordHistory is exposed as an ordered projection on the aggregate;
        // EF materialises the backing list directly.
        b.HasMany<PasswordHistoryEntry>("_passwordHistory")
            .WithOne()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation("_passwordHistory")
            .Metadata.SetField("_passwordHistory");
        b.Navigation("_passwordHistory")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(u => u.DomainEvents);

        // Email unico. Se normaliza a lowercase en el handler, asi que unique aqui es seguro.
        b.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ux_users_email");
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(r => r.Id);

        b.Property(r => r.Id).HasColumnName("id");
        b.Property(r => r.UserId).HasColumnName("user_id").IsRequired();
        b.Property(r => r.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
        b.Property(r => r.IssuedAt).HasColumnName("issued_at").IsRequired();
        b.Property(r => r.ExpiresAt).HasColumnName("expires_at").IsRequired();
        b.Property(r => r.RevokedAt).HasColumnName("revoked_at");
        b.Property(r => r.ReplacedByTokenId).HasColumnName("replaced_by_token_id");
        b.Property(r => r.CreatedByIp).HasColumnName("created_by_ip").HasMaxLength(45);
        b.Property(r => r.UserAgent).HasColumnName("user_agent").HasMaxLength(500);
        b.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(r => r.UpdatedAt).HasColumnName("updated_at");

        // FK to User. Without this EF Core doesn't know about the relationship and
        // may insert refresh_tokens BEFORE users in the same SaveChanges, which
        // violates the FK constraint at the DB level (FK is defined in the SQL migration).
        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(r => r.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_hash");
        b.HasIndex(r => new { r.UserId, r.RevokedAt }).HasDatabaseName("ix_refresh_tokens_user_active");
    }
}