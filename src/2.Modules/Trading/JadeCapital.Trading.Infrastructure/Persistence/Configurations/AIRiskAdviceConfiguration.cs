using JadeCapital.Trading.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Trading.Infrastructure.Persistence.Configurations;

// ============================================================================
//  AIRiskAdviceConfiguration — slice 5c.1 (Wave 5, AI Risk Advisor Pre-Trade).
//
//  EF fluent configuration for the AIRiskAdvice aggregate. Mirrors the
//  trading.ai_risk_advice table (10 columns + 1 index) declared in
//  migration 0021_ai_risk_advice.sql. snake_case columns + jsonb mappings
//  for context_json + provider_response keep the schema identical to the DB.
//
//  PII note: the JSONB columns DO carry serialized user data, but only the
//  aggregates from UserTradingContext — no email / displayName / absolute
//  P&L. The serialization happens in OllamaAIRiskAdvisor, and the prompt
//  template (AIRiskAdvisorPrompt.Render) guarantees the aggregates are
//  the only thing that crosses the AI boundary.
// ============================================================================

internal sealed class AIRiskAdviceConfiguration : IEntityTypeConfiguration<AIRiskAdvice>
{
    public void Configure(EntityTypeBuilder<AIRiskAdvice> b)
    {
        b.ToTable("ai_risk_advice");
        b.HasKey(a => a.Id);

        b.Property(a => a.Id).HasColumnName("id");
        b.Property(a => a.UserId).HasColumnName("user_id").IsRequired();
        b.Property(a => a.TradeId).HasColumnName("trade_id");
        b.Property(a => a.ContextJson).HasColumnName("context_json").HasColumnType("jsonb").IsRequired();
        b.Property(a => a.ProviderResponseText).HasColumnName("provider_response").HasColumnType("jsonb").IsRequired();
        b.Property(a => a.ParsedAction).HasColumnName("parsed_action").HasConversion<byte>().IsRequired();
        b.Property(a => a.Reason).HasColumnName("reason").HasMaxLength(500);
        b.Property(a => a.Model).HasColumnName("model").HasMaxLength(64).IsRequired();
        b.Property(a => a.LatencyMs).HasColumnName("latency_ms").IsRequired();

        // Audit columns inherited from AggregateRoot → Entity<TId>.
        b.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Ignore(a => a.UpdatedAt);

        // DomainEvents not persisted.
        b.Ignore(a => a.DomainEvents);

        // Index — drives GET /api/ai/risk-advice/{tradeId} (user+trade lookup).
        b.HasIndex(a => new { a.UserId, a.TradeId })
            .HasDatabaseName("ix_ai_risk_advice_user_trade");
    }
}
