using JadeCapital.Trading.Domain.Alerts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence.Configurations;

// ============================================================================
//  AlertConfiguration — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Mapeo del aggregate Alert a la tabla trading.alerts. La tabla la crea
//  la migration 0015b_alerts.sql; este configuration se alinea con los
//  nombres de columnas (snake_case) y los tipos para que EF no genere
//  DROP/CREATE adicionales si alguna vez se usa dotnet-ef migrations add.
//
//  Persistencia:
//   - UserId: cross-schema FK a identity.users (la migration SQL lo crea
//     con ON DELETE CASCADE). Aqui solo declaramos HasIndex.
//   - RuleId: VARCHAR(64). El aggregate enforce MaxRuleIdLength.
//   - Severity: SMALLINT via byte cast (Coaching.Severity = 1..3).
//   - Title: VARCHAR(120) cap (aggregate MaxTitleLength).
//   - Body: VARCHAR(500) cap (aggregate MaxBodyLength).
//   - CtaRoute + CtaLabel: VARCHAR(255) + VARCHAR(64) caps.
//   - AcknowledgedAt + ExpiresAt: TIMESTAMPTZ nullable.
//   - CreatedAt: TIMESTAMPTZ NOT NULL DEFAULT now().
//
//  Indexes:
//   - ix_alerts_user_active (user_id) WHERE acknowledged_at IS NULL
//   - ix_alerts_user_created (user_id, created_at DESC)
//   - ux_alerts_user_rule_day UNIQUE (user_id, rule_id, UTC-date)
//     vive SOLO en la migration SQL porque EF no soporta functional
//     indexes sobre ((created_at AT TIME ZONE 'UTC')::date) sin SQL crudo.
// ============================================================================

internal sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> b)
    {
        b.ToTable("alerts");
        b.HasKey(a => a.Id);

        b.Property(a => a.Id).HasColumnName("id");
        b.Property(a => a.UserId).HasColumnName("user_id").IsRequired();
        b.Property(a => a.RuleId).HasColumnName("rule_id").HasMaxLength(Alert.MaxRuleIdLength).IsRequired();

        // Severity: byte-backed. Stored as SMALLINT to match the spec's
        // `severity SMALLINT CHECK (severity BETWEEN 1 AND 3)`.
        b.Property(a => a.Severity).HasColumnName("severity").HasConversion<byte>().IsRequired();

        b.Property(a => a.Title).HasColumnName("title").HasMaxLength(Alert.MaxTitleLength).IsRequired();
        b.Property(a => a.Body).HasColumnName("body").HasMaxLength(Alert.MaxBodyLength).IsRequired();
        b.Property(a => a.CtaRoute).HasColumnName("cta_route").HasMaxLength(255).IsRequired();
        b.Property(a => a.CtaLabel).HasColumnName("cta_label").HasMaxLength(64).IsRequired();

        b.Property(a => a.AcknowledgedAt).HasColumnName("acknowledged_at");
        b.Property(a => a.ExpiresAt).HasColumnName("expires_at");
        b.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        b.Ignore(a => a.DomainEvents);

        // Indexes. The partial UNIQUE on (user_id, rule_id, UTC-date) is
        // intentionally NOT modeled here (functional date index requires
        // raw SQL). See migration 0015b_alerts.sql.
        b.HasIndex(a => new { a.UserId, a.CreatedAt })
            .HasDatabaseName("ix_alerts_user_created")
            .IsDescending(false, true);
        b.HasIndex(a => a.UserId)
            .HasDatabaseName("ix_alerts_user_active")
            .HasFilter("acknowledged_at IS NULL");
    }
}