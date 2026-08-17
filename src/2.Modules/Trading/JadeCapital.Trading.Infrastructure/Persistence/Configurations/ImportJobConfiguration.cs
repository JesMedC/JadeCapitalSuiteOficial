using JadeCapital.Trading.Domain.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF fluent configuration for the <see cref="ImportJob"/> aggregate.
/// Mirrors <c>trading.import_jobs</c> (16 columns + 2 indexes) declared in
/// migration 0019_import_jobs.sql. snake_case columns + smallint enums +
/// status byte conversion keep the schema identical to the DB.
/// </summary>
internal sealed class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> b)
    {
        b.ToTable("import_jobs");
        b.HasKey(j => j.Id);

        b.Property(j => j.Id).HasColumnName("id");
        b.Property(j => j.UserId).HasColumnName("user_id").IsRequired();
        b.Property(j => j.AccountId).HasColumnName("account_id").IsRequired();
        b.Property(j => j.Format).HasColumnName("format").HasConversion<byte>().IsRequired();
        b.Property(j => j.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
        b.Property(j => j.FileSizeBytes).HasColumnName("file_size_bytes").IsRequired();
        b.Property(j => j.FileSha256).HasColumnName("file_sha256").HasMaxLength(64).IsRequired();
        b.Property(j => j.Status).HasColumnName("status").HasConversion<byte>().IsRequired();
        b.Property(j => j.RowsTotal).HasColumnName("rows_total").IsRequired();
        b.Property(j => j.RowsImported).HasColumnName("rows_imported").IsRequired();
        b.Property(j => j.RowsSkipped).HasColumnName("rows_skipped").IsRequired();
        b.Property(j => j.RowsErrored).HasColumnName("rows_errored").IsRequired();
        b.Property(j => j.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
        b.Property(j => j.StartedAt).HasColumnName("started_at").IsRequired();
        b.Property(j => j.FinishedAt).HasColumnName("finished_at");

        // Audit columns inherited from AggregateRoot → Entity<TId>.
        b.Property(j => j.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(j => j.UpdatedAt).HasColumnName("updated_at");

        // DomainEvents not are persisted.
        b.Ignore(j => j.DomainEvents);

        // Indexes — align with the migration so EF doesn't generate DROP/CREATE
        // if a future slice uses `dotnet ef migrations add`.
        b.HasIndex(j => new { j.UserId, j.Status })
            .HasDatabaseName("ix_import_jobs_user_status");
        b.HasIndex(j => j.FileSha256)
            .HasDatabaseName("ix_import_jobs_sha")
            .HasFilter("status IN (0, 1, 2)");
    }
}