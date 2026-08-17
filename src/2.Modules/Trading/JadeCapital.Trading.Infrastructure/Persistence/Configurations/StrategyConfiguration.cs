using JadeCapital.Trading.Domain.Strategies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence.Configurations;

// ============================================================================
//  StrategyConfiguration — slice 3a (Trader Strategies + Alerts + Planner).
//
//  Mapeo del aggregate Strategy a la tabla trading.strategies. La tabla
//  la crea la migration SQL 0015a; este configuration se alinea con los
//  nombres de columnas (snake_case) y los tipos para que EF no genere
//  DROP/CREATE adicionales si alguna vez se usa dotnet-ef migrations add.
//
//  Persistencia:
//   - UserId: cross-schema FK a identity.users (la migration SQL lo crea
//     con ON DELETE CASCADE). Aqui solo declaramos HasIndex para que EF
//     no intente recrearlo.
//   - Symbol: VARCHAR(20) nullable. Se persiste como string (la validacion
//     contra trading.instruments vive en application, no en la DB).
//   - Timeframe: SMALLINT nullable via byte? converter. Rango 1..9 en
//     el dominio; la migration enforce 1..10 como red de seguridad.
//   - IsActive: BOOLEAN NOT NULL DEFAULT TRUE.
//   - DomainEvents no se persiste (borrado via b.Ignore).
// ============================================================================

internal sealed class StrategyConfiguration : IEntityTypeConfiguration<Strategy>
{
    public void Configure(EntityTypeBuilder<Strategy> b)
    {
        b.ToTable("strategies");
        b.HasKey(s => s.Id);

        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.UserId).HasColumnName("user_id").IsRequired();

        b.Property(s => s.Name).HasColumnName("name").HasMaxLength(Strategy.MaxNameLength).IsRequired();
        b.Property(s => s.Description).HasColumnName("description").HasMaxLength(Strategy.MaxDescriptionLength);
        b.Property(s => s.Symbol).HasColumnName("symbol").HasMaxLength(20);
        b.Property(s => s.Timeframe).HasColumnName("timeframe").HasConversion<byte?>();
        b.Property(s => s.Rules).HasColumnName("rules").HasColumnType("text");

        b.Property(s => s.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        b.Ignore(s => s.DomainEvents);

        // Indexes. Solo declaramos los que EF puede modelar:
        //   - ix_strategies_user_active (user_id) WHERE is_active=true — query
        //     principal del FE (listado de strategies activas del user).
        // El partial UNIQUE INDEX ux_strategies_user_name_active (user_id,
        // lower(name)) WHERE is_active=true vive SOLO en la migration SQL
        // porque EF no soporta functional indexes sobre lower() sin SQL crudo.
        // Si en algun momento se usa dotnet-ef migrations add, ese index
        // tendra que agregarse a mano al scaffold.
        b.HasIndex(s => s.UserId).HasDatabaseName("ix_strategies_user_active")
            .HasFilter("is_active = true");
    }
}