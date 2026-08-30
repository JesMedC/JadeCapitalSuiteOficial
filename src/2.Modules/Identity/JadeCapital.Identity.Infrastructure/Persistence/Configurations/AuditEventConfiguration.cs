using JadeCapital.Identity.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF fluent configuration for the <see cref="AuditEvent"/> aggregate
/// (Wave 6, slice 6d.1 + Wave 11.2a hotfix — column rename).
///
/// <para>
/// Mirrors <c>audit.events</c> (8 columns + 3 indexes + 1 CHECK constraint)
/// declared in migration 0027_audit_events.sql + 0034 (rename
/// <c>changes</c> → <c>changes_json</c>). The table lives in the
/// <c>audit</c> schema (NOT <c>identity</c>) — the dedicated
/// <c>AuditDbContext</c> registers it there.
/// </para>
///
/// <para>
/// <b>Defense-in-depth</b>: this configuration is registered ONCE via
/// <c>AuditDbContext.OnModelCreating</c> + <c>ApplyConfiguration</c>. There
/// is no duplicate registration through DI (per the Wave 4 lesson — see
/// <c>ImportJobConfiguration</c> 5a.1 comment).
/// </para>
///
/// <para>
/// <b>Wave 11.2a hotfix (Bug #1)</b>: the column was originally named
/// <c>changes</c> (snake_case per the migration's own convention) but
/// the GDPR spec canon + production anonymizer + 11.1 tests all use
/// <c>changes_json</c>. Migration 0034 renames the column to match the
/// canon. The mapping below was updated to point at <c>changes_json</c>
/// in lock-step so EF's Npgsql provider reads + writes the renamed
/// column. Without this update, EF would attempt to read/write to the
/// old <c>changes</c> column (which no longer exists) and fail with
/// `42703: column "changes" of relation "events" does not exist` on
/// every audit insert. The migration + the EF config MUST ship
/// together — neither is safe without the other.
/// </para>
/// </summary>
internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> b)
    {
        // Schema + table name. The migration 0027 uses lowercase "events"
        // in the audit schema; EF defaults to the entity type name
        // (AuditEvent) unless overridden. Override here to keep the
        // schema and column names aligned with the migration.
        b.ToTable("events");
        b.HasKey(e => e.Id);

        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(AuditEvent.MaxEntityTypeLength)
            .IsRequired();
        b.Property(e => e.EntityId).HasColumnName("entity_id").IsRequired();
        b.Property(e => e.Action)
            .HasColumnName("action")
            .HasConversion<byte>()
            .IsRequired();
        b.Property(e => e.TenantId).HasColumnName("tenant_id");
        b.Property(e => e.UserId).HasColumnName("user_id");
        // Wave 11.2a hotfix (Bug #1): column was renamed `changes` →
        // `changes_json` by migration 0034 to align with the GDPR spec
        // canon + production anonymizer + 11.1 test fixtures. The
        // property name on the C# side stays `ChangesJson` (the C#
        // PascalCase identifier is unrelated to the DB snake_case name).
        b.Property(e => e.ChangesJson)
            .HasColumnName("changes_json")
            .HasColumnType("jsonb");
        b.Property(e => e.OccurredAt)
            .HasColumnName("occurred_at")
            .IsRequired();

        // Slice 6d.2: the audit log has a dedicated <c>occurred_at</c>
        // column (pinned from IClock.UtcNow at AuditEvent.Create). The
        // inherited <see cref="Entity{TId}.CreatedAt"/> +
        // <see cref="Entity{TId}.UpdatedAt"/> properties are NOT mapped
        // here — the <c>audit.events</c> table doesn't carry them
        // (see migration 0027). Without this Ignore, EF's default
        // convention would map them to PascalCase columns that don't
        // exist in the DB, causing SaveChanges to fail in production
        // AND making the SQLite integration tests throw
        // 'no such column: e.CreatedAt' on the first SELECT.
        b.Ignore(e => e.CreatedAt);
        b.Ignore(e => e.UpdatedAt);

        // DomainEvents not persisted (audit events don't raise domain events).
        b.Ignore(e => e.DomainEvents);

        // Indexes — align with the migration so EF doesn't generate
        // DROP/CREATE if a future slice uses `dotnet ef migrations add`.
        b.HasIndex(e => new { e.EntityType, e.EntityId })
            .HasDatabaseName("ix_audit_events_entity");
        b.HasIndex(e => new { e.TenantId, e.OccurredAt })
            .HasDatabaseName("ix_audit_events_tenant_time")
            .IsDescending(false, true);
        b.HasIndex(e => e.UserId)
            .HasDatabaseName("ix_audit_events_user");
    }
}
