using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.Journal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeo de <see cref="JournalEntry"/> a la tabla <c>trading.journal_entries</c>
/// (slice 2a.1).
///
/// Persistencia:
/// <list type="bullet">
///   <item><c>LocalDate</c> es un <c>readonly record struct</c> con
///   Year/Month/Day. Se persiste como DATE via value converter
///   (LocalDate -&gt; DateOnly -&gt; DATE column).</item>
///   <item><c>Mood?</c> es un VO struct con byte value. Se persiste como
///   SMALLINT NULL via value converter (Mood? -&gt; byte?).</item>
///   <item><c>Tags</c> (<c>IReadOnlyList&lt;string&gt;</c>) se persiste
///   como <c>TEXT[]</c> (Npgsql native array). ValueComparer custom
///   para change tracking de colecciones.</item>
///   <item>El FK a <c>identity.users</c> es cross-schema; la migration
///   SQL lo crea con ON DELETE CASCADE. Aqui solo declaramos la
///   cardinalidad via shadow navigation (sin HasOne explicito — EF
///   no puede crear la FK cross-schema limpiamente, pero la migration
///   SQL ya la enforced).</item>
///   <item>El UNIQUE INDEX sobre (user_id, local_date) lo crea la
///   migration SQL; EF lo declara via HasIndex para que no intente
///   recrearlo en runtime.</item>
/// </list>
/// </summary>
internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> b)
    {
        b.ToTable("journal_entries");
        b.HasKey(j => j.Id);

        b.Property(j => j.Id).HasColumnName("id");
        b.Property(j => j.UserId).HasColumnName("user_id").IsRequired();

        b.Property(j => j.LocalDate)
            .HasColumnName("local_date")
            .HasConversion(
                v => v.ToDateOnly(),
                v => LocalDate.From(v))
            .IsRequired();

        b.Property(j => j.Timezone)
            .HasColumnName("timezone")
            .HasMaxLength(64)
            .IsRequired();

        b.Property(j => j.MoodPre)
            .HasColumnName("mood_pre")
            .HasConversion<byte?>(
                v => v.HasValue ? v.Value.Value : null,
                v => v.HasValue ? Mood.FromTrusted(v.Value) : null)
            .IsRequired(false);

        b.Property(j => j.MoodDuring)
            .HasColumnName("mood_during")
            .HasConversion<byte?>(
                v => v.HasValue ? v.Value.Value : null,
                v => v.HasValue ? Mood.FromTrusted(v.Value) : null)
            .IsRequired(false);

        b.Property(j => j.MoodPost)
            .HasColumnName("mood_post")
            .HasConversion<byte?>(
                v => v.HasValue ? v.Value.Value : null,
                v => v.HasValue ? Mood.FromTrusted(v.Value) : null)
            .IsRequired(false);

        b.Property(j => j.PremarketPlan)
            .HasColumnName("premarket_plan")
            .HasColumnType("text")
            .IsRequired(false);

        b.Property(j => j.PostmarketReflection)
            .HasColumnName("postmarket_reflection")
            .HasColumnType("text")
            .IsRequired(false);

        // Tags como TEXT[] (Npgsql native array). EF trata IReadOnlyList<string>
        // como una coleccion inmutable; el ValueComparer custom le dice
        // cuando dos instancias son iguales (SequenceEqual) para change tracking.
        b.Property(j => j.Tags)
            .HasColumnName("tags")
            .HasColumnType("text[]")
            .HasConversion(
                v => v.ToArray(),
                v => v)
            .IsRequired(false)
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<string>>(
                (a, b) => (a == null && b == null)
                          || (a != null && b != null && a.SequenceEqual(b)),
                v => v.Aggregate(0, (acc, s) => HashCode.Combine(acc, s.GetHashCode())),
                v => (IReadOnlyList<string>)v.ToArray()));

        b.Property(j => j.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(j => j.UpdatedAt).HasColumnName("updated_at").IsRequired(false);

        b.Ignore(j => j.DomainEvents);

        // FK cross-schema a identity.users. La migration SQL lo crea con
        // ON DELETE CASCADE; aqui solo declaramos el nombre del index
        // para que EF no genere un DROP/CREATE adicional.
        b.HasIndex(j => new { j.UserId, j.LocalDate })
            .IsUnique()
            .HasDatabaseName("ux_journal_user_date");

        b.HasIndex(j => new { j.UserId, j.CreatedAt })
            .HasDatabaseName("ix_journal_user_created");
    }
}
