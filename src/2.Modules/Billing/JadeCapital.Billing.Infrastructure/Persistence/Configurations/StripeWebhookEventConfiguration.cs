using JadeCapital.Billing.Domain.Stripe;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JadeCapital.Billing.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>billing.stripe_webhook_events</c> (Wave 6, slice 6a.2).
/// Mirrors migration 0023. The table is an append-only log of received
/// Stripe webhook events. The UNIQUE index on <c>event_id</c> is the DB-level
/// idempotency contract — re-delivery of the same <c>evt_...</c> fails the
/// INSERT, which the handler translates into a "duplicate" outcome.
/// </summary>
internal sealed class StripeWebhookEventConfiguration : IEntityTypeConfiguration<StripeWebhookEvent>
{
    public void Configure(EntityTypeBuilder<StripeWebhookEvent> b)
    {
        b.ToTable("stripe_webhook_events");
        b.HasKey(e => e.Id);

        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.EventId).HasColumnName("event_id").HasMaxLength(64).IsRequired();
        b.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(64).IsRequired();

        // payload_json is JSONB in Postgres; store as string + convert in
        // a future slice when EF Core 9 + Npgsql land the JSONB-as-string
        // bidirectional mapping we want. For 6a.2 the column is read-only
        // (we never query it from EF) — the gateway passes the raw payload
        // and the handler dispatches on Type.
        b.Property(e => e.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
        b.Property(e => e.SignatureHeader).HasColumnName("signature_header").HasMaxLength(256);
        b.Property(e => e.ReceivedAt).HasColumnName("received_at").IsRequired();
        b.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        b.Property(e => e.ProcessingError).HasColumnName("processing_error").HasMaxLength(2000);

        b.Ignore(e => e.DomainEvents);

        // UNIQUE (event_id) — idempotency dedup key. Mirrored in migration 0023.
        b.HasIndex(e => e.EventId)
            .IsUnique()
            .HasDatabaseName("ux_stripe_webhook_events_event_id");

        // Regular index on received_at — admin/forensics queries list
        // newest-first within a time window.
        b.HasIndex(e => e.ReceivedAt)
            .HasDatabaseName("ix_stripe_webhook_events_received_at")
            .IsDescending(true);
    }
}