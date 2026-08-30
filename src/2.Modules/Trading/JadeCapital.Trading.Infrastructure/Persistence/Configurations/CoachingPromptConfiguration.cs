using JadeCapital.Trading.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence.Configurations;

// ============================================================================
//  CoachingPromptConfiguration — slice 5b.2 (Wave 5).
//
//  EF fluent configuration for the <see cref="CoachingPrompt"/> aggregate.
//  Mirrors <c>trading.coaching_prompts_ai</c> (10 columns + 1 index)
//  declared in migration 0020_coaching_prompts_ai.sql. snake_case columns +
//  jsonb mappings for context_json + provider_response keep the schema
//  identical to the DB.
//
//  PII note: the JSONB columns DO carry serialized user data, but only the
//  aggregates from <c>UserTradingContext</c> — no email / displayName /
//  absolute P&L. The serialization happens in
//  <c>GenerateCoachingPromptHandler</c>, and the template
//  (<c>CoachingPromptTemplate.Render</c>) guarantees the aggregates are
//  the only thing that crosses the boundary.
// ============================================================================

internal sealed class CoachingPromptConfiguration : IEntityTypeConfiguration<CoachingPrompt>
{
    public void Configure(EntityTypeBuilder<CoachingPrompt> b)
    {
        b.ToTable("coaching_prompts_ai");
        b.HasKey(p => p.Id);

        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.UserId).HasColumnName("user_id").IsRequired();
        b.Property(p => p.PromptText).HasColumnName("prompt_text").HasMaxLength(4000).IsRequired();
        b.Property(p => p.ContextJson).HasColumnName("context_json").HasColumnType("jsonb").IsRequired();
        b.Property(p => p.ProviderResponseText).HasColumnName("provider_response").HasColumnType("jsonb").IsRequired();
        b.Property(p => p.Model).HasColumnName("model").HasMaxLength(64).IsRequired();
        b.Property(p => p.LatencyMs).HasColumnName("latency_ms").IsRequired();
        b.Property(p => p.Severity).HasColumnName("severity").HasConversion<byte>().IsRequired();
        b.Property(p => p.Kind).HasColumnName("kind").HasConversion<byte>().IsRequired();

        // Audit columns inherited from AggregateRoot → Entity<TId>.
        b.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        // DomainEvents not persisted.
        b.Ignore(p => p.DomainEvents);

        // Index — drives GET /api/coaching/ai-prompts?period=...
        b.HasIndex(p => new { p.UserId, p.CreatedAt })
            .HasDatabaseName("ix_coaching_ai_user_created")
            .IsDescending(false, true);
    }
}