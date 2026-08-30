using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.RiskProfile;
using JadeCapital.Identity.Domain.Tenants;
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
    public Microsoft.EntityFrameworkCore.DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new TemporaryCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new PasswordHistoryEntryConfiguration());
        modelBuilder.ApplyConfiguration(new RiskProfileConfiguration());
        // Wave 6, slice 6c.1 — tenants table + tenant_id on users.
        modelBuilder.ApplyConfiguration(new TenantConfiguration());
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

        // Wave 11.2a hotfix (Bug #2): map the Wave 10.5 lifecycle +
        // consent properties to the snake_case columns declared in
        // migration 0033 + (future) consent migrations. Without these
        // mappings, EF would attempt to read/write PascalCase columns
        // (`"SoftDeletedAt"`, `"ScheduledHardDeleteAt"`, etc.) that do
        // NOT exist in the DB schema. Production HotDeleteSweep LINQ
        // (`u.ScheduledHardDeleteAt <= cutoff`) throws at translation
        // time with `42703: column u.ScheduledHardDeleteAt does not
        // exist` on the very first cycle — this fix closes that gap.
        //
        // Column names + nullability:
        //   - soft_deleted_at              TIMESTAMPTZ NULL
        //   - scheduled_hard_delete_at     TIMESTAMPTZ NULL  (0033)
        //   - accepted_terms_version       VARCHAR(64) NULL  (slice 10.5)
        //   - accepted_privacy_version     VARCHAR(64) NULL  (slice 10.5)
        //   - accepted_at                  TIMESTAMPTZ NULL  (slice 10.5)
        //   - welcome_email_sent_at        TIMESTAMPTZ NULL  (0036)
        //   - terms_accepted_at            TIMESTAMPTZ NULL  (0037)
        //   - privacy_accepted_at          TIMESTAMPTZ NULL  (0037)
        //   - consent_ip                   VARCHAR(45) NULL  (0037)
        //   - cookie_consent_accepted_at   TIMESTAMPTZ NULL  (0038)
        //   - cookie_consent_choice        VARCHAR(16) NULL  (0038)
        b.Property(u => u.SoftDeletedAt).HasColumnName("soft_deleted_at");
        b.Property(u => u.ScheduledHardDeleteAt).HasColumnName("scheduled_hard_delete_at");
        b.Property(u => u.AcceptedTermsVersion).HasColumnName("accepted_terms_version").HasMaxLength(64);
        b.Property(u => u.AcceptedPrivacyVersion).HasColumnName("accepted_privacy_version").HasMaxLength(64);
        b.Property(u => u.AcceptedAt).HasColumnName("accepted_at");
        // Slice 11.3 schema-only addition (0036) — now mapped in 11.4
        // so RegisterUserHandler can persist the idempotency flag.
        b.Property(u => u.WelcomeEmailSentAt).HasColumnName("welcome_email_sent_at");
        // Slice 11.4 — GDPR Art. 7 consent ledger (0037) + ePrivacy
        // Directive cookie consent (0038).
        b.Property(u => u.TermsAcceptedAt).HasColumnName("terms_accepted_at");
        b.Property(u => u.PrivacyAcceptedAt).HasColumnName("privacy_accepted_at");
        b.Property(u => u.ConsentIp).HasColumnName("consent_ip").HasMaxLength(45);
        b.Property(u => u.CookieConsentAcceptedAt).HasColumnName("cookie_consent_accepted_at");
        b.Property(u => u.CookieConsentChoice).HasColumnName("cookie_consent_choice").HasMaxLength(16);

        // Wave 6, slice 6c.1 — tenant membership. Column is NULLABLE
        // (per the "ONE migration atómica" user decision). NOT NULL
        // lands in slice 6c.3 after the 6c.2 backfill. The FK is
        // created by the SQL migration (0025) — EF will not emit a
        // duplicate because we don't call HasOne here.
        // The TenantId record wraps a Guid; EF stores the inner Guid
        // directly. TenantId is a reference type (record), so the
        // nullable annotation is honored at the converter level — null
        // in -> Guid? null in DB, non-null in -> inner Guid in DB.
        var tenantIdConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<
            JadeCapital.Shared.Kernel.MultiTenancy.TenantId?,
            Guid?>(
            v => v == null ? null : (Guid?)v.Value,  // v is TenantId?; v.Value is the inner Guid
            v => v == null
                ? null
                : new JadeCapital.Shared.Kernel.MultiTenancy.TenantId(v.Value));
        b.Property(u => u.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(tenantIdConverter);

        // Slice 4d — attachment quota + cached usage (migration 0018).
        // Identity owns the columns; Trading reads them via
        // IAttachmentQuotaReader (Contracts projection). Identity writes
        // happen in two places: ConfirmAttachmentUploadedHandler (best-effort
        // increment) and AttachmentLifecycleService (daily sweep recompute).
        b.Property(u => u.AttachmentQuotaBytes).HasColumnName("attachment_quota_bytes").IsRequired();
        b.Property(u => u.AttachmentUsedBytes).HasColumnName("attachment_used_bytes").IsRequired();

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