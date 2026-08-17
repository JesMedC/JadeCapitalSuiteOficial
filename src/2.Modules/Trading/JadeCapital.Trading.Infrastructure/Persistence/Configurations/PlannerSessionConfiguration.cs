using JadeCapital.Trading.Domain.Planner;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence.Configurations;

// ============================================================================
//  PlannerSessionConfiguration — slice 3c.
//
//  Snake_case mapping. Las reglas de negocio (notes length, end > start,
//  status range, FK cross-schema) viven en la migration 0015c_planner.sql
//  como CHECK constraints y FKs; este Configuration replica los nombres de
//  columnas y el shadow FK de Instrument.code (ON DELETE SET NULL) ya
//  fue agregado por la migration.
// ============================================================================

internal sealed class PlannerSessionConfiguration : IEntityTypeConfiguration<PlannerSession>
{
    public void Configure(EntityTypeBuilder<PlannerSession> b)
    {
        b.ToTable("planner_sessions");
        b.HasKey(s => s.Id);

        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
        b.Property(s => s.SessionDate).HasColumnName("session_date").IsRequired();
        b.Property(s => s.PlannedStartTime).HasColumnName("planned_start_time");
        b.Property(s => s.PlannedEndTime).HasColumnName("planned_end_time");
        b.Property(s => s.Symbol).HasColumnName("symbol").HasMaxLength(20);
        b.Property(s => s.Status).HasColumnName("status").HasConversion<byte>().IsRequired();
        b.Property(s => s.SessionDate).HasColumnName("session_date").HasConversion(v => v.ToDateOnly(), v => JadeCapital.Shared.Kernel.Time.LocalDate.From(v));
        b.Property(s => s.Notes).HasColumnName("notes").HasMaxLength(500);
        b.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(s => s.UpdatedAt).HasColumnName("updated_at");
    }
}
