using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Identity.Infrastructure.Persistence.Configurations;

internal sealed class TemporaryCredentialConfiguration : IEntityTypeConfiguration<TemporaryCredential>
{
    public void Configure(EntityTypeBuilder<TemporaryCredential> b)
    {
        b.ToTable("temporary_credentials");
        b.HasKey(t => t.Id);

        b.Property(t => t.Id).HasColumnName("id");
        b.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
        b.Property(t => t.Generation).HasColumnName("generation").IsRequired();
        b.Property(t => t.Hash).HasColumnName("hash").HasMaxLength(255).IsRequired();
        b.Property(t => t.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(t => t.ActivatedAt).HasColumnName("activated_at");
        b.Property(t => t.ExpiresAt).HasColumnName("expires_at").IsRequired();
        b.Property(t => t.ConsumedAt).HasColumnName("consumed_at");
        b.Property(t => t.SupersededAt).HasColumnName("superseded_at");
        b.Property(t => t.GrantJti).HasColumnName("grant_jti").HasMaxLength(64);
        b.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // CAS activation uses this for ordering. (user, generation) is unique.
        b.HasIndex(t => new { t.UserId, t.Generation })
            .IsUnique()
            .HasDatabaseName("ux_temporary_credentials_user_generation");

        // Lookup the latest row for a user ordered by generation DESC.
        b.HasIndex(t => new { t.UserId, t.Generation })
            .HasDatabaseName("ix_temporary_credentials_user_generation_desc")
            .IsDescending(false, true);

        // Sweep index for the 7-day purge of consumed/expired rows.
        b.HasIndex(t => t.ExpiresAt)
            .HasDatabaseName("ix_temporary_credentials_expires_at");

        // Latest-only persistence foundation: at most ONE Activated row per
        // user at any time. Enforced by a unique partial index — the database
        // rejects a second Activated row even if the application race-condition
        // window opens. Slice 0b supersedes any older Activated row inside
        // the same DB transaction that reserves a new Pending row.
        b.HasIndex(t => t.UserId)
            .IsUnique()
            .HasDatabaseName("ux_temporary_credentials_user_active")
            .HasFilter("status = 'Activated'");
    }
}
