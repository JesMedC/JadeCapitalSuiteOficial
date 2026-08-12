using JadeCapital.Identity.Domain.Authentication;
using JadeCapital.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Identity.Infrastructure.Persistence.Configurations;

internal sealed class PasswordHistoryEntryConfiguration : IEntityTypeConfiguration<PasswordHistoryEntry>
{
    public void Configure(EntityTypeBuilder<PasswordHistoryEntry> b)
    {
        b.ToTable("password_history");
        b.HasKey(p => p.Id);

        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.UserId).HasColumnName("user_id").IsRequired();
        b.Property(p => p.Hash).HasColumnName("hash").HasMaxLength(255).IsRequired();
        b.Property(p => p.ChangedAt).HasColumnName("changed_at").IsRequired();
        b.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Read pattern: per-user newest-first. Matches the (changed_at DESC, id DESC) ordering
        // used by the domain invariant and the Application-side reuse-rejection lookup.
        // Id DESC is the deterministic tie-breaker when two entries share the same ChangedAt.
        b.HasIndex(p => new { p.UserId, p.ChangedAt, p.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_password_history_user_changed_at_id_desc");
    }
}
