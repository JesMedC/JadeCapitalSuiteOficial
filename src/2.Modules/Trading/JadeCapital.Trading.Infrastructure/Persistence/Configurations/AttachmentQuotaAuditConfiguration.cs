using JadeCapital.Trading.Domain.AttachmentAudits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="AttachmentQuotaAudit"/> — slice 4d (Wave 4).
/// Maps to <c>trading.attachments_quota_audit</c> (migration 0018).
/// </summary>
internal sealed class AttachmentQuotaAuditConfiguration : IEntityTypeConfiguration<AttachmentQuotaAudit>
{
    public void Configure(EntityTypeBuilder<AttachmentQuotaAudit> b)
    {
        b.ToTable("attachments_quota_audit");
        b.HasKey(a => a.Id);

        b.Property(a => a.Id).HasColumnName("id");
        b.Property(a => a.UserId).HasColumnName("user_id").IsRequired();
        b.Property(a => a.RanAt).HasColumnName("ran_at").IsRequired();
        b.Property(a => a.CleanedCount).HasColumnName("cleaned_count").IsRequired();
        b.Property(a => a.CleanedBytes).HasColumnName("cleaned_bytes").IsRequired();
        b.Property(a => a.RemainingCount).HasColumnName("remaining_count").IsRequired();
        b.Property(a => a.RemainingBytes).HasColumnName("remaining_bytes").IsRequired();
        b.Property(a => a.SkippedReason).HasColumnName("skipped_reason").HasMaxLength(64);
        b.Property(a => a.ErrorMessage).HasColumnName("error_message");

        b.Ignore(a => a.DomainEvents);

        // FK cross-schema to identity.users(id) ON DELETE CASCADE — enforced
        // by migration 0018 SQL. We don't declare HasOne<User>() because
        // Trading.Infrastructure does not depend on Identity.Domain (Clean
        // Architecture). The user_id column is enough; EF skips FK validation.

        // Mirrors migration 0018 indexes.
        b.HasIndex(a => a.RanAt).HasDatabaseName("ix_attachments_quota_audit_ran_at").IsDescending(true);
        b.HasIndex(a => a.UserId).HasDatabaseName("ix_attachments_quota_audit_user");
    }
}